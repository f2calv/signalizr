namespace CasCap;

public static partial class AppHost
{
    private const string MigrateArgument = "--migrate";

    private static void AddDatabaseMigration(WebApplicationBuilder builder)
    {
        builder.Services.AddSignalizrDataLayer(builder.Configuration, addRuntimeServices: false);
        builder.Services.AddScoped<SignalizrDatabaseMigrator>();
    }

    private static async Task RunDatabaseMigrationAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<SignalizrDatabaseMigrator>()
            .MigrateAsync(cancellationToken);
    }
}
