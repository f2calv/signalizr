namespace CasCap.Signalizr.Client;

/// <summary>A vote cast on a poll.</summary>
/// <remarks>A voter's later vote on the same poll replaces their earlier selection.</remarks>
public sealed record SignalizrPollVote
{
    /// <summary>
    /// The poll identifier, as returned by
    /// <see cref="ISignalizrClient.CreatePollAsync(string, string, IReadOnlyList{string}, bool, CancellationToken)"/>.
    /// </summary>
    public required string PollId { get; init; }

    /// <summary>The selected answer indexes.</summary>
    public IReadOnlyList<int> OptionIndexes { get; init; } = [];
}
