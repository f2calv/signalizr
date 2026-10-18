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

        if (!string.IsNullOrWhiteSpace(appConfig.AgentRuntimeKeyVaultPrefix))
            builder.AddAgentRuntimeKeyVaultSecrets(appConfig.AgentRuntimeKeyVaultPrefix);

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

    private static void AddAgentRuntimeKeyVaultSecrets(this IHostApplicationBuilder builder, string prefix)
    {
        var azureAuthConfig = builder.Configuration
            .GetSection(AzureAuthConfig.ConfigurationSectionName)
            .Get<AzureAuthConfig>();
        if (string.IsNullOrWhiteSpace(azureAuthConfig?.KeyVaultName) || !azureAuthConfig.IsKeyVaultEnabled)
            throw new InvalidOperationException(
                "AppConfig:AgentRuntimeKeyVaultPrefix requires AppConfig:KeyVaultName.");
        var credential = azureAuthConfig.TokenCredential
            ?? throw new InvalidOperationException(
                "AppConfig:AgentRuntimeKeyVaultPrefix requires an Azure Entra credential in AppConfig.");

        // No exclusive root: a shared vault's other secrets never enter this host's configuration.
        builder.Configuration.AddKeyVaultConfiguration(
            azureAuthConfig.KeyVaultUri,
            credential,
            new PrefixKeyVaultSecretManager(prefix, AgentRuntimeAzureAuthConfig.ConfigurationSectionName));
    }
}
