using CasCap.Services;

namespace CasCap.Abstractions;

/// <summary>Tracks connected subscribers and fans one message out to all of them.</summary>
public interface IInboundSubscriberRegistry
{
    /// <summary>How many subscribers are currently connected.</summary>
    int Count { get; }

    /// <summary>Registers a subscriber and returns its queue.</summary>
    InboundSubscription Subscribe(string subscriberName);

    /// <summary>Removes a subscriber and completes its queue.</summary>
    /// <param name="subscription">The subscription to remove.</param>
    /// <param name="error">Optional failure reported to the subscriber's stream.</param>
    void Unsubscribe(InboundSubscription subscription, Exception? error = null);

    /// <summary>
    /// Offers a delivery to every connected subscriber.
    /// </summary>
    /// <remarks>
    /// Each subscriber receives its own <see cref="InboundDelivery.DeliveryId"/>, so one
    /// subscriber's acknowledgement can never be mistaken for another's.
    /// </remarks>
    /// <returns>Subscribers that could not accept the delivery and must be disconnected.</returns>
    IReadOnlyList<InboundSubscription> Broadcast(InboundDelivery delivery);
}
