namespace CasCap.Tests.Unit;

/// <summary>Verifies that tracked operations report progress, completion and timeouts to the comms stream.</summary>
[Trait("Category", "Comms")]
public sealed class TrackedOperationServiceTests
{
    [Fact]
    public async Task TrackAsync_ReportsProgressThenCompletion()
    {
        var sink = new RecordingCommsSink();
        var operation = new ScriptedOperation(TimeSpan.FromSeconds(30),
            new TrackedOperationProgress(0, 2, ["Kitchen", "Office"]),
            new TrackedOperationProgress(1, 2, ["Office"]),
            new TrackedOperationProgress(2, 2, []));

        await CreateService(sink).TrackAsync("op", operation, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "Closing 2 shutters: 0 of 2 done, still in progress: Kitchen, Office.",
                "Closing 2 shutters: 1 of 2 done, still in progress: Office.",
                "Closing 2 shutters: done, all 2 reached their target.",
            ],
            sink.Events.Select(e => e.Message));
        Assert.All(sink.Events, e => Assert.Equal(TrackedOperationService.SourceName, e.Source));
    }

    [Fact]
    public async Task TrackAsync_TimeoutListsUnconfirmedItems()
    {
        var sink = new RecordingCommsSink();
        var operation = new ScriptedOperation(TimeSpan.FromMilliseconds(50),
            new TrackedOperationProgress(1, 2, ["Office"]));

        await CreateService(sink).TrackAsync("op", operation, TestContext.Current.CancellationToken);

        var last = sink.Events[^1].Message;
        Assert.StartsWith("Closing 2 shutters: stopped waiting after", last, StringComparison.Ordinal);
        Assert.EndsWith("1 of 2 done; not confirmed: Office.", last, StringComparison.Ordinal);
    }

    private static TrackedOperationService CreateService(RecordingCommsSink sink)
        => new(NullLogger<TrackedOperationService>.Instance, TimeProvider.System, new FakeHostEnvironment(), sink);

    private sealed class ScriptedOperation(TimeSpan timeout, params TrackedOperationProgress[] script) : ITrackedOperation
    {
        private int _index;

        public string Description => "Closing 2 shutters";

        public TimeSpan PollInterval => TimeSpan.FromMilliseconds(5);

        public TimeSpan ProgressInterval => TimeSpan.Zero;

        public TimeSpan Timeout => timeout;

        public ValueTask<TrackedOperationProgress> PollAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(script[Math.Min(_index++, script.Length - 1)]);
    }

    private sealed class RecordingCommsSink : IEventSink<CommsEvent>
    {
        public List<CommsEvent> Events { get; } = [];

        public string SinkType => "Recording";

        public Task WriteEvent(CommsEvent @event, CancellationToken cancellationToken = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
