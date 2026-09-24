namespace CasCap.Models.Dtos;

/// <summary>Descriptor for durable inbound binary content.</summary>
public sealed record InboundAttachment
{
    /// <summary>Opaque identifier used to download or delete the bytes.</summary>
    public required string Id { get; init; }

    /// <summary>MIME content type supplied by Signal.</summary>
    public string? ContentType { get; init; }

    /// <summary>Original filename supplied by the sender.</summary>
    public string? Filename { get; init; }

    /// <summary>Persisted binary size in bytes.</summary>
    public long Size { get; init; }
}