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

        if (!string.IsNullOrWhiteSpace(appConfig.AgentRuntimeKeyVaultPrefix) || appConfig.KeyVaultSecretPrefixes.Length > 0)
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

    private static void AddKeyVaultSecrets(this IHostApplicationBuilder builder, AppConfig appConfig)
    {
        var azureAuthConfig = builder.Configuration
            .GetSection(AzureAuthConfig.ConfigurationSectionName)
            .Get<AzureAuthConfig>();
        if (string.IsNullOrWhiteSpace(azureAuthConfig?.KeyVaultName) || !azureAuthConfig.IsKeyVaultEnabled)
            throw new InvalidOperationException(
                "AppConfig Key Vault secret prefixes require AppConfig:KeyVaultName.");
        var credential = azureAuthConfig.TokenCredential
            ?? throw new InvalidOperationException(
                "AppConfig Key Vault secret prefixes require an Azure Entra credential in AppConfig.");

        // Each source loads one prefix with no exclusive root, so a shared vault's other secrets never enter.
        foreach (var prefix in appConfig.KeyVaultSecretPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix)))
            builder.Configuration.AddKeyVaultConfiguration(
                azureAuthConfig.KeyVaultUri,
                credential,
                new PrefixKeyVaultSecretManager(prefix, prefix.TrimEnd('-').Replace("--", ConfigurationPath.KeyDelimiter)));

        if (!string.IsNullOrWhiteSpace(appConfig.AgentRuntimeKeyVaultPrefix))
            builder.Configuration.AddKeyVaultConfiguration(
                azureAuthConfig.KeyVaultUri,
                credential,
                new PrefixKeyVaultSecretManager(
                    appConfig.AgentRuntimeKeyVaultPrefix,
                    AgentRuntimeAzureAuthConfig.ConfigurationSectionName));
    }
}
