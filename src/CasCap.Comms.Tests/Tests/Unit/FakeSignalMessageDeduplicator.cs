namespace CasCap.Tests.Unit;

/// <summary>
/// <see cref="ISignalMessageDeduplicator"/> substitute with a configurable claim outcome that
/// records every reservation and release.
/// </summary>
public sealed class FakeSignalMessageDeduplicator : ISignalMessageDeduplicator
{
    /// <summary>Whether the next claim succeeds.</summary>
    public bool ClaimResult { get; set; } = true;

    /// <summary>Every identity presented to <see cref="TryClaimAsync"/>, in order.</summary>
    public ConcurrentQueue<SignalMessageIdentity> Claims { get; } = new();

    /// <summary>Every identity presented to <see cref="ReleaseAsync"/>, in order.</summary>
    public ConcurrentQueue<SignalMessageIdentity> Releases { get; } = new();

    /// <summary>When set, the first claim waits on this task without observing cancellation, simulating a hung store.</summary>
    public Task? HangFirstClaim { get; set; }

    private int _hung;

    /// <inheritdoc/>
    public async Task<bool> TryClaimAsync(SignalMessageIdentity identity, CancellationToken cancellationToken = default)
    {
        Claims.Enqueue(identity);
        if (HangFirstClaim is { } hang && Interlocked.Exchange(ref _hung, 1) == 0)
            await hang;
        return ClaimResult;
    }

    /// <inheritdoc/>
    public Task ReleaseAsync(SignalMessageIdentity identity, CancellationToken cancellationToken = default)
    {
        Releases.Enqueue(identity);
        return Task.CompletedTask;
    }
}
