using CasCap.Diagnostics;

namespace CasCap.Services;

/// <summary>Drains the inbound queue and fans each message out to connected subscribers.</summary>
/// <remarks>
/// Separate from the receive loop on purpose. This is where per-message work belongs — channel
/// resolution, mapping and fan-out — because time spent here does not stop the upstream being read.
/// </remarks>
public sealed class DispatcherBgService(
    ILogger<DispatcherBgService> logger,
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
            var delivery = CreateDelivery(message, channelResolver);

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
    /// <returns><see langword="null"/> when the envelope carries no content, such as a receipt or
    /// a typing indicator.</returns>
    public static InboundDelivery? CreateDelivery(
        SignalReceivedMessage message, IChannelResolver channelResolver)
    {
        var notification = (IReceivedNotification)message;
        if (!notification.HasContent)
            return null;

        string? channel = null;
        if (notification.GroupId is not null
            && channelResolver.TryGetChannelName(notification.GroupId, out var resolved))
            channel = resolved;

        return new InboundDelivery
        {
            DeliveryId = string.Empty,
            Channel = channel,
            Sender = notification.Sender,
            Message = notification.Message,
            Timestamp = notification.Timestamp ?? message.Envelope.Timestamp
        };
    }
}
