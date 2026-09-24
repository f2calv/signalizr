using CasCap.Abstractions;
using CasCap.Common.Abstractions;
using CasCap.Common.Extensions;
using CasCap.Common.Models;
using CasCap.Constants;
using CasCap.Diagnostics;
using CasCap.Extensions;
using CasCap.Models;
using CasCap.Services;
using CasCap.Signalizr.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var logger = builder.InitializeSerilog(nameof(Program));

var appConfig = builder.Configuration
    .GetSection(AppConfig.ConfigurationSectionName)
    .Get<AppConfig>() ?? new AppConfig();
builder.Services.AddOptionsWithValidateOnStart<AppConfig>()
    .BindConfiguration(AppConfig.ConfigurationSectionName)
    .ValidateDataAnnotations();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SignalizrMetrics>();
builder.InitializeOpenTelemetry(
    appConfig,
    new GitMetadata(),
    configureMetrics: metrics => metrics
        .AddMeter(SignalizrMetrics.MeterName)
        .AddMeter(SignalCliTelemetry.MeterName),
    configureTracing: tracing => tracing
        .AddSource(SignalizrMetrics.ActivitySourceName)
        .AddSource(SignalCliTelemetry.ActivitySourceName));

var featureConfig = builder.Configuration
    .GetSection(FeatureConfig.ConfigurationSectionName)
    .Get<FeatureConfig>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{FeatureConfig.ConfigurationSectionName}' is missing.");

var enabledFeatures = featureConfig.GetEnabledFeatures();

logger.LogInformation("{AppName} starting with features {@Features}",
    AppDomain.CurrentDomain.FriendlyName, enabledFeatures);

if (enabledFeatures.Contains(FeatureNames.DbMigrator))
{
    builder.Services.AddSignalizrDataLayer(builder.Configuration, addRuntimeServices: false);
    builder.Services.AddSingleton<IBgFeature, DbMigratorBgService>();
}

if (enabledFeatures.Contains(FeatureNames.Gateway))
    builder.Services.AddSingleton<IBgFeature, GatewayBgService>();

if (enabledFeatures.Contains(FeatureNames.Receiver))
{
    builder.Services.AddSignalizrDataLayer(builder.Configuration);
    builder.Services.Configure<ReceiverConfig>(
        builder.Configuration.GetSection(ReceiverConfig.ConfigurationSectionName));
    builder.Services.Configure<SubscriberConfig>(
        builder.Configuration.GetSection(SubscriberConfig.ConfigurationSectionName));
    builder.Services.AddSingleton<IInboundMessageQueue, InboundMessageQueue>();
    builder.Services.AddSingleton<IInboundSubscriberRegistry, InboundSubscriberRegistry>();
    builder.Services.AddSingleton<IBgFeature, ReceiverBgService>();
    // Separate from the receive loop: per-message work belongs here, where it cannot stop the
    // upstream being read.
    builder.Services.AddSingleton<IBgFeature, DispatcherBgService>();
}

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
{
    // The demo consumes the gateway like any other client, over its published package.
    builder.Services.AddSignalizrClient(builder.Configuration);
    builder.Services.AddSingleton<IBgFeature, DemoClientBgService>();
}

builder.Services.AddFeatureFlagService(enabledFeatures, addGitMetadataService: true);
builder.Services.AddHealthChecks();

// Every controller compiles into every image, so a controller whose dependency is registered only
// for one role would throw on activation elsewhere. Gating removes it from routing entirely.
builder.Services.AddControllers().AddFeatureGatedControllers(enabledFeatures);

// Only the receiver holds the inbound stream, so only it serves subscriptions and only it needs
// the second endpoint.
var servesGrpc = enabledFeatures.Contains(FeatureNames.Receiver);
if (servesGrpc)
{
    var grpcHostConfig = builder.Configuration
        .GetSection(GrpcHostConfig.ConfigurationSectionName)
        .Get<GrpcHostConfig>() ?? new GrpcHostConfig();

    builder.Services.AddGrpc();

    // A plaintext port cannot negotiate protocols: without TLS there is no ALPN, so one endpoint
    // answers HTTP/1.1 or HTTP/2, not both. Sharing one fails at the first gRPC call with
    // HTTP_1_1_REQUIRED, which is why REST and gRPC get a port each.
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(grpcHostConfig.Http1Port, listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenAnyIP(grpcHostConfig.Http2Port, listen => listen.Protocols = HttpProtocols.Http2);
    });

    logger.LogInformation("{AppName} serving HTTP/1.1 on {Http1Port} and gRPC on {Http2Port}",
        AppDomain.CurrentDomain.FriendlyName, grpcHostConfig.Http1Port, grpcHostConfig.Http2Port);
}

var app = builder.Build();

app.MapControllers();

if (servesGrpc)
    app.MapGrpcService<InboundGrpcService>();

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
