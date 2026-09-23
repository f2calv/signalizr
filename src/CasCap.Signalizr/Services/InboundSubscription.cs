using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CasCap.Services;

/// <summary>One connected subscriber's delivery queue and outstanding-acknowledgement budget.</summary>
/// <remarks>
/// Each subscriber owns its queue so a slow one cannot stall the dispatcher, and owns its budget so
/// it cannot accumulate unbounded unacknowledged work. Both limits fail the subscriber loudly
/// rather than dropping its messages quietly, which is the defect this service exists to avoid.
/// </remarks>
public sealed class InboundSubscription : IDisposable
{
    private readonly Channel<InboundDelivery> _channel;
    private readonly SemaphoreSlim _budget;
    private readonly ConcurrentDictionary<string, byte> _outstanding = new(StringComparer.Ordinal);

    internal InboundSubscription(string name, int queueCapacity, int maxOutstanding)
    {
        Name = name;
        _channel = Channel.CreateBounded<InboundDelivery>(new BoundedChannelOptions(queueCapacity)
        {
            // Never drop and never block the dispatcher: a full queue means this subscriber is too
            // slow, which is reported by failing it rather than by quietly losing its messages.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _budget = new SemaphoreSlim(maxOutstanding, maxOutstanding);
    }

    /// <summary>Diagnostic name supplied by the subscriber. Not unique and never used for routing.</summary>
    public string Name { get; }

    /// <summary>Queues a delivery without waiting.</summary>
    /// <returns><see langword="false"/> when the subscriber is too far behind to accept it.</returns>
    public bool TryEnqueue(InboundDelivery delivery) => _channel.Writer.TryWrite(delivery);

    /// <summary>Reads queued deliveries until the token is cancelled or the subscription ends.</summary>
    public IAsyncEnumerable<InboundDelivery> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Takes one unit of the outstanding-acknowledgement budget, waiting when it is exhausted.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the budget did not free up in time, meaning the subscriber is
    /// receiving but not acknowledging.
    /// </returns>
    public async Task<bool> TryReserveAsync(
        string deliveryId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!await _budget.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return false;

        if (_outstanding.TryAdd(deliveryId, 0))
            return true;

        _budget.Release();
        return false;
    }

    /// <summary>Returns the budget reserved for one delivery.</summary>
    /// <param name="deliveryId">The identifier assigned to this subscriber's delivery.</param>
    /// <returns><see langword="true"/> when the delivery was outstanding.</returns>
    public bool Acknowledge(string deliveryId)
    {
        if (!_outstanding.TryRemove(deliveryId, out _))
            return false;

        _budget.Release();
        return true;
    }

    /// <summary>Ends the subscription so its reader completes.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc/>
    public void Dispose()
    {
        Complete();
        _budget.Dispose();
    }
}
