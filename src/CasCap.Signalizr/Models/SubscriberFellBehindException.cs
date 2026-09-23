namespace CasCap.Models;

/// <summary>Thrown when a subscriber cannot accept deliveries as quickly as they arrive.</summary>
public sealed class SubscriberFellBehindException(string subscriberName)
    : Exception($"Subscriber '{subscriberName}' fell behind and was disconnected.")
{
    /// <summary>The diagnostic subscriber name supplied during connection.</summary>
    public string SubscriberName { get; } = subscriberName;
}