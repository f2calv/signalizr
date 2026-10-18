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
    /// Gets the Key Vault secret-name prefix holding the Agent Runtime execution identity, for example
    /// <c>AgentRuntime--MyApp--Exec</c>. When set, only secrets under it are loaded from
    /// <c>AppConfig:KeyVaultName</c> into <c>CasCap:AgentRuntimeAzureAuthConfig</c>; when unset, Key
    /// Vault is not used.
    /// </summary>
    public string? AgentRuntimeKeyVaultPrefix { get; init; }
}
