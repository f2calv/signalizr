namespace CasCap.Models.Dtos;

/// <summary>The result of a successful send.</summary>
public sealed record SendMessageResponse
{
    /// <summary>The group the message was sent to.</summary>
    public required string GroupName { get; init; }

    /// <summary>
    /// The Signal server's message timestamp, which identifies the message for a later reaction,
    /// receipt or edit.
    /// </summary>
    public required string Timestamp { get; init; }
}
