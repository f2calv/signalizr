namespace CasCap.Models.Dtos;

/// <summary>One inbound message queued for one subscriber.</summary>
/// <remarks>
/// The identifier is per delivery, not per message: two subscribers receiving the same Signal
/// message each acknowledge their own copy, so one slow subscriber cannot acknowledge another's.
/// </remarks>
public sealed record InboundDelivery
{
    /// <summary>Unique per delivery attempt to one subscriber.</summary>
    public required string DeliveryId { get; init; }

    /// <summary>The configured channel name, or <see langword="null"/> when the message did not
    /// arrive on a known channel.</summary>
    public string? Channel { get; init; }

    /// <summary>The sender's Signal identifier. Personal data: never log it.</summary>
    public string? Sender { get; init; }

    /// <summary>The message text.</summary>
    public string? Message { get; init; }

    /// <summary>Milliseconds since the Unix epoch, as supplied by the Signal server.</summary>
    public long? Timestamp { get; init; }
}
