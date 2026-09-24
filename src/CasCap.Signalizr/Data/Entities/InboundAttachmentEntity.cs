namespace CasCap.Data.Entities;

/// <summary>Durable binary attachment associated with an inbound message.</summary>
public sealed class InboundAttachmentEntity
{
    /// <summary>Opaque attachment identifier exposed to subscribers.</summary>
    public required string Id { get; set; }

    /// <summary>Owning inbound message sequence.</summary>
    public long InboundMessageId { get; set; }

    /// <summary>MIME content type supplied by Signal.</summary>
    public string? ContentType { get; set; }

    /// <summary>Original filename supplied by the sender.</summary>
    public string? Filename { get; set; }

    /// <summary>Persisted binary content.</summary>
    public required byte[] Content { get; set; }

    /// <summary>Owning inbound message.</summary>
    public InboundMessageEntity? InboundMessage { get; set; }
}