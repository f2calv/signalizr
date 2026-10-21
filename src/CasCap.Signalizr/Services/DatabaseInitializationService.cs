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
        var config = databaseConfig.Value;
        var provider = config.Provider;

        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (provider is DatabaseProvider.InMemory)
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }

        else if (config.MigrateOnStartup)
        {
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        metrics.SetStoredMessages(await dbContext.InboundMessages
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false));

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "{ClassName} initialized {Provider} durable-delivery storage",
                nameof(DatabaseInitializationService),
                provider);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
