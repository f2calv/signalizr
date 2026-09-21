using CasCap.Common.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Constants;
using CasCap.Extensions;
using CasCap.Models;
using CasCap.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logger = builder.InitializeSerilog(nameof(Program));

var featureConfig = builder.Configuration
    .GetSection(FeatureConfig.ConfigurationSectionName)
    .Get<FeatureConfig>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{FeatureConfig.ConfigurationSectionName}' is missing.");

var enabledFeatures = featureConfig.GetEnabledFeatures();

logger.LogInformation("{AppName} starting with features {@Features}",
    AppDomain.CurrentDomain.FriendlyName, enabledFeatures);

if (enabledFeatures.Contains(FeatureNames.Gateway))
    builder.Services.AddSingleton<IBgFeature, GatewayBgService>();

if (enabledFeatures.Contains(FeatureNames.Receiver))
    builder.Services.AddSingleton<IBgFeature, ReceiverBgService>();

//Only the roles that talk to Signal need an account, so a DemoClient container needs no phone number.
if (enabledFeatures.Contains(FeatureNames.Gateway) || enabledFeatures.Contains(FeatureNames.Receiver))
    builder.Services.AddSignalCli(builder.Configuration);

if (enabledFeatures.Contains(FeatureNames.DemoClient))
    builder.Services.AddSingleton<IBgFeature, DemoClientBgService>();

builder.Services.AddFeatureFlagService(enabledFeatures, addGitMetadataService: true);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/healthz");

await app.RunAsync();
