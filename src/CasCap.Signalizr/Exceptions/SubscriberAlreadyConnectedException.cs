namespace CasCap.Exceptions;

/// <summary>Thrown when two streams use the same durable subscriber identity concurrently.</summary>
public sealed class SubscriberAlreadyConnectedException(string subscriberName)
    : Exception($"Subscriber '{subscriberName}' is already connected.")
{
    /// <summary>The duplicate stable subscriber identity.</summary>
    public string SubscriberName { get; } = subscriberName;
}