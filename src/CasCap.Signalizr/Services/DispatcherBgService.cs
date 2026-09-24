using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using Microsoft.EntityFrameworkCore;

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
    IDbContextFactory<SignalizrDbContext> dbContextFactory) : IBgFeature
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
            await using (var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                // The Receiver role is deliberately single-owner, so one dispatcher assigns the
                // provider-neutral monotonic sequence without a database-specific identity type.
                var nextMessageId = await dbContext.InboundMessages
                    .Select(message => (long?)message.Id)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) + 1 ?? 1;
                dbContext.InboundMessages.Add(new InboundMessageEntity
                {
                    Id = nextMessageId,
                    Channel = delivery.Channel,
                    Sender = delivery.Sender,
                    Message = delivery.Message,
                    Timestamp = delivery.Timestamp,
                    PersistedAtUtc = timeProvider.GetUtcNow()
                });
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

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
