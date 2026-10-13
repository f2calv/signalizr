using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;

namespace CasCap.Tests.Unit;

/// <summary>Tests the Comms responder across its Agent Runtime HTTP boundary.</summary>
[Trait("Category", "Agent Runtime")]
public sealed class AgentCommsResponderTests
{
    [Fact]
    public async Task RespondAsync_PreservesLiveProgressAttachmentsAndDiagnostics()
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new RunAgentStreamItem
                {
                    Event = new RunAgentEvent
                    {
                        Type = RunAgentEventTypes.DelegationStarted,
                        AgentName = "specialist",
                        Depth = 1,
                        ModelName = "special-model",
                    },
                },
                new RunAgentStreamItem
                {
                    Response = new RunAgentResponse
                    {
                        SessionId = "session",
                        OutputText = "completed",
                        DefinitionVersion = "v1",
                        ModelName = "primary-model",
                        ElapsedMilliseconds = 1250,
                        Usage = new RunAgentUsage { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
                        Attachments =
                        [
                            new RunAgentAttachment
                            {
                                MimeType = "image/png",
                                FileName = "result.png",
                                Base64Content = "AQID",
                            },
                        ],
                    },
                },
            }),
        }));
        var signalizr = new FakeSignalizrClient();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var prefix = UniquePrefix();
        using var outcomes = new OutcomeRecorder(prefix);
        var responder = CreateResponder(httpClient, signalizr, prefix);

        var reply = await responder.RespondAsync(
            new CommsTurn("run", Sender: "sender", Timestamp: 123),
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.False(reply.Failed);
        Assert.Equal("completed", reply.Text);
        Assert.Contains("15", reply.Footer, StringComparison.Ordinal);
        Assert.Single(reply.Base64Attachments!, attachment =>
            attachment == "data:image/png;filename=result.png;base64,AQID");
        Assert.Contains(signalizr.Reactions, reaction => reaction.Emoji == "\U0001F500");
        Assert.Contains(signalizr.Reactions, reaction => reaction.Emoji == "\u23F3");
        Assert.Equal([nameof(CommsAgentRunOutcome.Succeeded)], outcomes.Outcomes);
    }

    [Fact]
    public async Task RespondAsync_ToolFailures_AreNamedInTheFooterAndRecorded()
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new RunAgentStreamItem
                {
                    Event = new RunAgentEvent
                    {
                        Type = RunAgentEventTypes.DelegationCompleted,
                        AgentName = "specialist",
                        Result = new RunAgentStepResult { ToolCalls = [new RunAgentToolCall { Name = "create_poll", Failed = true }] },
                    },
                },
                new RunAgentStreamItem
                {
                    Response = new RunAgentResponse
                    {
                        SessionId = "session",
                        OutputText = "All shutters are closed.",
                        DefinitionVersion = "v1",
                        ModelName = "model",
                        ToolCalls =
                        [
                            new RunAgentToolCall { Name = "invoke_specialist" },
                            new RunAgentToolCall { Name = "close_all_house_shutters", Failed = true },
                        ],
                    },
                },
            }),
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var prefix = UniquePrefix();
        using var outcomes = new OutcomeRecorder(prefix);

        var reply = await CreateResponder(httpClient, new FakeSignalizrClient(), prefix).RespondAsync(
            new CommsTurn("close all shutters"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.False(reply.Failed);
        Assert.Contains("\u26A0\uFE0F Tool failed: close_all_house_shutters, create_poll", reply.Footer, StringComparison.Ordinal);
        Assert.Equal([nameof(CommsAgentRunOutcome.CompletedWithToolErrors)], outcomes.Outcomes);
    }

    [Theory]
    [InlineData("unreachable", CommsAgentRunOutcome.Unreachable, "could not be reached")]
    [InlineData("503", CommsAgentRunOutcome.HttpError, "returned HTTP 503")]
    [InlineData("timeout", CommsAgentRunOutcome.Timeout, "timed out")]
    [InlineData("no-response", CommsAgentRunOutcome.NoResponse, "ended without a response")]
    [InlineData("malformed", CommsAgentRunOutcome.Error, "unexpected error")]
    public async Task RespondAsync_RuntimeFailure_RepliesWithFailureNoticeAndRecordsOutcome(
        string failure, CommsAgentRunOutcome expectedOutcome, string expectedReason)
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler((_, _) => failure switch
        {
            "unreachable" => throw new HttpRequestException("connection refused"),
            "timeout" => throw new TaskCanceledException("client timeout"),
            "503" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            "no-response" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new RunAgentStreamItem { Event = new RunAgentEvent { Type = RunAgentEventTypes.DelegationStarted } },
                }),
            }),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{") }),
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var prefix = UniquePrefix();
        using var outcomes = new OutcomeRecorder(prefix);

        var reply = await CreateResponder(httpClient, new FakeSignalizrClient(), prefix).RespondAsync(
            new CommsTurn("run", Sender: "sender", Timestamp: 123),
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply);
        Assert.True(reply.Failed);
        Assert.StartsWith("\u26A0\uFE0F Assistant unavailable: ", reply.Text, StringComparison.Ordinal);
        Assert.Contains(expectedReason, reply.Text, StringComparison.Ordinal);
        Assert.Null(reply.Footer);
        Assert.Equal([expectedOutcome.ToString()], outcomes.Outcomes);
    }

    [Fact]
    public async Task RespondAsync_EmptyOutput_SendsNothingAndRecordsOutcome()
    {
        using var httpClient = new HttpClient(new FakeAgentRuntimeHttpMessageHandler()) { BaseAddress = new Uri("http://agent-runtime.test") };
        var prefix = UniquePrefix();
        using var outcomes = new OutcomeRecorder(prefix);

        var reply = await CreateResponder(httpClient, new FakeSignalizrClient(), prefix).RespondAsync(
            new CommsTurn("run"),
            TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.Equal([nameof(CommsAgentRunOutcome.EmptyOutput)], outcomes.Outcomes);
    }

    [Fact]
    public async Task RespondAsync_CallerCancellation_PropagatesWithoutRecordingAnOutcome()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handler = new FakeAgentRuntimeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var prefix = UniquePrefix();
        using var outcomes = new OutcomeRecorder(prefix);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateResponder(httpClient, new FakeSignalizrClient(), prefix).RespondAsync(new CommsTurn("run"), cancellation.Token));

        Assert.Empty(outcomes.Outcomes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RespondAsync_RequestsDiagnosticDetailsOnlyWhenAnEnricherNeedsThem(bool enricherNeedsDetails, bool expected)
    {
        RunAgentRequest? sent = null;
        var handler = new FakeAgentRuntimeHttpMessageHandler(async (request, cancellationToken) =>
        {
            sent = await request.Content!.ReadFromJsonAsync<RunAgentRequest>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new RunAgentStreamItem
                    {
                        Response = new RunAgentResponse
                        {
                            SessionId = "session",
                            OutputText = string.Empty,
                            DefinitionVersion = "test-v1",
                            ModelName = "test-model",
                        },
                    },
                }),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };

        await CreateResponder(httpClient, new FakeSignalizrClient(), UniquePrefix(), [new StubEnricher(enricherNeedsDetails)])
            .RespondAsync(new CommsTurn("run"), TestContext.Current.CancellationToken);

        Assert.Equal(expected, sent?.IncludeDiagnosticDetails);
    }

    [Fact]
    public async Task TryHandleCommandAsync_UpdatesRemoteModelOverride()
    {
        UpdateAgentOverridesRequest? update = null;
        var handler = new FakeAgentRuntimeHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AgentOverridesResponse { SessionEnabled = true }),
                };
            }

            update = await request.Content!.ReadFromJsonAsync<UpdateAgentOverridesRequest>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AgentOverridesResponse
                {
                    SessionEnabled = update!.SessionEnabled,
                    ModelName = update.ModelName,
                    Instructions = update.Instructions,
                }),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var responder = CreateResponder(httpClient, new FakeSignalizrClient());

        var outcome = await responder.TryHandleCommandAsync(
            "/model qwen3:32b",
            TestContext.Current.CancellationToken);

        Assert.Equal("Model overridden to: qwen3:32b", outcome?.ReplyText);
        Assert.Equal("qwen3:32b", update?.ModelName);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task TryHandleCommandAsync_BypassRemainsLocalDeferredTurn()
    {
        var handler = new FakeAgentRuntimeHttpMessageHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://agent-runtime.test") };
        var responder = CreateResponder(httpClient, new FakeSignalizrClient());

        var outcome = await responder.TryHandleCommandAsync(
            "/session bypass inspect this",
            TestContext.Current.CancellationToken);

        Assert.Null(outcome?.ReplyText);
        Assert.Equal("inspect this", outcome?.DeferredTurn?.Prompt);
        Assert.True(outcome?.DeferredTurn?.BypassSession);
        Assert.Empty(handler.Calls);
    }

    private static AgentCommsResponder CreateResponder(
        HttpClient httpClient,
        FakeSignalizrClient signalizr,
        string metricNamePrefix = "comms",
        IAgentRunEnricher[]? enrichers = null)
    {
        var config = Options.Create(new CommsConfig { GroupName = "group" });
        return new AgentCommsResponder(
            NullLogger<AgentCommsResponder>.Instance,
            config,
            new CommsAgentProfile("assistant", "session", "respond"),
            new AgentRuntimeClient(httpClient),
            signalizr,
            new InMemoryPollTracker(config, TimeProvider.System),
            new CommsDebugNotifier(
                NullLogger<CommsDebugNotifier>.Instance,
                config,
                TimeProvider.System,
                signalizr,
                enrichers ?? []),
            TestMetrics.CommsAgent(metricNamePrefix),
            enrichers ?? []);
    }

    private sealed class StubEnricher(bool requiresDiagnosticDetails) : IAgentRunEnricher
    {
        public bool RequiresDiagnosticDetails => requiresDiagnosticDetails;

        public Task<object?> BeforeRunAsync(CancellationToken cancellationToken) => Task.FromResult<object?>(null);

        public Task AfterRunAsync(CommsAgentRunResult result, object? state, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> FormatFooterLineAsync(CommsAgentRunResult result, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public IEnumerable<string> FormatDebugLines(CommsAgentRunResult result) => [];
    }

    private static string UniquePrefix() => $"comms-{Guid.NewGuid():N}";

    /// <summary>Collects run outcomes from one uniquely prefixed instrument, so parallel tests do not interfere.</summary>
    private sealed class OutcomeRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();

        public OutcomeRecorder(string metricNamePrefix)
        {
            var instrumentName = $"{metricNamePrefix}.comms.agent.runs";
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == instrumentName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (var tag in tags)
                    if (tag.Key == CommsAgentMetrics.OutcomeTagName)
                        Outcomes.Enqueue((string)tag.Value!);
            });
            _listener.Start();
        }

        public ConcurrentQueue<string> Outcomes { get; } = new();

        public void Dispose() => _listener.Dispose();
    }
}
