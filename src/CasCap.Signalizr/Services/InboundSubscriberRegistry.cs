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
    IDbContextFactory<SignalizrDbContext> dbContextFactory) : IInboundSubscriberRegistry
{
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

            var options = config.Value;
            var subscription = new InboundSubscription(
                subscriberName,
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
            logger.LogInformation("{ClassName} subscriber connected, {Count} total",
                nameof(InboundSubscriberRegistry), _subscriptions.Count);

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

        logger.LogInformation("{ClassName} subscriber disconnected, {Count} remaining",
            nameof(InboundSubscriberRegistry), _subscriptions.Count);
    }

    /// <inheritdoc/>
    public void NotifyMessageAvailable()
    {
        foreach (var subscription in _subscriptions.Values)
            subscription.NotifyMessageAvailable();
    }
}
