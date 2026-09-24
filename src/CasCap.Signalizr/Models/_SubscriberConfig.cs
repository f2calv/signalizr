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

    /// <summary>How long to wait for acknowledgements before disconnecting a subscriber.</summary>
    [Range(1_000, 600_000)]
    public int AckTimeoutMs { get; init; } = 30_000;
}
