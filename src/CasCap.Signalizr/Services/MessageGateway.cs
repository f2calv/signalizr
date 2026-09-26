using CasCap.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;

namespace CasCap.Services;

/// <inheritdoc cref="IMessageGateway"/>
public sealed class MessageGateway(
    ILogger<MessageGateway> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<GatewayConfig> gatewayConfig,
    TimeProvider timeProvider,
    ISignalCliClient client,
    IChannelResolver channelResolver,
    IOperatorNotifier operatorNotifier,
    TypingLeaseService typingLeases,
    // Registered only with the Receiver role, which owns the persisted deliveries.
    IDbContextFactory<SignalizrDbContext>? dbContextFactory = null) : IMessageGateway
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
    public async Task SetDeliveryReactionAsync(
        string channelName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var (author, timestamp) = await ResolveDeliveryAsync(channelName, deliveryId, cancellationToken).ConfigureAwait(false);
        EnsureAccepted(await client.SendReaction(signalCliConfig.Value.PhoneNumber, groupId, reaction,
            author, timestamp, cancellationToken).ConfigureAwait(false), "reaction");
    }

    /// <inheritdoc/>
    public async Task RemoveDeliveryReactionAsync(
        string channelName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var (author, timestamp) = await ResolveDeliveryAsync(channelName, deliveryId, cancellationToken).ConfigureAwait(false);
        EnsureAccepted(await client.RemoveReaction(signalCliConfig.Value.PhoneNumber, groupId, reaction,
            author, timestamp, cancellationToken).ConfigureAwait(false), "reaction removal");
    }

    /// <inheritdoc/>
    public async Task StartTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await typingLeases.StartAsync(channelName, groupId, cancellationToken).ConfigureAwait(false),
            "typing indicator");
    }

    /// <inheritdoc/>
    public async Task StopTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await typingLeases.StopAsync(channelName, groupId, cancellationToken).ConfigureAwait(false),
            "typing indicator removal");
    }

    /// <summary>Maps a stored delivery onto the author and timestamp Signal addresses a reaction by.</summary>
    /// <remarks>
    /// The gateway's own messages are authored by its account; consumers never need to know it.
    /// </remarks>
    public static (string Author, long Timestamp) GetReactionTarget(
        string? sender, bool fromSelf, long? timestamp, string account)
        => (fromSelf || string.IsNullOrEmpty(sender) ? account : sender,
            timestamp ?? throw new InvalidOperationException("The delivery carries no Signal timestamp."));

    private async Task<(string Author, long Timestamp)> ResolveDeliveryAsync(
        string channelName, string deliveryId, CancellationToken cancellationToken)
    {
        if (dbContextFactory is null)
            throw new NotSupportedException("Delivery lookups need the Receiver role in the same process.");

        if (!long.TryParse(deliveryId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new UnknownDeliveryException(channelName, deliveryId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var message = await dbContext.InboundMessages
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.Channel, candidate.Sender, candidate.FromSelf, candidate.Timestamp })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A delivery from another channel is reported as missing rather than reacted to, so a caller
        // cannot reach a group through a channel name it was not given.
        if (message is null || !string.Equals(message.Channel, channelName, StringComparison.OrdinalIgnoreCase))
            throw new UnknownDeliveryException(channelName, deliveryId);

        return GetReactionTarget(message.Sender, message.FromSelf, message.Timestamp, signalCliConfig.Value.PhoneNumber);
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
