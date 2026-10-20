namespace CasCap.Exceptions;

/// <summary>Thrown when a subscriber name has no configured groups.</summary>
public sealed class SubscriberNotPermittedException(string subscriberName)
    : Exception($"Subscriber '{subscriberName}' has no configured groups.")
{
    /// <summary>The refused stable subscriber identity.</summary>
    public string SubscriberName { get; } = subscriberName;
}
