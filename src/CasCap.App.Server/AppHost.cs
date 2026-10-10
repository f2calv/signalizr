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
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(string[] args, Assembly entryAssembly)
    {
        SerilogExtensions.GetBootstrapLogger();

        try
        {
            // Host builder
            var builder = WebApplication.CreateBuilder(args);

            // Configuration
            var (appConfig, enabledFeatures, gitMetadata) = builder.InitializeConfiguration(entryAssembly);

            // Logging
            var logger = SerilogWebApplicationBuilderExtensions.InitializeSerilog(builder);

            // Infrastructure
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<SignalizrMetrics>();

            // Observability
            builder.InitializeOpenTelemetry(
                appConfig,
                gitMetadata,
                configureMetrics: metrics => metrics
                    .AddMeter(appConfig.MetricNamePrefix)
                    .AddMeter(SignalCliTelemetry.MeterName)
                    .AddMeter("System.Net.Http"),
                configureTracing: tracing => tracing
                    .AddSource(appConfig.MetricNamePrefix)
                    .AddSource(SignalCliTelemetry.ActivitySourceName));

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

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("{ClassName} starting", nameof(AppHost));

            // Endpoint mapping
            MapEndpoints(app, enabledFeatures, servesGrpc);

            // Run
            await app.RunAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not TaskCanceledException)
        {
            Log.Fatal(exception, "{AppName} terminated unexpectedly", AppDomain.CurrentDomain.FriendlyName);
            throw new InvalidOperationException("Application host terminated unexpectedly.", exception);
        }
        finally
        {
            Log.Information("Stopped {AppName}", AppDomain.CurrentDomain.FriendlyName);
            await Log.CloseAndFlushAsync();
        }

        return 0;
    }
}
