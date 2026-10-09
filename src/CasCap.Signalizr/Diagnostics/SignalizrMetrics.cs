using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CasCap.Diagnostics;

/// <summary>Low-cardinality metrics and traces for durable Signalizr delivery.</summary>
public sealed class SignalizrMetrics(IOptions<AppConfig>? appConfig = null) : IDisposable
{
    private readonly InstrumentSet _instruments = CreateInstruments(appConfig);

    /// <summary>Activity source for persistence and delivery spans.</summary>
    public ActivitySource ActivitySource => _instruments.ActivitySource;

    /// <summary>Records a message accepted into the process queue.</summary>
    public void RecordReceived()
    {
        _instruments.Received.Add(1);
        _instruments.QueueDepth.Add(1);
    }

    /// <summary>Records a message removed from the process queue.</summary>
    public void RecordDequeued() => _instruments.QueueDepth.Add(-1);

    /// <summary>Records a message displaced from the bounded process queue.</summary>
    public void RecordDropped()
    {
        _instruments.Dropped.Add(1);
        _instruments.QueueDepth.Add(-1);
    }

    /// <summary>Records a successful durable write.</summary>
    /// <param name="elapsed">Time spent writing through EF Core.</param>
    public void RecordPersisted(TimeSpan elapsed)
    {
        _instruments.Persisted.Add(1);
        Interlocked.Increment(ref _instruments.StoredMessages);
        _instruments.PersistenceDuration.Record(elapsed.TotalMilliseconds);
    }

    /// <summary>Records a delivery written to a subscriber stream.</summary>
    public void RecordDelivered() => _instruments.Delivered.Add(1);

    /// <summary>Records a durable subscriber acknowledgement.</summary>
    public void RecordAcknowledged() => _instruments.Acknowledged.Add(1);

    /// <summary>Records a subscriber disconnected after its acknowledgement budget timed out.</summary>
    public void RecordAcknowledgementTimeout() => _instruments.AcknowledgementTimeouts.Add(1);

    /// <summary>Records a subscriber connection.</summary>
    public void RecordSubscriberConnected() => _instruments.Subscribers.Add(1);

    /// <summary>Records a subscriber disconnection.</summary>
    public void RecordSubscriberDisconnected() => _instruments.Subscribers.Add(-1);

    /// <summary>Initializes the stored-message gauge from durable storage.</summary>
    /// <param name="count">Current persisted message count.</param>
    public void SetStoredMessages(long count) => Interlocked.Exchange(ref _instruments.StoredMessages, count);

    /// <summary>Records acknowledged messages removed by retention.</summary>
    /// <param name="count">Number of rows removed.</param>
    public void RecordPruned(long count)
    {
        if (count <= 0)
            return;

        _instruments.Pruned.Add(count);
        Interlocked.Add(ref _instruments.StoredMessages, -count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _instruments.ActivitySource.Dispose();
        _instruments.Meter.Dispose();
    }

    private static InstrumentSet CreateInstruments(IOptions<AppConfig>? appConfig)
    {
        var prefix = appConfig?.Value.MetricNamePrefix ?? new AppConfig().MetricNamePrefix;
        var meter = new Meter(prefix);
        var instruments = new InstrumentSet
        {
            Meter = meter,
            Acknowledged = meter.CreateCounter<long>(
                $"{prefix}.subscriber.acknowledged", "1", "Durable subscriber acknowledgements."),
            AcknowledgementTimeouts = meter.CreateCounter<long>(
                $"{prefix}.subscriber.acknowledgement_timeouts", "1", "Subscribers disconnected after acknowledgement timeout."),
            Delivered = meter.CreateCounter<long>(
                $"{prefix}.subscriber.delivered", "1", "Messages delivered to subscriber streams."),
            Dropped = meter.CreateCounter<long>(
                $"{prefix}.inbound.dropped", "1", "Inbound messages displaced from the bounded process queue."),
            Persisted = meter.CreateCounter<long>(
                $"{prefix}.inbound.persisted", "1", "Inbound messages committed to durable storage."),
            Pruned = meter.CreateCounter<long>(
                $"{prefix}.inbound.pruned", "1", "Persisted messages removed by retention."),
            PersistenceDuration = meter.CreateHistogram<double>(
                $"{prefix}.inbound.persistence_duration", "ms", "EF Core inbound-message persistence duration."),
            QueueDepth = meter.CreateUpDownCounter<long>(
                $"{prefix}.inbound.queue_depth", "1", "Current messages in the bounded inbound process queue."),
            Received = meter.CreateCounter<long>(
                $"{prefix}.inbound.received", "1", "Inbound messages accepted into the process queue."),
            Subscribers = meter.CreateUpDownCounter<long>(
                $"{prefix}.subscribers.active", "1", "Current connected durable subscribers."),
            ActivitySource = new ActivitySource(prefix)
        };
        // An annotation unit keeps the Prometheus name unchanged: the OTLP translation appends "_ratio" to a gauge
        // whose unit is "1", which would hide it from the dashboard's signalizr_inbound_stored_messages query.
        meter.CreateObservableGauge(
            $"{prefix}.inbound.stored_messages",
            () => Interlocked.Read(ref instruments.StoredMessages),
            "{message}",
            "Current persisted inbound messages available for replay.");
        return instruments;
    }

    private sealed class InstrumentSet
    {
        public required Meter Meter { get; init; }

        public required ActivitySource ActivitySource { get; init; }

        public required Counter<long> Acknowledged { get; init; }

        public required Counter<long> AcknowledgementTimeouts { get; init; }

        public required Counter<long> Delivered { get; init; }

        public required Counter<long> Dropped { get; init; }

        public required Counter<long> Persisted { get; init; }

        public required Counter<long> Pruned { get; init; }

        public required Histogram<double> PersistenceDuration { get; init; }

        public required UpDownCounter<long> QueueDepth { get; init; }

        public required Counter<long> Received { get; init; }

        public required UpDownCounter<long> Subscribers { get; init; }

        public long StoredMessages;
    }
}
