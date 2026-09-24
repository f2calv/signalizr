using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers the buffer between the receive loop and the dispatcher. Its whole purpose is to never
/// block the writer, so the tests assert that it drops rather than waits, and that it says so.
/// </summary>
public class InboundMessageQueueTests
{
    private static InboundMessageQueue CreateQueue(
        int capacity, ILogger<InboundMessageQueue>? logger = null)
        => new(logger ?? NullLogger<InboundMessageQueue>.Instance,
            Options.Create(new ReceiverConfig { QueueCapacity = capacity }));

    private static SignalReceivedMessage CreateMessage(long timestamp)
        => new() { Envelope = new SignalEnvelope { Timestamp = timestamp } };

    [Fact]
    public async Task Enqueued_messages_are_read_back_in_order()
    {
        var queue = CreateQueue(10);

        for (var i = 1; i <= 3; i++)
            Assert.True(queue.TryEnqueue(CreateMessage(i)));

        queue.Complete();

        var received = new List<long?>();
        await foreach (var message in queue.DequeueAllAsync(TestContext.Current.CancellationToken))
            received.Add(message.Envelope.Timestamp);

        Assert.Equal([1L, 2L, 3L], received);
        Assert.Equal(3, queue.EnqueuedCount);
        Assert.Equal(0, queue.DroppedCount);
    }

    [Fact]
    public void A_full_queue_drops_instead_of_blocking()
    {
        var queue = CreateQueue(2);

        // Four writes into a queue of two. TryEnqueue must still return immediately: blocking here
        // would stall the receive loop, which loses messages upstream where nothing counts them.
        for (var i = 1; i <= 4; i++)
            Assert.True(queue.TryEnqueue(CreateMessage(i)));

        Assert.Equal(4, queue.EnqueuedCount);
        Assert.Equal(2, queue.DroppedCount);
    }

    [Fact]
    public async Task A_full_queue_keeps_the_newest_messages()
    {
        var queue = CreateQueue(2);

        for (var i = 1; i <= 4; i++)
            queue.TryEnqueue(CreateMessage(i));

        queue.Complete();

        var received = new List<long?>();
        await foreach (var message in queue.DequeueAllAsync(TestContext.Current.CancellationToken))
            received.Add(message.Envelope.Timestamp);

        // DropOldest: a stale message is worth less than a current one, and the drop is counted.
        Assert.Equal([3L, 4L], received);
    }

    [Fact]
    public void A_completed_queue_refuses_further_writes()
    {
        var queue = CreateQueue(10);
        queue.Complete();

        Assert.False(queue.TryEnqueue(CreateMessage(1)));
        Assert.Equal(0, queue.EnqueuedCount);
    }

    [Fact]
    public async Task A_reader_waits_for_a_message_rather_than_spinning()
    {
        var queue = CreateQueue(10);
        var enumerator = queue.DequeueAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            // An empty queue must leave the reader parked, not returning false or spinning.
            var pending = enumerator.MoveNextAsync();
            Assert.False(pending.IsCompleted);

            queue.TryEnqueue(CreateMessage(42));

            Assert.True(await pending);
            Assert.Equal(42L, enumerator.Current.Envelope.Timestamp);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Ten_thousand_message_burst_reports_exact_queue_loss()
    {
        const int Capacity = 1_000;
        const int Sent = 10_000;
        var logger = new RecordingLogger<InboundMessageQueue>();
        var queue = CreateQueue(Capacity, logger);

        for (var index = 1; index <= Sent; index++)
            Assert.True(queue.TryEnqueue(CreateMessage(index)));

        queue.Complete();
        var retained = new List<long?>();
        await foreach (var message in queue.DequeueAllAsync(TestContext.Current.CancellationToken))
            retained.Add(message.Envelope.Timestamp);

        Assert.Equal(Sent, queue.EnqueuedCount);
        Assert.Equal(Sent - Capacity, queue.DroppedCount);
        Assert.Equal(Capacity, retained.Count);
        Assert.Equal(Sent - Capacity + 1, retained[0]);
        Assert.Equal(Sent, retained[^1]);
        Assert.Single(logger.Messages, message => message.Level is LogLevel.Warning);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add((logLevel, formatter(state, exception)));
    }
}
