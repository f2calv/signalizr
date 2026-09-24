namespace CasCap.Signalizr.Client;

/// <summary>An inbound Signal message delivered to a subscriber.</summary>
/// <remarks>
/// A plain record rather than the generated protobuf type, so a consumer can use this package
/// without taking a dependency on the wire format.
/// </remarks>
public sealed record SignalizrMessage
{
    /// <summary>Identifies this delivery to this subscriber, for acknowledgement.</summary>
    public required string DeliveryId { get; init; }

    /// <summary>The configured channel name, or <see langword="null"/> when the message did not
    /// arrive on a known channel.</summary>
    public string? Channel { get; init; }

    /// <summary>The sender's Signal identifier. Personal data: never log it.</summary>
    public string? Sender { get; init; }

    /// <summary>The message text.</summary>
    public string? Message { get; init; }

    /// <summary>Milliseconds since the Unix epoch, as supplied by the Signal server.</summary>
    public long Timestamp { get; init; }

    /// <summary>Durable binary attachments associated with this message.</summary>
    public IReadOnlyList<SignalizrAttachment> Attachments { get; init; } = [];
}
