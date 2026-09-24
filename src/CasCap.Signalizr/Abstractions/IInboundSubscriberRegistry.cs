using CasCap.Services;

namespace CasCap.Abstractions;

/// <summary>Tracks connected durable subscribers and signals newly persisted messages.</summary>
public interface IInboundSubscriberRegistry
{
    /// <summary>How many subscribers are currently connected.</summary>
    int Count { get; }

    /// <summary>Registers a stable subscriber identity and resumes from its durable cursor.</summary>
    Task<InboundSubscription> SubscribeAsync(
        string subscriberName,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a subscriber and completes its queue.</summary>
    /// <param name="subscription">The subscription to remove.</param>
    /// <param name="error">Optional failure reported to the subscriber's stream.</param>
    void Unsubscribe(InboundSubscription subscription, Exception? error = null);

    /// <summary>Signals every connected subscriber that persisted messages may be available.</summary>
    void NotifyMessageAvailable();
}
