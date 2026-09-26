using System.Threading.Channels;
using CasCap.Diagnostics;
using System.Runtime.CompilerServices;

namespace CasCap.Services;

/// <inheritdoc cref="IInboundMessageQueue"/>
public sealed class InboundMessageQueue : IInboundMessageQueue
{
    private readonly ILogger<InboundMessageQueue> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SignalizrMetrics _metrics;
    private readonly IOperatorNotifier _operatorNotifier;
    private readonly TimeSpan _throttleNoticeInterval;
    private readonly Channel<SignalReceivedMessage> _channel;
    private long _dropped;
    private long _enqueued;
    private long _lastThrottleNotice;
    private long _droppedAtLastNotice;

    public InboundMessageQueue(
        ILogger<InboundMessageQueue> logger,
        IOptions<ReceiverConfig> config,
        IOptions<OperatorNotificationConfig> notificationConfig,
        TimeProvider timeProvider,
        SignalizrMetrics metrics,
        IOperatorNotifier operatorNotifier)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _operatorNotifier = operatorNotifier;
        _throttleNoticeInterval = TimeSpan.FromMilliseconds(notificationConfig.Value.ThrottleNoticeIntervalMs);

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

        NotifyThrottling(dropped);
    }

    /// <summary>Reports drops to the operator at most once per interval, counting every drop since the last notice.</summary>
    private void NotifyThrottling(long dropped)
    {
        var now = _timeProvider.GetTimestamp();
        var last = Interlocked.Read(ref _lastThrottleNotice);
        if (last != 0 && _timeProvider.GetElapsedTime(last, now) < _throttleNoticeInterval)
            return;

        // One drop callback wins the notice; the others fall through to the next interval.
        if (Interlocked.CompareExchange(ref _lastThrottleNotice, now, last) != last)
            return;

        var since = dropped - Interlocked.Exchange(ref _droppedAtLastNotice, dropped);
        _operatorNotifier.Notify($"throttling in effect: the inbound queue is full and dropped {since} message(s); " +
            $"{dropped} dropped since startup");
    }
}
