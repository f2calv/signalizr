namespace CasCap.Models.Dtos;

/// <summary>Sets or removes a reaction on a message in a channel.</summary>
public sealed record ChannelReactionRequest
{
    /// <summary>The reaction emoji.</summary>
    [Required, MinLength(1)]
    public required string Reaction { get; init; }

    /// <summary>The target message's Signal timestamp, as delivered or as returned by a send.</summary>
    [Range(1, long.MaxValue)]
    public required long TargetTimestamp { get; init; }

    /// <summary>
    /// The target message's author, or <see langword="null"/> for a message the gateway sent.
    /// </summary>
    /// <remarks>
    /// Personal data. Consumers pass back the sender they were delivered; they never need to know
    /// the gateway's own account to react to its messages.
    /// </remarks>
    public string? TargetAuthor { get; init; }
}
