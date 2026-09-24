namespace CasCap.Data.Entities;

/// <summary>Persisted inbound message available for durable subscriber delivery.</summary>
public sealed class InboundMessageEntity
{
    /// <summary>Monotonic message sequence used as the delivery identifier.</summary>
    public long Id { get; set; }

    /// <summary>The configured channel name, or <see langword="null"/> when unresolved.</summary>
    public string? Channel { get; set; }

    /// <summary>The sender identifier. Personal data: never log or expose as a telemetry attribute.</summary>
    public string? Sender { get; set; }

    /// <summary>The message body. Personal data: never log or expose as a telemetry attribute.</summary>
    public string? Message { get; set; }

    /// <summary>Milliseconds since the Unix epoch, as supplied by Signal.</summary>
    public long? Timestamp { get; set; }

    /// <summary>Unix milliseconds at which Signalizr persisted the message.</summary>
    public long PersistedAtUnixMilliseconds { get; set; }
}