namespace CasCap.Models;

/// <summary>Bounds the inbound queue that the receive loop drains into.</summary>
/// <remarks>
/// Binds from <c>CasCap:ReceiverConfig</c>, which arrives identically from <c>appsettings.json</c>,
/// a mounted file, a projected ConfigMap key or <c>CasCap__ReceiverConfig__QueueCapacity</c>.
/// </remarks>
public sealed record ReceiverConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(ReceiverConfig)}";

    /// <summary>
    /// How many inbound messages may be buffered before the oldest is dropped.
    /// </summary>
    /// <remarks>
    /// The queue is bounded rather than unbounded because an unbounded one converts a slow consumer
    /// into unbounded memory growth, and the pod is killed instead of degrading. It drops rather
    /// than blocks because blocking the writer would stall the receive loop, which is the one
    /// failure this whole design exists to avoid.
    /// </remarks>
    [Range(1, 1_000_000)]
    public int QueueCapacity { get; init; } = 1_000;
}
