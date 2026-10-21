namespace CasCap.Models;

/// <summary>Application-wide telemetry configuration.</summary>
public sealed record AppConfig : IAppConfig, IMetricsConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => nameof(AppConfig);

    /// <inheritdoc/>
    [Required, MinLength(1)]
    public string MetricNamePrefix { get; init; } = "signalizr";

    /// <inheritdoc/>
    [Required, MinLength(1)]
    public string OtelServiceName { get; init; } = "CasCap.Signalizr";

    /// <inheritdoc/>
    public Uri? OtlpExporterEndpoint { get; init; }

    /// <summary>
    /// Gets the Key Vault secret-name prefix holding this host's Agent Runtime execution identity, for
    /// example <c>AgentRuntime--MyApp--Exec</c>, mapped into <c>CasCap:AgentRuntimeAzureAuthConfig</c>.
    /// </summary>
    /// <remarks>
    /// Setting <c>AppConfig:KeyVaultName</c> loads the shared vault with the standard <c>--</c> to
    /// <c>:</c> mapping. Every other <c>AgentRuntime--*</c> identity is excluded, so a host never sees
    /// another application's credential. Requires <c>AppConfig:KeyVaultName</c> when set.
    /// </remarks>
    public string? AgentRuntimeKeyVaultPrefix { get; init; }
}
