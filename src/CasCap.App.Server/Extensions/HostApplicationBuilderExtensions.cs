using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using CasCap.Common.Extensions;
using CasCap.Common.Models;
using CasCap.Models;
using System.Reflection;

namespace CasCap.Extensions;

/// <summary>Provides standard configuration bootstrapping for the Signalizr host.</summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>Adds standard configuration sources and binds application configuration.</summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="assembly">Assembly used for user-secrets loading.</param>
    /// <returns>The application configuration, enabled features and deployment metadata.</returns>
    public static (AppConfig appConfig, HashSet<string> enabledFeatures, ApplicationMetadata applicationMetadata) InitializeConfiguration(
        this IHostApplicationBuilder builder,
        Assembly assembly)
    {
        builder.Configuration.AddStandardConfiguration(builder.Environment.EnvironmentName, assembly);

        var appConfig = builder.Configuration
            .GetSection(AppConfig.ConfigurationSectionName)
            .Get<AppConfig>() ?? new AppConfig();
        builder.Services.AddOptionsWithValidateOnStart<AppConfig>()
            .BindConfiguration(AppConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        if (!string.IsNullOrWhiteSpace(appConfig.AgentRuntimeKeyVaultPrefix) || HasKeyVaultName(builder.Configuration))
            builder.AddKeyVaultSecrets(appConfig);

        var featureConfig = builder.Configuration
            .GetSection(FeatureConfig.ConfigurationSectionName)
            .Get<FeatureConfig>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{FeatureConfig.ConfigurationSectionName}' is missing.");
        builder.Services.AddOptionsWithValidateOnStart<FeatureConfig>()
            .BindConfiguration(FeatureConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var applicationMetadata = new ApplicationMetadata(assembly);
        builder.Services.AddSingleton(applicationMetadata);

        return (appConfig, featureConfig.GetEnabledFeatures(), applicationMetadata);
    }

    private static bool HasKeyVaultName(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{AzureAuthConfig.ConfigurationSectionName}:{nameof(AzureAuthConfig.KeyVaultName)}"]);

    private static void AddKeyVaultSecrets(this IHostApplicationBuilder builder, AppConfig appConfig)
    {
        var azureAuthConfig = builder.Configuration
            .GetSection(AzureAuthConfig.ConfigurationSectionName)
            .Get<AzureAuthConfig>();
        if (string.IsNullOrWhiteSpace(azureAuthConfig?.KeyVaultName))
            throw new InvalidOperationException(
                "AppConfig:AgentRuntimeKeyVaultPrefix requires AppConfig:KeyVaultName.");
        if (!azureAuthConfig.IsKeyVaultEnabled)
            return;
        var credential = azureAuthConfig.TokenCredential
            ?? throw new InvalidOperationException(
                "AppConfig:KeyVaultName requires an Azure Entra credential in AppConfig.");

        // Every Agent Runtime identity sits under one reserved root; only this host's own prefix is loaded.
        KeyVaultSecretManager secretManager = string.IsNullOrWhiteSpace(appConfig.AgentRuntimeKeyVaultPrefix)
            ? new ExcludedRootKeyVaultSecretManager(AgentRuntimeRootPrefix)
            : new PrefixKeyVaultSecretManager(
                appConfig.AgentRuntimeKeyVaultPrefix,
                AgentRuntimeAzureAuthConfig.ConfigurationSectionName,
                AgentRuntimeRootPrefix);
        builder.Configuration.AddKeyVaultConfiguration(azureAuthConfig.KeyVaultUri, credential, secretManager);
    }

    private const string AgentRuntimeRootPrefix = "AgentRuntime";

    private sealed class ExcludedRootKeyVaultSecretManager(string rootPrefix) : KeyVaultSecretManager
    {
        private readonly string _rootPrefix = rootPrefix + "--";

        public override bool Load(SecretProperties secret) =>
            base.Load(secret) && !secret.Name.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
