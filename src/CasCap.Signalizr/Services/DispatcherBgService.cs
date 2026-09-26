using CasCap.Diagnostics;

namespace CasCap.Services;

/// <summary>Drains the inbound queue and fans each message out to connected subscribers.</summary>
/// <remarks>
/// Separate from the receive loop on purpose. This is where per-message work belongs — channel
/// resolution, mapping and fan-out — because time spent here does not stop the upstream being read.
/// </remarks>
public sealed class DispatcherBgService(
    ILogger<DispatcherBgService> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IInboundMessageQueue queue,
    IInboundSubscriberRegistry subscribers,
    IChannelResolver channelResolver,
    TimeProvider timeProvider,
    SignalizrMetrics metrics,
    InboundMessagePersistenceService persistenceSvc) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Receiver;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} dispatching inbound messages", nameof(DispatcherBgService));

        await foreach (var message in queue.DequeueAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var delivery = CreateDelivery(message, channelResolver, signalCliConfig.Value.PhoneNumber);

            // Receipts and typing indicators carry no content at all. Skipping them here saves
            // every consumer from filtering out empty messages.
            if (delivery is null)
                continue;

            using var activity = metrics.ActivitySource.StartActivity("signalizr.persist_inbound");
            var startedAt = timeProvider.GetTimestamp();
            await persistenceSvc.PersistAsync(message, delivery, cancellationToken).ConfigureAwait(false);

            metrics.RecordPersisted(timeProvider.GetElapsedTime(startedAt));
            subscribers.NotifyMessageAvailable();
        }

        logger.LogInformation("{ClassName} stopped", nameof(DispatcherBgService));
    }

    /// <summary>Maps an upstream message onto a delivery, resolving the group to a channel name.</summary>
    /// <remarks>
    /// Separated from the loop so the mapping is testable without a queue or a Signal account. The
    /// identifier is replaced per subscriber during fan-out.
    /// </remarks>
    /// <param name="message">The upstream envelope.</param>
    /// <param name="channelResolver">Resolves the envelope's group to a channel name.</param>
    /// <param name="accountNumber">The gateway's own account, used to mark its own messages.</param>
    /// <returns><see langword="null"/> when the envelope carries no content, such as a receipt or
    /// a typing indicator.</returns>
    public static InboundDelivery? CreateDelivery(
        SignalReceivedMessage message, IChannelResolver channelResolver, string accountNumber)
    {
        var notification = (IReceivedNotification)message;
        if (!notification.HasContent)
            return null;

        string? channel = null;
        if (notification.GroupId is not null
            && channelResolver.TryGetChannelName(notification.GroupId, out var resolved))
            channel = resolved;

        var envelope = message.Envelope;
        var content = envelope.DataMessage ?? envelope.SyncMessage?.SentMessage;

        // A sync sentMessage is by definition the account's own, whichever device sent it.
        var fromSelf = envelope.SyncMessage?.SentMessage is not null
            || string.Equals(envelope.SourceNumber, accountNumber, StringComparison.Ordinal)
            || string.Equals(envelope.Source, accountNumber, StringComparison.Ordinal);

        return new InboundDelivery
        {
            DeliveryId = string.Empty,
            Channel = channel,
            Sender = notification.Sender,
            Message = notification.Message,
            Timestamp = notification.Timestamp ?? envelope.Timestamp,
            FromSelf = fromSelf,
            PollVote = content?.PollVote is { TargetSentTimestamp: { } pollTimestamp } vote
                ? new InboundPollVote { PollTimestamp = pollTimestamp, OptionIndexes = vote.OptionIndexes ?? [] }
                : null
        };
    }
}
