using CasCap.Data;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Applies relational Signalizr migrations for the finite external migration job.</summary>
public sealed class SignalizrDatabaseMigrator(
    ILogger<SignalizrDatabaseMigrator> logger,
    IOptions<DatabaseConfig> databaseConfig,
    IDbContextFactory<SignalizrDbContext> dbContextFactory)
{
    /// <summary>Applies pending migrations and returns when the schema is current.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var config = databaseConfig.Value;

        if (config.Provider is DatabaseProvider.InMemory)
        {
            throw new InvalidOperationException(
                $"{nameof(FeatureNames.DbMigrator)} requires a relational database provider.");
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} applying pending migrations", nameof(SignalizrDatabaseMigrator));
        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} migrations applied", nameof(SignalizrDatabaseMigrator));
    }
}
