namespace CasCap.Models.Dtos;

/// <summary>One persisted inbound message delivered to a subscriber.</summary>
/// <remarks>
/// The identifier is the durable message sequence. Acknowledgements remain subscriber-scoped
/// because each stable subscriber identity owns an independent cursor.
/// </remarks>
public sealed record InboundDelivery
{
    /// <summary>Durable message sequence represented as an invariant string.</summary>
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

    /// <summary>Durable binary attachments associated with this message.</summary>
    public IReadOnlyList<InboundAttachment> Attachments { get; init; } = [];
}
