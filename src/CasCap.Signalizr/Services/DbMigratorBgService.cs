using CasCap.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace CasCap.Services;

/// <summary>Applies relational EF Core migrations and stops the one-shot host.</summary>
public sealed class DbMigratorBgService(
    ILogger<DbMigratorBgService> logger,
    IOptions<DatabaseConfig> databaseConfig,
    IHostApplicationLifetime appLifetime,
    IDbContextFactory<SignalizrDbContext> dbContextFactory) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.DbMigrator;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (databaseConfig.Value.Provider is DatabaseProvider.InMemory)
        {
            throw new InvalidOperationException(
                $"{nameof(FeatureNames.DbMigrator)} requires a relational database provider.");
        }

        logger.LogInformation("{ClassName} applying pending migrations", nameof(DbMigratorBgService));
        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{ClassName} migrations applied", nameof(DbMigratorBgService));

        await Task.Delay(Timeout.InfiniteTimeSpan, appLifetime.ApplicationStarted)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        appLifetime.StopApplication();
    }
}