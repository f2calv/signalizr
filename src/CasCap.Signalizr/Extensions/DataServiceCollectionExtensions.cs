using CasCap.Data;
using CasCap.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI extensions for Signalizr durable delivery persistence.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Registers <see cref="SignalizrDbContext"/> using the configured EF Core provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="addRuntimeServices">
    /// Whether to register database initialization and retention services. The one-shot migrator
    /// disables them because it owns schema application directly.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSignalizrDataLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        bool addRuntimeServices = true)
    {
        var databaseConfig = configuration
            .GetSection(DatabaseConfig.ConfigurationSectionName)
            .Get<DatabaseConfig>() ?? new DatabaseConfig();

        services.AddOptionsWithValidateOnStart<DatabaseConfig>()
            .BindConfiguration(DatabaseConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        services.AddDbContextFactory<SignalizrDbContext>(options =>
        {
            switch (databaseConfig.Provider)
            {
                case DatabaseProvider.InMemory:
                    options.UseInMemoryDatabase("Signalizr");
                    break;
                case DatabaseProvider.Sqlite:
                    options.UseSqlite(databaseConfig.ConnectionString);
                    break;
                case DatabaseProvider.Postgres:
                    options.UseNpgsql(databaseConfig.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported {nameof(DatabaseProvider)} value {databaseConfig.Provider}.");
            }
        });

        if (addRuntimeServices)
        {
            services.AddHostedService<DatabaseInitializationService>();
            services.AddHostedService<DurableMessagePrunerService>();
        }

        return services;
    }
}