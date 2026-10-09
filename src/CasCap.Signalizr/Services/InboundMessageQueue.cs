using CasCap.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CasCap.Services;

/// <inheritdoc cref="IInboundMessageQueue"/>
public sealed class InboundMessageQueue(
    ILogger<InboundMessageQueue> logger,
    IOptions<ReceiverConfig> config,
    IOptions<OperatorNotificationConfig> notificationConfig,
    TimeProvider timeProvider,
    SignalizrMetrics metrics,
    IOperatorNotifier operatorNotifier) : IInboundMessageQueue
{
    private readonly QueueState _state = QueueState.Create(
        logger, config, notificationConfig, timeProvider, metrics, operatorNotifier);

    /// <inheritdoc/>
    public long DroppedCount => _state.DroppedCount;

    /// <inheritdoc/>
    public long EnqueuedCount => _state.EnqueuedCount;

    /// <inheritdoc/>
    public bool TryEnqueue(SignalReceivedMessage message) => _state.TryEnqueue(message);

    /// <inheritdoc/>
    public async IAsyncEnumerable<SignalReceivedMessage> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _state.DequeueAllAsync(cancellationToken).ConfigureAwait(false))
            yield return message;
    }

    /// <inheritdoc/>
    public void Complete() => _state.Complete();

    private sealed class QueueState
    {
        public required ILogger<InboundMessageQueue> Logger { get; init; }
        public required TimeProvider TimeProvider { get; init; }
        public required SignalizrMetrics Metrics { get; init; }
        public required IOperatorNotifier OperatorNotifier { get; init; }
        public required TimeSpan ThrottleNoticeInterval { get; init; }
        private Channel<SignalReceivedMessage> Channel { get; set; } = null!;
        private long _dropped;
        private long _enqueued;
        private long _lastThrottleNotice;
        private long _droppedAtLastNotice;

        public long DroppedCount => Interlocked.Read(ref _dropped);

        public long EnqueuedCount => Interlocked.Read(ref _enqueued);

        public static QueueState Create(
            ILogger<InboundMessageQueue> logger,
            IOptions<ReceiverConfig> config,
            IOptions<OperatorNotificationConfig> notificationConfig,
            TimeProvider timeProvider,
            SignalizrMetrics metrics,
            IOperatorNotifier operatorNotifier)
        {
            var state = new QueueState
            {
                Logger = logger,
                TimeProvider = timeProvider,
                Metrics = metrics,
                OperatorNotifier = operatorNotifier,
                ThrottleNoticeInterval = TimeSpan.FromMilliseconds(notificationConfig.Value.ThrottleNoticeIntervalMs)
            };
            var options = new BoundedChannelOptions(config.Value.QueueCapacity)
            {
                // DropOldest, not Wait: waiting would block the receive loop, which is precisely how a
                // naive consumer loses messages upstream. Dropping here is at least counted.
                FullMode = BoundedChannelFullMode.DropOldest,
                // One receive loop owns the stream, and one dispatcher fans out from the queue.
                SingleWriter = true,
                SingleReader = true
            };
            state.Channel = System.Threading.Channels.Channel.CreateBounded<SignalReceivedMessage>(options, state.OnDropped);
            return state;
        }

        public bool TryEnqueue(SignalReceivedMessage message)
        {
            if (!Channel.Writer.TryWrite(message))
                return false;

            Interlocked.Increment(ref _enqueued);
            Metrics.RecordReceived();
            return true;
        }

        public async IAsyncEnumerable<SignalReceivedMessage> DequeueAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var message in Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                Metrics.RecordDequeued();
                yield return message;
            }
        }

        public void Complete() => Channel.Writer.TryComplete();

        private void OnDropped(SignalReceivedMessage message)
        {
            var dropped = Interlocked.Increment(ref _dropped);
            Metrics.RecordDropped();

            // Only the first drop is logged: by the time messages are being dropped the consumer is
            // already behind, and a line per drop would compete with it for the same thread pool.
            // The running total is the durable signal.
            if (dropped == 1)
            {
                Logger.LogWarning(
                    "{ClassName} dropped an inbound message because the queue is full. " +
                    "The dispatcher is not keeping up; raise {Setting} or make dispatch faster.",
                    nameof(InboundMessageQueue), $"{ReceiverConfig.ConfigurationSectionName}:QueueCapacity");
            }

            NotifyThrottling(dropped);
        }

        private void NotifyThrottling(long dropped)
        {
            var now = TimeProvider.GetTimestamp();
            var last = Interlocked.Read(ref _lastThrottleNotice);
            if (last != 0 && TimeProvider.GetElapsedTime(last, now) < ThrottleNoticeInterval)
                return;

            // One drop callback wins the notice; the others fall through to the next interval.
            if (Interlocked.CompareExchange(ref _lastThrottleNotice, now, last) != last)
                return;

            var since = dropped - Interlocked.Exchange(ref _droppedAtLastNotice, dropped);
            OperatorNotifier.Notify($"throttling in effect: the inbound queue is full and dropped {since} message(s); " +
                $"{dropped} dropped since startup");
        }
    }
}
