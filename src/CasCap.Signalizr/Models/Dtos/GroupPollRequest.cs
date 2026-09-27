namespace CasCap.Models.Dtos;

/// <summary>Creates a poll in a group.</summary>
public sealed record GroupPollRequest
{
    /// <summary>The poll question.</summary>
    [Required, MinLength(1)]
    public required string Question { get; init; }

    /// <summary>The answer options, in display order. Votes refer to them by index.</summary>
    [Required, MinLength(2)]
    public required IReadOnlyList<string> Answers { get; init; }

    /// <summary>Whether a voter may select more than one answer.</summary>
    public bool AllowMultipleSelections { get; init; }
}
