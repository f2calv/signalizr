namespace CasCap.IntegrationTests;

/// <summary>Credential-free startup and dependency-injection checks for the server host.</summary>
[Trait("Category", "Integration")]
public sealed class HostStartupTests(SignalizrWebApplicationFactory factory)
    : IClassFixture<SignalizrWebApplicationFactory>
{
    [Fact]
    public void Services_ExposeConfiguredHostBaseline()
    {
        var appConfig = factory.Services.GetRequiredService<IOptions<AppConfig>>().Value;
        var featureConfig = factory.Services.GetRequiredService<IOptions<FeatureConfig>>().Value;
        var featureFlagConfig = factory.Services.GetRequiredService<IOptions<FeatureFlagConfig>>().Value;
        var hostLifecycleConfig = factory.Services.GetRequiredService<IOptions<HostLifecycleConfig>>().Value;

        Assert.Equal(SignalizrWebApplicationFactory.TestMetricNamePrefix, appConfig.MetricNamePrefix);
        Assert.Equal(SignalizrWebApplicationFactory.TestOtelServiceName, appConfig.OtelServiceName);
        Assert.Equal(FeatureNames.DemoClient, featureConfig.EnabledFeatures);
        Assert.Contains(FeatureNames.DemoClient, featureFlagConfig.EnabledFeatures);
        Assert.Equal(60_000, hostLifecycleConfig.ShutdownTimeoutMs);
        Assert.Equal(
            TimeSpan.FromMilliseconds(hostLifecycleConfig.ShutdownTimeoutMs),
            factory.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
        Assert.Same(TimeProvider.System, factory.Services.GetRequiredService<TimeProvider>());
        Assert.NotNull(factory.Services.GetRequiredService<SignalizrMetrics>());
        Assert.NotNull(factory.Services.GetRequiredService<IControllerActivator>());
    }

    [Fact]
    public async Task StartupHealthEndpoint_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz/startup", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
