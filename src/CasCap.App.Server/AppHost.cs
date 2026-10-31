using CasCap.Common.Extensions;
using CasCap.Diagnostics;
using CasCap.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using System.Reflection;

namespace CasCap;

/// <summary>Builds and runs the signalizr application host.</summary>
public static partial class AppHost
{
    /// <summary>Bootstraps and runs the application.</summary>
    /// <param name="args">Command-line arguments forwarded from the entry point.</param>
    /// <param name="entryAssembly">Entry assembly used for configuration and user-secrets resolution.</param>
    /// <returns>The process exit code: zero after normal shutdown, or one after an unexpected fault.</returns>
    public static async Task<int> RunAsync(string[] args, Assembly entryAssembly)
    {
        SerilogExtensions.GetBootstrapLogger();

        try
        {
            var migrateArgument = args.Contains(MigrateArgument, StringComparer.Ordinal);
            var configurationArgs = args.Where(argument => argument != MigrateArgument).ToArray();

            // Host builder
            var builder = WebApplication.CreateBuilder(configurationArgs);
            builder.Services.AddApplicationExitState();

            // Configuration
            var (appConfig, configuredFeatures, applicationMetadata) = builder.InitializeConfiguration(entryAssembly);
            var enabledFeatures = migrateArgument
                ? new HashSet<string>([FeatureNames.DbMigrator], StringComparer.OrdinalIgnoreCase)
                : configuredFeatures;
            var migrateOnly = enabledFeatures.SetEquals([FeatureNames.DbMigrator]);

            // Logging
            var logger = SerilogWebApplicationBuilderExtensions.InitializeSerilog(builder);

            // Infrastructure
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SignalizrMetrics>();

            // Observability
            builder.InitializeOpenTelemetry(
                appConfig,
                applicationMetadata,
                configureMetrics: metrics => metrics
                    .AddMeter(appConfig.MetricNamePrefix)
                    .AddMeter(SignalCliTelemetry.MeterName)
                    .AddMeter("System.Net.Http"),
                configureTracing: tracing => tracing
                    .AddSource(appConfig.MetricNamePrefix)
                    .AddSource(SignalCliTelemetry.ActivitySourceName));

            if (migrateOnly)
            {
                AddDatabaseMigration(builder);
                await using var migrationApp = builder.Build();
                await RunDatabaseMigrationAsync(migrationApp.Services, CancellationToken.None);
                return 0;
            }

            // Feature validation and startup diagnostics
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} {AppName} running with features {@Features}",
                    nameof(AppHost), AppDomain.CurrentDomain.FriendlyName, enabledFeatures);

            // Feature registration
            AddFeatures(builder, enabledFeatures);

            // Web API registration
            AddWebApi(builder, enabledFeatures);

            // Transport registration
            var servesGrpc = ConfigureGrpc(builder, enabledFeatures, logger);

            // Build
            var app = builder.Build();
            var exitState = app.Services.GetApplicationExitState();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} starting", nameof(AppHost));

            // Endpoint mapping
            MapEndpoints(app, enabledFeatures, servesGrpc);

            // Run
            await app.RunAsync();
            return exitState.ExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not TaskCanceledException)
        {
            Log.Fatal(exception, "{AppName} terminated unexpectedly", AppDomain.CurrentDomain.FriendlyName);
            return 1;
        }
        finally
        {
            Log.Information("Stopped {AppName}", AppDomain.CurrentDomain.FriendlyName);
            await Log.CloseAndFlushAsync();
        }

    }
}
