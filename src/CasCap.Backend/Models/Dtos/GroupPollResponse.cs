namespace CasCap.Models.Dtos;

/// <summary>The result of creating a poll.</summary>
public sealed record GroupPollResponse
{
    /// <summary>The group the poll was created in.</summary>
    public required string GroupName { get; init; }

    /// <summary>
    /// The poll identifier: the Signal timestamp of the poll message. Votes carry it as their
    /// target timestamp, and closing the poll requires it.
    /// </summary>
    public required string PollId { get; init; }
}
