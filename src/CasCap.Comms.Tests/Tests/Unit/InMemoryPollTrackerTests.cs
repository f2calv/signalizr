namespace CasCap.Tests.Unit;

/// <summary>Tests the Comms-owned in-memory poll lifecycle.</summary>
[Trait("Category", "Messaging")]
public sealed class InMemoryPollTrackerTests
{
    [Fact]
    public void GetPoll_ExpiredPollIsRemoved()
    {
        var timeProvider = new AdjustableTimeProvider(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        var tracker = new InMemoryPollTracker(
            Options.Create(new CommsConfig { PollTtlMs = 1_000 }),
            timeProvider);
        tracker.TrackPoll("poll", "Question?", ["A", "B"], "group");

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(tracker.GetPoll("poll"));
        Assert.Empty(tracker.GetActivePolls());
    }

    [Fact]
    public void RecordVote_UpdatesResultSummary()
    {
        var tracker = new InMemoryPollTracker(
            Options.Create(new CommsConfig()),
            new AdjustableTimeProvider(DateTimeOffset.UtcNow));
        tracker.TrackPoll("poll", "Question?", ["A", "B"], "group");

        var recorded = tracker.RecordVote("poll", "voter", [1]);

        Assert.True(recorded);
        Assert.Contains("[1] B: 1 vote(s)", tracker.GetPoll("poll")?.BuildResultSummary());
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
