using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>Holds typing indicators as leases: refreshed while held, cleared on stop or on expiry.</summary>
/// <remarks>
/// State is per process. That matches the single gateway replica deployed today; with several
/// replicas, a stop routed to a different replica than the start would leave the lease to expire.
/// </remarks>
public sealed class TypingLeaseService(
    ILogger<TypingLeaseService> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<GatewayConfig> gatewayConfig,
    TimeProvider timeProvider,
    ISignalCliClient client) : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _leases = new(StringComparer.Ordinal);

    /// <summary>Shows the indicator now and keeps it alive until stopped or expired.</summary>
    /// <returns><see langword="false"/> when the wrapper rejected the first indicator.</returns>
    public async Task<bool> StartAsync(string channelName, string groupId, CancellationToken cancellationToken = default)
    {
        var shown = await client.ShowTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId, cancellationToken)
            .ConfigureAwait(false);
        if (!shown)
            return false;

        var lease = new CancellationTokenSource();
        var previous = _leases.AddOrUpdate(channelName, lease, (_, _) => lease);
        if (!ReferenceEquals(previous, lease))
            Release(previous);

        _ = KeepAliveAsync(channelName, groupId, lease);
        return true;
    }

    /// <summary>Ends any held lease for the channel and clears the indicator.</summary>
    /// <returns><see langword="false"/> when the wrapper rejected the clear.</returns>
    public async Task<bool> StopAsync(string channelName, string groupId, CancellationToken cancellationToken = default)
    {
        if (_leases.TryRemove(channelName, out var lease))
            Release(lease);

        return await client.HideTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var lease in _leases.Values)
            Release(lease);
        _leases.Clear();
    }

    private async Task KeepAliveAsync(string channelName, string groupId, CancellationTokenSource lease)
    {
        var refresh = TimeSpan.FromMilliseconds(gatewayConfig.Value.TypingRefreshIntervalMs);
        var maxDuration = TimeSpan.FromMilliseconds(gatewayConfig.Value.TypingMaxDurationMs);
        var startedAt = timeProvider.GetTimestamp();
        var account = signalCliConfig.Value.PhoneNumber;
        try
        {
            while (true)
            {
                await Task.Delay(refresh, timeProvider, lease.Token).ConfigureAwait(false);
                if (timeProvider.GetElapsedTime(startedAt) >= maxDuration)
                    break;
                await client.ShowTypingIndicator(account, groupId, lease.Token).ConfigureAwait(false);
            }

            // Expired without a stop: the holder is gone or stuck, so the gateway clears it.
            if (_leases.TryRemove(new KeyValuePair<string, CancellationTokenSource>(channelName, lease)))
            {
                await client.HideTypingIndicator(account, groupId, CancellationToken.None).ConfigureAwait(false);
                logger.LogWarning("{ClassName} typing in channel {Channel} was not stopped within {MaxDuration}, cleared it",
                    nameof(TypingLeaseService), channelName, maxDuration);
                lease.Dispose();
            }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // Stopped or replaced; the stop path owns the clear.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{ClassName} could not refresh typing in channel {Channel}",
                nameof(TypingLeaseService), channelName);
        }
    }

    private static void Release(CancellationTokenSource lease)
    {
        try
        {
            lease.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already expired and disposed by its own keep-alive loop.
        }
    }
}
