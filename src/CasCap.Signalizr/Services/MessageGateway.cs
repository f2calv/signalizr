using CasCap.Data;
using CasCap.Diagnostics;
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
    SignalizrMetrics metrics,
    ISignalCliClient client,
    IGroupResolver groupResolver,
    IOperatorNotifier operatorNotifier,
    TypingLeaseService typingLeases,
    // Registered only with the Receiver role, which owns the persisted deliveries.
    IDbContextFactory<SignalizrDbContext>? dbContextFactory = null) : IMessageGateway
{
    private static readonly TimeSpan SendRateInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, SendRateWindow> _sendRates = new(StringComparer.Ordinal);
    /// <inheritdoc/>
    public async Task<SendMessageResponse> SendAsync(
        string groupName, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);

        var message = CreateRequest(
            signalCliConfig.Value.PhoneNumber,
            groupId,
            request.Message,
            request.Base64Attachments);

        // A null response means the wrapper accepted nothing we can identify the message by, which
        // is indistinguishable from a failed send, so it must not be reported as success.
        var response = await client.SendMessage(message, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no send result.");

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} sent a message to a configured Signal group",
                nameof(MessageGateway));
        metrics.RecordSent();
        TrackSendRate(groupName);

        return new SendMessageResponse { GroupName = groupName, Timestamp = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task SetReactionAsync(
        string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.SendReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction");
    }

    /// <inheritdoc/>
    public async Task RemoveReactionAsync(
        string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.RemoveReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction removal");
    }

    /// <inheritdoc/>
    public async Task SetDeliveryReactionAsync(
        string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        var (author, timestamp) = await ResolveDeliveryAsync(groupName, deliveryId, cancellationToken).ConfigureAwait(false);
        EnsureAccepted(await client.SendReaction(signalCliConfig.Value.PhoneNumber, groupId, reaction,
            author, timestamp, cancellationToken).ConfigureAwait(false), "reaction");
    }

    /// <inheritdoc/>
    public async Task RemoveDeliveryReactionAsync(
        string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        var (author, timestamp) = await ResolveDeliveryAsync(groupName, deliveryId, cancellationToken).ConfigureAwait(false);
        EnsureAccepted(await client.RemoveReaction(signalCliConfig.Value.PhoneNumber, groupId, reaction,
            author, timestamp, cancellationToken).ConfigureAwait(false), "reaction removal");
    }

    /// <inheritdoc/>
    public async Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        EnsureAccepted(await typingLeases.StartAsync(groupName, groupId, cancellationToken).ConfigureAwait(false),
            "typing indicator");
    }

    /// <inheritdoc/>
    public async Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        EnsureAccepted(await typingLeases.StopAsync(groupName, groupId, cancellationToken).ConfigureAwait(false),
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
        string groupName, string deliveryId, CancellationToken cancellationToken)
    {
        if (dbContextFactory is null)
            throw new NotSupportedException("Delivery lookups need the Receiver role in the same process.");

        if (!long.TryParse(deliveryId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new UnknownDeliveryException(groupName, deliveryId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var message = await dbContext.InboundMessages
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.GroupName, candidate.Sender, candidate.FromSelf, candidate.Timestamp })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A delivery from another group is reported as missing rather than reacted to, so a caller
        // cannot reach a group through a group name it was not given.
        if (message is null || !string.Equals(message.GroupName, groupName, StringComparison.Ordinal))
            throw new UnknownDeliveryException(groupName, deliveryId);

        return GetReactionTarget(message.Sender, message.FromSelf, message.Timestamp, signalCliConfig.Value.PhoneNumber);
    }

    /// <inheritdoc/>
    public async Task<GroupPollResponse> CreatePollAsync(
        string groupName, GroupPollRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        var response = await client.CreatePoll(signalCliConfig.Value.PhoneNumber, new CreatePollRequest
        {
            Question = request.Question,
            Answers = [.. request.Answers],
            Recipient = groupId,
            AllowMultipleSelections = request.AllowMultipleSelections
        }, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no poll result.");

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} created a poll in a configured Signal group",
                nameof(MessageGateway));

        return new GroupPollResponse { GroupName = groupName, PollId = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(groupName);
        EnsureAccepted(await client.ClosePoll(signalCliConfig.Value.PhoneNumber, new ClosePollRequest
        {
            PollTimestamp = pollId,
            Recipient = groupId
        }, cancellationToken).ConfigureAwait(false), "poll closure");
    }

    /// <summary>Warns once per group per minute when sends exceed the configured rate.</summary>
    /// <remarks>
    /// A fixed one-minute window: cheap, and precise enough to say "this group is flooding".
    /// </remarks>
    // TODO: Choose and enforce a per-group and per-account send budget after the outbound sent and
    // warning counters establish real production rates. Detection comes first so a queue or rejection
    // threshold is not guessed; until then producers such as CAS keep their own throttles.
    private void TrackSendRate(string groupName)
    {
        var threshold = gatewayConfig.Value.SendRateWarningPerMinute;
        if (threshold <= 0)
            return;

        var window = _sendRates.GetOrAdd(groupName, _ => new SendRateWindow());
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

        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("{ClassName} a configured Signal group exceeded {Threshold} sends within a minute, a possible flood",
                nameof(MessageGateway), threshold);
        }
        metrics.RecordSendRateWarning();
        operatorNotifier.Notify($"flood warning: groupName {groupName} sent more than {threshold} messages within a minute; " +
            "Signal may start rate-limiting the account");
    }

    private string ResolveGroupId(string groupName) =>
        groupResolver.TryGetGroupId(groupName, out var groupId)
            ? groupId
            : throw new UnknownGroupException(groupName);

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
