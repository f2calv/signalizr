namespace CasCap.Models.Dtos;

/// <summary>A request to send a message to a named channel.</summary>
/// <remarks>
/// Deliberately smaller than the upstream send contract. signalizr is a gateway, not a proxy: it
/// owns the account and exposes only what a caller needs, so a caller cannot choose a recipient,
/// a sender number or a sticker.
/// </remarks>
public sealed record SendMessageRequest
{
    /// <summary>The message text.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(4096, MinimumLength = 1)]
    public required string Message { get; init; }

    /// <summary>Optional upstream-compatible binary data-URI attachments.</summary>
    /// <remarks>
    /// Each value uses the signal-cli REST API format, for example
    /// <c>data:image/png;filename=chart.png;base64,...</c> or
    /// <c>data:audio/ogg;filename=reply.ogg;base64,...</c>. Addressing and account fields remain
    /// gateway-owned.
    /// </remarks>
    [MaxLength(10)]
    public IReadOnlyList<string>? Base64Attachments { get; init; }
}
