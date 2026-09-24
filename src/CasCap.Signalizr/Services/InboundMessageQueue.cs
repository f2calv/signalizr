using System.Threading.Channels;
using CasCap.Diagnostics;
using System.Runtime.CompilerServices;

namespace CasCap.Services;

/// <inheritdoc cref="IInboundMessageQueue"/>
public sealed class InboundMessageQueue : IInboundMessageQueue
{
    private readonly ILogger<InboundMessageQueue> _logger;
    private readonly SignalizrMetrics _metrics;
    private readonly Channel<SignalReceivedMessage> _channel;
    private long _dropped;
    private long _enqueued;

    public InboundMessageQueue(
        ILogger<InboundMessageQueue> logger,
        IOptions<ReceiverConfig> config,
        SignalizrMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;

        var options = new BoundedChannelOptions(config.Value.QueueCapacity)
        {
            // DropOldest, not Wait: waiting would block the receive loop, which is precisely how a
            // naive consumer loses messages upstream. Dropping here is at least counted.
            FullMode = BoundedChannelFullMode.DropOldest,
            // One receive loop owns the stream, and one dispatcher fans out from the queue.
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateBounded<SignalReceivedMessage>(options, OnDropped);
    }

    /// <inheritdoc/>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc/>
    public long EnqueuedCount => Interlocked.Read(ref _enqueued);

    /// <inheritdoc/>
    public bool TryEnqueue(SignalReceivedMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
            return false;

        Interlocked.Increment(ref _enqueued);
        _metrics.RecordReceived();
        return true;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SignalReceivedMessage> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _channel.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            _metrics.RecordDequeued();
            yield return message;
        }
    }

    /// <inheritdoc/>
    public void Complete() => _channel.Writer.TryComplete();

    private void OnDropped(SignalReceivedMessage message)
    {
        var dropped = Interlocked.Increment(ref _dropped);
        _metrics.RecordDropped();

        // Only the first drop is logged: by the time messages are being dropped the consumer is
        // already behind, and a line per drop would compete with it for the same thread pool.
        // The running total is the durable signal.
        if (dropped == 1)
        {
            _logger.LogWarning(
                "{ClassName} dropped an inbound message because the queue is full. " +
                "The dispatcher is not keeping up; raise {Setting} or make dispatch faster.",
                nameof(InboundMessageQueue), $"{ReceiverConfig.ConfigurationSectionName}:QueueCapacity");
        }
    }
}
