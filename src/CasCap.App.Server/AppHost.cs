using CasCap.Common.Extensions;
using CasCap.Common.Models;
using CasCap.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

namespace CasCap;

/// <summary>Builds and runs the signalizr application host.</summary>
public static partial class AppHost
{
    /// <summary>Bootstraps and runs the application.</summary>
    /// <param name="args">Command-line arguments forwarded from the entry point.</param>
    public static async Task RunAsync(string[] args)
    {
        // Host builder
        // TODO: Add shared bootstrap logging before CreateBuilder so pre-host configuration failures are captured consistently with other hosts.
        var builder = WebApplication.CreateBuilder(args);

        // Logging
        // Logging stays before local binding so configuration failures remain observable.
        var logger = builder.InitializeSerilog(nameof(Program));

        // Configuration
        var appConfig = builder.Configuration
            .GetSection(AppConfig.ConfigurationSectionName)
            .Get<AppConfig>() ?? new AppConfig();
        builder.Services.AddOptionsWithValidateOnStart<AppConfig>()
            .BindConfiguration(AppConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var featureConfig = builder.Configuration
            .GetSection(FeatureConfig.ConfigurationSectionName)
            .Get<FeatureConfig>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{FeatureConfig.ConfigurationSectionName}' is missing.");
        builder.Services.AddOptionsWithValidateOnStart<FeatureConfig>()
            .BindConfiguration(FeatureConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        // Infrastructure
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SignalizrMetrics>();

        // Observability
        // TODO: Source GitMetadata from configuration instead of constructing defaults so telemetry carries deployed build metadata consistently.
        builder.InitializeOpenTelemetry(
            appConfig,
            new GitMetadata(),
            configureMetrics: metrics => metrics
                .AddMeter(appConfig.MetricNamePrefix)
                .AddMeter(SignalCliTelemetry.MeterName),
            configureTracing: tracing => tracing
                .AddSource(appConfig.MetricNamePrefix)
                .AddSource(SignalCliTelemetry.ActivitySourceName));

        // Feature validation and startup diagnostics
        var enabledFeatures = featureConfig.GetEnabledFeatures();

        logger.LogInformation("{AppName} starting with features {@Features}",
            AppDomain.CurrentDomain.FriendlyName, enabledFeatures);

        // Feature registration
        AddFeatures(builder, enabledFeatures);

        // Web API registration
        builder.Services.AddFeatureFlagService(enabledFeatures, addGitMetadataService: true);
        AddWebApi(builder, enabledFeatures);

        // Transport registration
        var servesGrpc = ConfigureGrpc(builder, enabledFeatures, logger);

        // Build
        var app = builder.Build();

        // Endpoint mapping
        MapEndpoints(app, enabledFeatures, servesGrpc);

        // Run
        await app.RunAsync();
    }
}
