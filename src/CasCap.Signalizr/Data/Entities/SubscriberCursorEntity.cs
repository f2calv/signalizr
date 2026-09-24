namespace CasCap.Data.Entities;

/// <summary>Durable acknowledgement cursor for one stable subscriber identity.</summary>
public sealed class SubscriberCursorEntity
{
    /// <summary>Stable subscriber identity supplied by the client configuration.</summary>
    public required string SubscriberName { get; set; }

    /// <summary>Highest contiguously acknowledged message sequence.</summary>
    public long LastAcknowledgedMessageId { get; set; }

    /// <summary>UTC time at which the cursor was last advanced.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}