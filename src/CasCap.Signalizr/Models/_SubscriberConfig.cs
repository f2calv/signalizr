namespace CasCap.Models;

/// <summary>Bounds what one connected subscriber may accumulate.</summary>
/// <remarks>
/// Binds from <c>CasCap:SubscriberConfig</c>. Both limits exist so one slow subscriber degrades
/// only itself: it is disconnected with an explicit error rather than stalling the dispatcher or
/// having its messages dropped silently.
/// </remarks>
public sealed record SubscriberConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(SubscriberConfig)}";

    /// <summary>How many deliveries may be queued for one subscriber before it is disconnected.</summary>
    [Range(1, 100_000)]
    public int QueueCapacity { get; init; } = 100;

    /// <summary>How many delivered messages may be unacknowledged before delivery pauses.</summary>
    /// <remarks>
    /// This is what makes the bidirectional stream worth its cost. Without it a subscriber could
    /// read from the socket and never process anything, and the server would keep sending.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxOutstanding { get; init; } = 32;

    /// <summary>How long to wait for acknowledgements before disconnecting a subscriber.</summary>
    [Range(1_000, 600_000)]
    public int AckTimeoutMs { get; init; } = 30_000;
}
