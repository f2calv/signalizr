using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CasCap.Diagnostics;

/// <summary>Low-cardinality metrics and traces for durable Signalizr delivery.</summary>
public sealed class SignalizrMetrics : IDisposable
{
    /// <summary>Meter name registered by the Signalizr host.</summary>
    public const string MeterName = "CasCap.Signalizr";

    /// <summary>Activity source name registered by the Signalizr host.</summary>
    public const string ActivitySourceName = MeterName;

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _acknowledged;
    private readonly Counter<long> _acknowledgementTimeouts;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _dropped;
    private readonly Counter<long> _persisted;
    private readonly Counter<long> _pruned;
    private readonly Histogram<double> _persistenceDuration;
    private readonly UpDownCounter<long> _queueDepth;
    private readonly Counter<long> _received;
    private readonly UpDownCounter<long> _subscribers;
    private long _storedMessages;

    /// <summary>Initializes Signalizr metric instruments.</summary>
    public SignalizrMetrics()
    {
        _acknowledged = _meter.CreateCounter<long>("signalizr.subscriber.acknowledged");
        _acknowledgementTimeouts = _meter.CreateCounter<long>("signalizr.subscriber.acknowledgement_timeouts");
        _delivered = _meter.CreateCounter<long>("signalizr.subscriber.delivered");
        _dropped = _meter.CreateCounter<long>("signalizr.inbound.dropped");
        _persisted = _meter.CreateCounter<long>("signalizr.inbound.persisted");
        _pruned = _meter.CreateCounter<long>("signalizr.inbound.pruned");
        _persistenceDuration = _meter.CreateHistogram<double>(
            "signalizr.inbound.persistence_duration",
            unit: "ms");
        _queueDepth = _meter.CreateUpDownCounter<long>("signalizr.inbound.queue_depth");
        _received = _meter.CreateCounter<long>("signalizr.inbound.received");
        _subscribers = _meter.CreateUpDownCounter<long>("signalizr.subscribers.active");
        _meter.CreateObservableGauge(
            "signalizr.inbound.stored_messages",
            () => Interlocked.Read(ref _storedMessages));
        ActivitySource = new ActivitySource(ActivitySourceName);
    }

    /// <summary>Activity source for persistence and delivery spans.</summary>
    public ActivitySource ActivitySource { get; }

    /// <summary>Records a message accepted into the process queue.</summary>
    public void RecordReceived()
    {
        _received.Add(1);
        _queueDepth.Add(1);
    }

    /// <summary>Records a message removed from the process queue.</summary>
    public void RecordDequeued() => _queueDepth.Add(-1);

    /// <summary>Records a message displaced from the bounded process queue.</summary>
    public void RecordDropped()
    {
        _dropped.Add(1);
        _queueDepth.Add(-1);
    }

    /// <summary>Records a successful durable write.</summary>
    /// <param name="elapsed">Time spent writing through EF Core.</param>
    public void RecordPersisted(TimeSpan elapsed)
    {
        _persisted.Add(1);
        Interlocked.Increment(ref _storedMessages);
        _persistenceDuration.Record(elapsed.TotalMilliseconds);
    }

    /// <summary>Records a delivery written to a subscriber stream.</summary>
    public void RecordDelivered() => _delivered.Add(1);

    /// <summary>Records a durable subscriber acknowledgement.</summary>
    public void RecordAcknowledged() => _acknowledged.Add(1);

    /// <summary>Records a subscriber disconnected after its acknowledgement budget timed out.</summary>
    public void RecordAcknowledgementTimeout() => _acknowledgementTimeouts.Add(1);

    /// <summary>Records a subscriber connection.</summary>
    public void RecordSubscriberConnected() => _subscribers.Add(1);

    /// <summary>Records a subscriber disconnection.</summary>
    public void RecordSubscriberDisconnected() => _subscribers.Add(-1);

    /// <summary>Initializes the stored-message gauge from durable storage.</summary>
    /// <param name="count">Current persisted message count.</param>
    public void SetStoredMessages(long count) => Interlocked.Exchange(ref _storedMessages, count);

    /// <summary>Records acknowledged messages removed by retention.</summary>
    /// <param name="count">Number of rows removed.</param>
    public void RecordPruned(long count)
    {
        if (count <= 0)
            return;

        _pruned.Add(count);
        Interlocked.Add(ref _storedMessages, -count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}