namespace CasCap.Signalizr.Client;

/// <summary>Metadata for a durable inbound binary attachment.</summary>
public sealed record SignalizrAttachment
{
    /// <summary>Opaque identifier used to download the attachment.</summary>
    public required string Id { get; init; }

    /// <summary>MIME content type supplied by Signal.</summary>
    public string? ContentType { get; init; }

    /// <summary>Original filename supplied by the sender.</summary>
    public string? Filename { get; init; }

    /// <summary>Persisted binary size in bytes.</summary>
    public long Size { get; init; }
}
