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
    IChannelResolver channelResolver) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Receiver;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} dispatching inbound messages", nameof(DispatcherBgService));

        await foreach (var message in queue.DequeueAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Receipts, typing indicators and syncs of the account's own sent messages carry no
            // data message. Delivering them as empty messages would make every consumer filter
            // them out, so they stop here.
            if (message.Envelope.DataMessage is null)
                continue;

            var delivery = CreateDelivery(message, channelResolver);

            foreach (var failed in subscribers.Broadcast(delivery))
            {
                // Disconnect rather than drop: a subscriber that cannot keep up must find out, and
                // the gRPC stream ends with an error the client can act on.
                logger.LogWarning("{ClassName} subscriber {Subscriber} is too far behind, disconnecting",
                    nameof(DispatcherBgService), failed.Name);
                subscribers.Unsubscribe(failed);
            }
        }

        logger.LogInformation("{ClassName} stopped", nameof(DispatcherBgService));
    }

    /// <summary>Maps an upstream message onto a delivery, resolving the group to a channel name.</summary>
    /// <remarks>
    /// Separated from the loop so the mapping is testable without a queue or a Signal account. The
    /// identifier is replaced per subscriber during fan-out.
    /// </remarks>
    public static InboundDelivery CreateDelivery(SignalReceivedMessage message, IChannelResolver channelResolver)
    {
        var groupId = message.Envelope.DataMessage?.GroupInfo?.GroupId;

        string? channel = null;
        if (groupId is not null && channelResolver.TryGetChannelName(groupId, out var resolved))
            channel = resolved;

        return new InboundDelivery
        {
            DeliveryId = string.Empty,
            Channel = channel,
            Sender = message.Envelope.Source ?? message.Envelope.SourceNumber,
            Message = message.Envelope.DataMessage?.Message,
            Timestamp = message.Envelope.DataMessage?.Timestamp ?? message.Envelope.Timestamp
        };
    }
}
