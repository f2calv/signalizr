namespace CasCap.IntegrationTests;

/// <summary>Runs the server host with deterministic configuration and no background feature execution.</summary>
public sealed class SignalizrWebApplicationFactory : WebApplicationFactory<AppEntryPoint>
{
    /// <summary>Metric prefix supplied to the test host.</summary>
    public const string TestMetricNamePrefix = "signalizr-tests";

    /// <summary>OpenTelemetry service name supplied to the test host.</summary>
    public const string TestOtelServiceName = "CasCap.Signalizr.Tests";

    private static readonly IReadOnlyDictionary<string, string?> _startupEnvironment =
        new Dictionary<string, string?>
        {
            [$"{AppConfig.ConfigurationSectionName}__{nameof(AppConfig.MetricNamePrefix)}"] = TestMetricNamePrefix,
            [$"{AppConfig.ConfigurationSectionName}__{nameof(AppConfig.OtelServiceName)}"] = TestOtelServiceName,
            ["CasCap__FeatureConfig__EnabledFeatures"] = FeatureNames.DemoClient,
            ["CasCap__SignalizrClientConfig__SubscriberName"] = "server-tests",
        };

    /// <inheritdoc/>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var previous = _startupEnvironment.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);

        foreach (var (key, value) in _startupEnvironment)
            Environment.SetEnvironmentVariable(key, value);

        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}
