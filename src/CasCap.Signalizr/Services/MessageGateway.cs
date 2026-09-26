using System.Collections.Concurrent;

namespace CasCap.Services;

/// <inheritdoc cref="IMessageGateway"/>
public sealed class MessageGateway(
    ILogger<MessageGateway> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<GatewayConfig> gatewayConfig,
    TimeProvider timeProvider,
    ISignalCliClient client,
    IChannelResolver channelResolver,
    IOperatorNotifier operatorNotifier) : IMessageGateway
{
    private static readonly TimeSpan SendRateInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, SendRateWindow> _sendRates = new(StringComparer.OrdinalIgnoreCase);
    /// <inheritdoc/>
    public async Task<SendMessageResponse> SendAsync(
        string channelName, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);

        var message = CreateRequest(
            signalCliConfig.Value.PhoneNumber,
            groupId,
            request.Message,
            request.Base64Attachments);

        // A null response means the wrapper accepted nothing we can identify the message by, which
        // is indistinguishable from a failed send, so it must not be reported as success.
        var response = await client.SendMessage(message, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no send result.");

        // Channel name only: the group id is an account-linked identifier and the message body is
        // the caller's content, neither of which belongs in a log.
        logger.LogInformation("{ClassName} sent a message to channel {Channel}",
            nameof(MessageGateway), channelName);
        TrackSendRate(channelName);

        return new SendMessageResponse { Channel = channelName, Timestamp = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task SetReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.SendReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction");
    }

    /// <inheritdoc/>
    public async Task RemoveReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.RemoveReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction removal");
    }

    /// <inheritdoc/>
    public async Task StartTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.ShowTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId,
            cancellationToken).ConfigureAwait(false), "typing indicator");
    }

    /// <inheritdoc/>
    public async Task StopTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.HideTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId,
            cancellationToken).ConfigureAwait(false), "typing indicator removal");
    }

    /// <inheritdoc/>
    public async Task<ChannelPollResponse> CreatePollAsync(
        string channelName, ChannelPollRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var response = await client.CreatePoll(signalCliConfig.Value.PhoneNumber, new CreatePollRequest
        {
            Question = request.Question,
            Answers = [.. request.Answers],
            Recipient = groupId,
            AllowMultipleSelections = request.AllowMultipleSelections
        }, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no poll result.");

        logger.LogInformation("{ClassName} created a poll in channel {Channel}",
            nameof(MessageGateway), channelName);

        return new ChannelPollResponse { Channel = channelName, PollId = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task ClosePollAsync(string channelName, string pollId, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.ClosePoll(signalCliConfig.Value.PhoneNumber, new ClosePollRequest
        {
            PollTimestamp = pollId,
            Recipient = groupId
        }, cancellationToken).ConfigureAwait(false), "poll closure");
    }

    /// <summary>Warns once per channel per minute when sends exceed the configured rate.</summary>
    /// <remarks>
    /// A fixed one-minute window: cheap, and precise enough to say "this channel is flooding".
    /// </remarks>
    // TODO: enforce a per-channel and per-account send budget (queue or reject with 429) once the
    // warnings show real production rates. Detection comes first so a limit is not guessed; until
    // then producers such as CAS keep their own throttles.
    private void TrackSendRate(string channelName)
    {
        var threshold = gatewayConfig.Value.SendRateWarningPerMinute;
        if (threshold <= 0)
            return;

        var window = _sendRates.GetOrAdd(channelName, _ => new SendRateWindow());
        int count;
        lock (window.Gate)
        {
            var now = timeProvider.GetTimestamp();
            if (window.StartedAt == 0 || timeProvider.GetElapsedTime(window.StartedAt, now) >= SendRateInterval)
            {
                window.StartedAt = now;
                window.Count = 0;
                window.Warned = false;
            }

            count = ++window.Count;
            if (count <= threshold || window.Warned)
                return;
            window.Warned = true;
        }

        logger.LogWarning("{ClassName} channel {Channel} exceeded {Threshold} sends within a minute, a possible flood",
            nameof(MessageGateway), channelName, threshold);
        operatorNotifier.Notify($"flood warning: channel {channelName} sent more than {threshold} messages within a minute; " +
            "Signal may start rate-limiting the account");
    }

    private string ResolveGroupId(string channelName) =>
        channelResolver.TryGetGroupId(channelName, out var groupId)
            ? groupId
            : throw new UnknownChannelException(channelName);

    // The upstream reports rejection as false rather than an error status, and the caller must
    // not mistake a refused operation for a completed one.
    private static void EnsureAccepted(bool accepted, string operation)
    {
        if (!accepted)
            throw new HttpRequestException($"The signal-cli wrapper rejected the {operation}.");
    }

    private sealed class SendRateWindow
    {
        public readonly Lock Gate = new();
        public long StartedAt;
        public int Count;
        public bool Warned;
    }

    /// <summary>Builds the upstream send request for one group.</summary>
    /// <remarks>
    /// Separated from the call so the translation is testable without stubbing the whole client.
    /// </remarks>
    public static SignalMessageRequest CreateRequest(
        string number,
        string groupId,
        string message,
        IReadOnlyList<string>? base64Attachments = null)
        => new()
        {
            Number = number,
            Recipients = [groupId],
            Message = message,
            Base64Attachments = base64Attachments?.ToArray()
        };
}
