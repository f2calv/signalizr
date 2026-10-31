using CasCap.Models.Dtos;
using Xunit;

namespace CasCap.Tests;

/// <summary>Covers the durable text form of a poll vote's selection.</summary>
public class InboundPollVoteTests
{
    [Theory]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { 0, 2, 7 })]
    public void Stored_option_indexes_round_trip(int[] optionIndexes)
    {
        var stored = InboundPollVote.FormatOptionIndexes(optionIndexes);

        Assert.Equal(optionIndexes, InboundPollVote.ParseOptionIndexes(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_selection_parses_as_empty(string? stored)
        => Assert.Empty(InboundPollVote.ParseOptionIndexes(stored));
}
