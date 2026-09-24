using CasCap.Data;
using CasCap.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace CasCap.Services;

/// <summary>Removes acknowledged messages early and enforces the maximum message retention window.</summary>
public sealed class DurableMessagePrunerService(
    ILogger<DurableMessagePrunerService> logger,
    IOptions<DatabaseConfig> databaseConfig,
    TimeProvider timeProvider,
    SignalizrMetrics metrics,
    IDbContextFactory<SignalizrDbContext> dbContextFactory) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(databaseConfig.Value.PruneIntervalMs);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
            await PruneOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one retention pass.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PruneOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var minimumAcknowledgedId = await dbContext.SubscriberCursors
            .Select(cursor => (long?)cursor.LastAcknowledgedMessageId)
            .MinAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var acknowledgedCutoff = now
            .AddHours(-databaseConfig.Value.AcknowledgedMessageRetentionHours);
        var hardCutoff = now.AddDays(-databaseConfig.Value.MessageRetentionDays);
        var query = dbContext.InboundMessages.Where(message =>
            message.PersistedAtUtc < hardCutoff
            || (minimumAcknowledgedId != null
                && message.Id <= minimumAcknowledgedId
                && message.PersistedAtUtc < acknowledgedCutoff));
        int deleted;
        if (databaseConfig.Value.Provider is DatabaseProvider.InMemory)
        {
            var messages = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            dbContext.InboundMessages.RemoveRange(messages);
            deleted = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            deleted = await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        metrics.RecordPruned(deleted);
        if (deleted > 0)
        {
            logger.LogInformation("{ClassName} pruned {MessageCount} acknowledged message(s)",
                nameof(DurableMessagePrunerService), deleted);
        }
    }
}