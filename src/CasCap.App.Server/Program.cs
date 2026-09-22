using CasCap.Abstractions;
using CasCap.Common.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Constants;
using CasCap.Extensions;
using CasCap.Models;
using CasCap.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
{
    builder.Services.AddSignalCli(builder.Configuration);
    builder.Services.Configure<ChannelConfig>(
        builder.Configuration.GetSection(ChannelConfig.ConfigurationSectionName));
    builder.Services.AddSingleton<IChannelResolver, ChannelResolver>();
}

if (enabledFeatures.Contains(FeatureNames.Gateway))
    builder.Services.AddSingleton<IMessageGateway, MessageGateway>();

if (enabledFeatures.Contains(FeatureNames.DemoClient))
    builder.Services.AddSingleton<IBgFeature, DemoClientBgService>();

builder.Services.AddFeatureFlagService(enabledFeatures, addGitMetadataService: true);
builder.Services.AddHealthChecks();

// Every controller compiles into every image, so a controller whose dependency is registered only
// for one role would throw on activation elsewhere. Gating removes it from routing entirely.
builder.Services.AddControllers().AddFeatureGatedControllers(enabledFeatures);

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/healthz");

// One endpoint per Kubernetes probe type, each running only the checks carrying that tag.
// The upstream signal-cli check is tagged "ready" by default, so an unreachable wrapper takes a
// pod out of the Service rotation without also failing liveness — a liveness probe that depended
// on the upstream would restart every gateway pod during a wrapper outage it cannot fix.
foreach (var probeType in Enum.GetValues<KubernetesProbeTypes>())
{
    if (probeType is KubernetesProbeTypes.None)
        continue;

    var tag = probeType.GetDescription();
    app.MapHealthChecks($"/healthz/{tag}", new HealthCheckOptions
    {
        Predicate = healthCheck => healthCheck.Tags.Contains(tag)
    });
}

await app.RunAsync();
