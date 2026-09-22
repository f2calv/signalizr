using System.Collections.Concurrent;

namespace CasCap.Services;

/// <inheritdoc cref="IInboundSubscriberRegistry"/>
public sealed class InboundSubscriberRegistry(
    ILogger<InboundSubscriberRegistry> logger, IOptions<SubscriberConfig> config) : IInboundSubscriberRegistry
{
    private readonly ConcurrentDictionary<InboundSubscription, byte> _subscriptions = new();

    /// <inheritdoc/>
    public int Count => _subscriptions.Count;

    /// <inheritdoc/>
    public InboundSubscription Subscribe(string subscriberName)
    {
        var options = config.Value;
        var subscription = new InboundSubscription(subscriberName, options.QueueCapacity, options.MaxOutstanding);
        _subscriptions[subscription] = 0;

        logger.LogInformation("{ClassName} subscriber {Subscriber} connected, {Count} total",
            nameof(InboundSubscriberRegistry), subscriberName, _subscriptions.Count);

        return subscription;
    }

    /// <inheritdoc/>
    public void Unsubscribe(InboundSubscription subscription)
    {
        if (!_subscriptions.TryRemove(subscription, out _))
            return;

        subscription.Complete();

        logger.LogInformation("{ClassName} subscriber {Subscriber} disconnected, {Count} remaining",
            nameof(InboundSubscriberRegistry), subscription.Name, _subscriptions.Count);
    }

    /// <inheritdoc/>
    public IReadOnlyList<InboundSubscription> Broadcast(InboundDelivery delivery)
    {
        List<InboundSubscription>? failed = null;

        foreach (var subscription in _subscriptions.Keys)
        {
            // A distinct identifier per subscriber, so acknowledgements cannot be confused.
            var copy = delivery with { DeliveryId = Guid.NewGuid().ToString("N") };

            if (subscription.TryEnqueue(copy))
                continue;

            (failed ??= []).Add(subscription);
        }

        return failed ?? (IReadOnlyList<InboundSubscription>)[];
    }
}
