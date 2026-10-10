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
    public static (AppConfig appConfig, HashSet<string> enabledFeatures, GitMetadata gitMetadata) InitializeConfiguration(
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

        var featureConfig = builder.Configuration
            .GetSection(FeatureConfig.ConfigurationSectionName)
            .Get<FeatureConfig>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{FeatureConfig.ConfigurationSectionName}' is missing.");
        builder.Services.AddOptionsWithValidateOnStart<FeatureConfig>()
            .BindConfiguration(FeatureConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        var gitMetadata = new GitMetadata();
        builder.Services.AddSingleton(gitMetadata);

        return (appConfig, featureConfig.GetEnabledFeatures(), gitMetadata);
    }
}
