using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace CasCap.Services;

/// <inheritdoc cref="IInboundSubscriberRegistry"/>
public sealed class InboundSubscriberRegistry(
    ILogger<InboundSubscriberRegistry> logger,
    IOptions<SubscriberConfig> config,
    TimeProvider timeProvider,
    SignalizrMetrics metrics,
    IDbContextFactory<SignalizrDbContext> dbContextFactory,
    IOperatorNotifier operatorNotifier) : IInboundSubscriberRegistry
{
    // TODO: Introduce durable ownership/fencing before allowing overlapping gateway replicas.
    // This registry enforces identity only within one process; retain single-owner deployment until
    // distributed cursor concurrency and takeover/replay behavior have explicit tests.
    // A subscriber whose pod died without closing its stream (observed after a k3s control-plane
    // restart) stays registered, so every reconnect fails with AlreadyExists until this process
    // restarts. Detect dead streams (HTTP/2 keepalive or a server-side idle deadline) or let a newer
    // connection with the same identity take over the stale subscription.
    private readonly ConcurrentDictionary<string, InboundSubscription> _subscriptions =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    /// <inheritdoc/>
    public int Count => _subscriptions.Count;

    /// <inheritdoc/>
    public async Task<InboundSubscription> SubscribeAsync(
        string subscriberName,
        CancellationToken cancellationToken = default)
    {
        await _registrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = config.Value;
            if (!options.SubscriberGroups.TryGetValue(subscriberName, out var groups) || groups.Length == 0)
                throw new SubscriberNotPermittedException(subscriberName);

            if (_subscriptions.ContainsKey(subscriberName))
                throw new SubscriberAlreadyConnectedException(subscriberName);

            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var cursor = await dbContext.SubscriberCursors
                .SingleOrDefaultAsync(candidate => candidate.SubscriberName == subscriberName, cancellationToken)
                .ConfigureAwait(false);

            if (cursor is null)
            {
                var currentTail = await dbContext.InboundMessages
                    .Select(message => (long?)message.Id)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) ?? 0;
                cursor = new SubscriberCursorEntity
                {
                    SubscriberName = subscriberName,
                    LastAcknowledgedMessageId = currentTail,
                    UpdatedAtUtc = timeProvider.GetUtcNow()
                };
                dbContext.SubscriberCursors.Add(cursor);
            }
            else
            {
                cursor.UpdatedAtUtc = timeProvider.GetUtcNow();
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var subscription = new InboundSubscription(
                subscriberName,
                groups,
                options.ReplayBatchSize,
                options.MaxOutstanding,
                cursor.LastAcknowledgedMessageId,
                timeProvider,
                metrics,
                dbContextFactory);

            if (!_subscriptions.TryAdd(subscriberName, subscription))
            {
                subscription.Dispose();
                throw new SubscriberAlreadyConnectedException(subscriberName);
            }

            subscription.NotifyMessageAvailable();
            metrics.RecordSubscriberConnected();
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("{ClassName} subscriber connected, {Count} total",
                    nameof(InboundSubscriberRegistry), _subscriptions.Count);
            }
            operatorNotifier.Notify($"{subscriberName} connected ({_subscriptions.Count} subscriber(s))");

            return subscription;
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Unsubscribe(InboundSubscription subscription, Exception? error = null)
    {
        if (!_subscriptions.TryGetValue(subscription.Name, out var current)
            || !ReferenceEquals(current, subscription)
            || !_subscriptions.TryRemove(subscription.Name, out _))
            return;

        subscription.Complete(error);
        metrics.RecordSubscriberDisconnected();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{ClassName} subscriber disconnected, {Count} remaining",
                nameof(InboundSubscriberRegistry), _subscriptions.Count);
        }
        // The exception type only: its message can carry details from the transport.
        operatorNotifier.Notify($"{subscription.Name} disconnected ({_subscriptions.Count} remaining)"
            + (error is null ? string.Empty : $", {error.GetType().Name}"));
    }

    /// <inheritdoc/>
    public void NotifyMessageAvailable()
    {
        foreach (var subscription in _subscriptions.Values)
            subscription.NotifyMessageAvailable();
    }
}
