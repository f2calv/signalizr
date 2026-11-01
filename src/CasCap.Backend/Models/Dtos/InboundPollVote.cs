using System.Globalization;

namespace CasCap.Models.Dtos;

/// <summary>A vote cast on a poll, carried by an inbound delivery.</summary>
/// <remarks>A voter's later vote on the same poll replaces their earlier selection.</remarks>
public sealed record InboundPollVote
{
    /// <summary>The poll identifier: the Signal timestamp of the poll message.</summary>
    public required long PollTimestamp { get; init; }

    /// <summary>The selected answer indexes.</summary>
    public IReadOnlyList<int> OptionIndexes { get; init; } = [];

    /// <summary>Formats answer indexes for durable storage as comma-separated integers.</summary>
    public static string FormatOptionIndexes(IReadOnlyList<int> optionIndexes) =>
        string.Join(',', optionIndexes.Select(index => index.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Parses answer indexes stored by <see cref="FormatOptionIndexes"/>.</summary>
    public static IReadOnlyList<int> ParseOptionIndexes(string? stored) =>
        string.IsNullOrEmpty(stored)
            ? []
            : [.. stored.Split(',').Select(index => int.Parse(index, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))];
}
