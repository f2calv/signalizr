namespace CasCap.Models;

/// <summary>Controls durable subscriber replay and acknowledgement flow.</summary>
/// <remarks>
/// Binds from <c>CasCap:SubscriberConfig</c>. A stable subscriber name owns one durable cursor;
/// unacknowledged messages are replayed after reconnect or process restart.
/// </remarks>
public sealed record SubscriberConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(SubscriberConfig)}";

    /// <summary>Maximum number of persisted messages loaded in one replay query.</summary>
    [Range(1, 10_000)]
    public int ReplayBatchSize { get; init; } = 100;

    /// <summary>How many delivered messages may be unacknowledged before delivery pauses.</summary>
    /// <remarks>
    /// This pauses one subscriber without blocking persistence or delivery to other subscribers.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxOutstanding { get; init; } = 32;

    /// <summary>Maximum age of an unacknowledged delivery reservation before disconnecting a subscriber.</summary>
    /// <remarks>
    /// Starts before the response write, including blocked writes. Each reservation keeps its own
    /// deadline; later deliveries or invalid acknowledgements do not extend it. Quiet streams with
    /// no outstanding deliveries do not expire.
    /// </remarks>
    [Range(1_000, 600_000)]
    public int AckTimeoutMs { get; init; } = 30_000;

    /// <summary>Gets the exact Signal group names each subscriber receives, keyed by subscriber name.</summary>
    /// <remarks>
    /// A subscriber without an entry is refused. Every name must also appear in
    /// <see cref="GroupConfig.GroupNames"/>; messages from other groups and direct messages are never
    /// delivered to it.
    /// </remarks>
    public Dictionary<string, string[]> SubscriberGroups { get; init; } = new(StringComparer.Ordinal);
}
