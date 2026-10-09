using System.Text;

namespace CasCap.Tests.Unit;

/// <summary>
/// Assembles a <see cref="CommunicationsBgService"/> from deterministic fakes and drives it through
/// its public <see cref="CommunicationsBgService.ExecuteAsync"/> surface.
/// </summary>
/// <remarks>
/// The service exposes no other entry point, so the fixture starts the real execution pipeline —
/// stream consumer, reply drain loop and subscription — and the tests observe it through
/// <see cref="Signalizr"/> and <see cref="Redis"/>. The responder is a real <see cref="AgentCommsResponder"/>
/// over the typed Agent Runtime client and a fake HTTP handler whose default response has no reply text.
/// </remarks>
/// <param name="voiceMode">The voice-processing mode under test.</param>
/// <param name="responderEnabled">Whether an agent responder answers, enabling the reply queue.</param>
/// <param name="replyQueueCapacity">Bound applied to the reply queue.</param>
/// <param name="echoTranscriptToDebugChat">Whether voice transcripts are echoed to the monitor group.</param>
/// <param name="voiceReplyMode">The spoken-reply mode under test.</param>
/// <param name="formatter">Formatter for directly forwarded stream events; defaults to <see cref="PlainCommsEventFormatter"/>.</param>
/// <param name="streamSendBurst">Token-bucket capacity for stream-originated sends.</param>
/// <param name="streamSendRatePerMinute">Token-bucket refill rate for stream-originated sends.</param>
/// <param name="monitorSources">Stream event sources routed to the monitor group.</param>
/// <param name="streamEventTurnsEnabled">Whether chat-bound stream events become responder turns.</param>
public sealed class CommunicationsBgServiceTestFixture(
    VoiceProcessingMode voiceMode = VoiceProcessingMode.Enabled,
    bool responderEnabled = true,
    int replyQueueCapacity = 100,
    bool echoTranscriptToDebugChat = false,
    VoiceReplyMode voiceReplyMode = VoiceReplyMode.Disabled,
    ICommsEventFormatter? formatter = null,
    int streamSendBurst = 10,
    int streamSendRatePerMinute = 20,
    string[]? monitorSources = null,
    bool streamEventTurnsEnabled = true) : IAsyncDisposable
{
    /// <summary>The gateway's own account, whose messages the service must ignore.</summary>
    public const string Account = "+10000000000";

    /// <summary>A group member who sends the envelopes under test.</summary>
    public const string Sender = "+10000000001";

    /// <summary>The configured chat group.</summary>
    public const string ChatGroupName = "My Test Group Name";

    /// <summary>The operator-only group that receives diagnostics.</summary>
    public const string MonitorGroupName = "My Test Monitor Group Name";

    /// <summary>A group the service must ignore.</summary>
    public const string OtherGroupName = "Other Test Group Name";

    /// <summary>The agent profile key answering the chat group.</summary>
    public const string AgentKey = "CommsAgent";

    /// <summary>The environment acronym written to every stream entry.</summary>
    public const string EnvironmentAcronym = "DEV";

    private readonly FixtureState _state = FixtureState.Create(
        voiceMode,
        responderEnabled,
        replyQueueCapacity,
        echoTranscriptToDebugChat,
        voiceReplyMode,
        formatter,
        streamSendBurst,
        streamSendRatePerMinute,
        monitorSources,
        streamEventTurnsEnabled);

    /// <summary>The duplicate-suppression fake.</summary>
    public FakeSignalMessageDeduplicator Deduplicator => _state.Deduplicator;

    /// <summary>The poll tracker fake.</summary>
    public FakePollTracker PollTracker => _state.PollTracker;

    /// <summary>The speech-to-text backend behind the transcription service.</summary>
    public FakeSpeechToTextClient SpeechToText => _state.SpeechToText;

    /// <summary>The synthesis backend behind the spoken-reply service.</summary>
    public FakeTextToSpeechClient TextToSpeech => _state.TextToSpeech;

    /// <summary>The gateway fake feeding deliveries in and recording every group operation.</summary>
    public FakeSignalizrClient Signalizr => _state.Signalizr;

    /// <summary>The Redis fake backing the comms stream and cached media.</summary>
    public InMemoryCommsRedis Redis => _state.Redis;

    /// <summary>Bytes returned for every attachment the tests queue.</summary>
    public byte[] AttachmentContent
    {
        get => _state.AttachmentContent;
        set => _state.AttachmentContent = value;
    }

    /// <summary>The service under test.</summary>
    public CommunicationsBgService Service => _state.Service;

    /// <summary>The running <see cref="CommunicationsBgService.ExecuteAsync"/> task.</summary>
    public Task Execution => _state.Execution ?? throw new InvalidOperationException("The fixture has not been started.");

    /// <summary>
    /// Builds a silent 16 kHz mono signed 16-bit PCM WAV, which is what the transcription service
    /// accepts without invoking ffmpeg.
    /// </summary>
    /// <param name="sampleCount">Number of silent samples to emit.</param>
    /// <remarks>
    /// The media policy validates the file signature before transmitting anything, so an arbitrary
    /// byte array would be rejected as invalid rather than reaching the backend.
    /// </remarks>
    public static byte[] SyntheticWav(int sampleCount = 1_600)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataBytes = sampleCount * channels * (bitsPerSample / 8);

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
        writer.Flush();
        return buffer.ToArray();
    }

    /// <summary>Launches execution without waiting for the gateway, so a test can observe startup itself.</summary>
    public void LaunchWithoutWaiting() => _state.Execution = Service.ExecuteAsync(_state.Cancellation.Token);

    /// <summary>
    /// Starts execution and waits until the groups are ready and the subscription is open, so
    /// deliveries queued afterwards travel the normal inbound path.
    /// </summary>
    public async Task StartAsync()
    {
        LaunchWithoutWaiting();
        await WaitForAsync(() => Signalizr.SubscribeCallCount > 0);
    }

    /// <summary>Queues application notifications as durable Signalizr deliveries.</summary>
    public void Enqueue(params IReceivedNotification[] notifications)
    {
        foreach (var notification in notifications)
        {
            var attachments = notification.Attachments?
                .Select(attachment => new SignalizrAttachment
                {
                    Id = attachment.Id ?? throw new InvalidOperationException("Test attachments require an identifier."),
                    ContentType = attachment.ContentType,
                })
                .ToArray() ?? [];

            foreach (var attachment in attachments)
                Signalizr.Attachments[attachment.Id] = AttachmentContent;

            Signalizr.Enqueue(new SignalizrMessage
            {
                DeliveryId = $"delivery-{notification.Timestamp}",
                GroupName = notification.GroupId,
                Sender = notification.Sender,
                Message = notification.Message,
                Timestamp = notification.Timestamp ?? 0,
                Attachments = attachments,
                FromSelf = notification.Sender == Account,
            });
        }
    }

    /// <summary>Adds a <see cref="CommsEvent"/> to the comms stream in the shape the stream sink writes.</summary>
    public void AddStreamEvent(string source, string message, DateTime timestampUtc, string? jsonPayload = null)
    {
        List<StackExchange.Redis.NameValueEntry> fields =
        [
            new(nameof(CommsEvent.Source), source),
            new(nameof(CommsEvent.Message), message),
            new(nameof(CommsEvent.TimestampUtc), timestampUtc.ToString("o")),
            new(nameof(CommsEvent.Environment), EnvironmentAcronym),
        ];
        if (jsonPayload is not null)
            fields.Add(new(nameof(CommsEvent.JsonPayload), jsonPayload));
        Redis.AddStreamEntry([.. fields]);
    }

    /// <summary>Builds an ordinary text envelope from the chat group.</summary>
    public static FakeReceivedNotification TextEnvelope(string message, long timestamp,
        string sender = Sender, string? groupId = ChatGroupName) =>
        new()
        {
            Sender = sender,
            GroupId = groupId,
            Message = message,
            Timestamp = timestamp,
        };

    /// <summary>Builds an attachment-only envelope from the chat group.</summary>
    public static FakeReceivedNotification AttachmentEnvelope(long timestamp, params (string Id, string ContentType)[] attachments) =>
        new()
        {
            Sender = Sender,
            GroupId = ChatGroupName,
            Timestamp = timestamp,
            Attachments = [.. attachments.Select(a => new FakeNotificationAttachment { Id = a.Id, ContentType = a.ContentType })],
        };

    /// <summary>Polls <paramref name="condition"/> until it holds or the timeout elapses.</summary>
    public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                Assert.Fail($"condition not met within {timeoutMs}ms");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Asserts <paramref name="condition"/> stays false for <paramref name="settleMs"/>, proving an
    /// absence rather than merely observing one early.
    /// </summary>
    public static async Task AssertStaysFalseAsync(Func<bool> condition, int settleMs = 500)
    {
        var deadline = Environment.TickCount64 + settleMs;
        while (Environment.TickCount64 < deadline)
        {
            Assert.False(condition());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        //Release any gate first, or a held drain loop would never observe the cancellation.
        Signalizr.ReleaseStartTyping();
        await _state.Cancellation.CancelAsync();
        if (_state.Execution is not null)
        {
            try
            {
                await _state.Execution.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException
                or CasCap.Signalizr.Client.Exceptions.SignalizrGroupNotConfiguredException)
            {
                //Cancellation is the expected shutdown path; a configuration fault is asserted by its own test.
            }
        }
        _state.Cancellation.Dispose();
        _state.AgentRuntimeHttpClient?.Dispose();
    }

    private sealed class FixtureState
    {
        public required CancellationTokenSource Cancellation { get; init; }
        public required FakeSignalMessageDeduplicator Deduplicator { get; init; }
        public required FakePollTracker PollTracker { get; init; }
        public required FakeSpeechToTextClient SpeechToText { get; init; }
        public required FakeTextToSpeechClient TextToSpeech { get; init; }
        public required FakeSignalizrClient Signalizr { get; init; }
        public required InMemoryCommsRedis Redis { get; init; }
        public required CommunicationsBgService Service { get; init; }
        public required byte[] AttachmentContent { get; set; }
        public HttpClient? AgentRuntimeHttpClient { get; init; }
        public Task? Execution { get; set; }

        public static FixtureState Create(
            VoiceProcessingMode voiceMode,
            bool responderEnabled,
            int replyQueueCapacity,
            bool echoTranscriptToDebugChat,
            VoiceReplyMode voiceReplyMode,
            ICommsEventFormatter? formatter,
            int streamSendBurst,
            int streamSendRatePerMinute,
            string[]? monitorSources,
            bool streamEventTurnsEnabled)
        {
            var deduplicator = new FakeSignalMessageDeduplicator();
            var pollTracker = new FakePollTracker();
            var speechToText = new FakeSpeechToTextClient();
            var textToSpeech = new FakeTextToSpeechClient();
            var signalizr = new FakeSignalizrClient { Groups = [ChatGroupName, MonitorGroupName] };
            var redis = new InMemoryCommsRedis();
            var commsConfig = Options.Create(new CommsConfig
            {
                GroupName = ChatGroupName,
                MonitorGroupName = MonitorGroupName,
                MonitorSources = [.. monitorSources ?? []],
                StreamEventTurnsEnabled = streamEventTurnsEnabled,
                //Short so the idle stream loop and resubscription react within a test's timeout.
                PollingIntervalMs = 20,
                HealthCheckProbeDelayMs = 1,
                ReplyQueueCapacity = replyQueueCapacity,
                StreamSendBurst = streamSendBurst,
                StreamSendRatePerMinute = streamSendRatePerMinute,
                EchoTranscriptToDebugChat = echoTranscriptToDebugChat,
            });
            var speechToTextConfig = Options.Create(new SpeechToTextConfig { Mode = voiceMode });

#pragma warning disable MEAI001 // ISpeechToTextClient is experimental; see WhisperAsrSpeechToTextClient.
            var transcriptionSvc = new VoiceMessageTranscriptionService(
                NullLogger<VoiceMessageTranscriptionService>.Instance, speechToTextConfig, speechToText,
                TestMetrics.Voice());
            var voiceReplySvc = new VoiceReplySynthesisService(
                NullLogger<VoiceReplySynthesisService>.Instance, textToSpeech,
                Options.Create(new TextToSpeechConfig { Mode = voiceReplyMode }));
#pragma warning restore MEAI001

            AgentCommsResponder? responder = null;
            HttpClient? agentRuntimeHttpClient = null;
            if (responderEnabled)
            {
                var debugNotifier = new CommsDebugNotifier(NullLogger<CommsDebugNotifier>.Instance, commsConfig,
                    TimeProvider.System, signalizr, []);
                agentRuntimeHttpClient = new HttpClient(new FakeAgentRuntimeHttpMessageHandler())
                {
                    BaseAddress = new Uri("http://agent-runtime.test"),
                };
                responder = new AgentCommsResponder(
                    NullLogger<AgentCommsResponder>.Instance,
                    commsConfig,
                    new CommsAgentProfile(AgentKey, "test-group-session", "respond"),
                    new AgentRuntimeClient(agentRuntimeHttpClient),
                    signalizr,
                    pollTracker,
                    debugNotifier,
                    []);
            }

            var service = new CommunicationsBgService(
                NullLogger<CommunicationsBgService>.Instance,
                commsConfig,
                speechToTextConfig,
                TimeProvider.System,
                new FakeHostEnvironment(),
                signalizr,
                deduplicator,
                transcriptionSvc,
                voiceReplySvc,
                formatter ?? new PlainCommsEventFormatter(),
                new MonitorSourcesGroupRouter(commsConfig),
                redis,
                responder);
            return new FixtureState
            {
                Cancellation = new CancellationTokenSource(),
                Deduplicator = deduplicator,
                PollTracker = pollTracker,
                SpeechToText = speechToText,
                TextToSpeech = textToSpeech,
                Signalizr = signalizr,
                Redis = redis,
                Service = service,
                AttachmentContent = SyntheticWav(),
                AgentRuntimeHttpClient = agentRuntimeHttpClient
            };
        }
    }
}
