using CasCap.Data;
using CasCap.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace CasCap.Services;

/// <summary>Initializes the configured durable-delivery database before receiver work starts.</summary>
public sealed class DatabaseInitializationService(
    ILogger<DatabaseInitializationService> logger,
    IOptions<DatabaseConfig> databaseConfig,
    SignalizrMetrics metrics,
    IDbContextFactory<SignalizrDbContext> dbContextFactory) : IHostedService
{
    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (databaseConfig.Value.Provider is DatabaseProvider.InMemory)
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }

        else if (databaseConfig.Value.MigrateOnStartup)
        {
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        metrics.SetStoredMessages(await dbContext.InboundMessages
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false));

        logger.LogInformation(
            "{ClassName} initialized {Provider} durable-delivery storage",
            nameof(DatabaseInitializationService),
            databaseConfig.Value.Provider);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}