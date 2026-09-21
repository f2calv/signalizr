namespace CasCap.Models;

/// <summary>Strongly-typed configuration for the comma-separated feature-flag string.</summary>
/// <remarks>
/// Binds from <c>CasCap:FeatureConfig:EnabledFeatures</c>, which arrives identically from
/// <c>appsettings.json</c>, a mounted file, a projected ConfigMap key or the
/// <c>CasCap__FeatureConfig__EnabledFeatures</c> environment variable.
/// </remarks>
public sealed record FeatureConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(FeatureConfig)}";

    /// <summary>Comma-separated list of enabled feature names.</summary>
    [Required]
    public required string EnabledFeatures { get; init; }

    /// <summary>Parses <see cref="EnabledFeatures"/> into a case-insensitive set of feature names.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="EnabledFeatures"/> is empty or contains a value not present in
    /// <see cref="FeatureNames.ValidNames"/>.
    /// </exception>
    public HashSet<string> GetEnabledFeatures()
    {
        var features = EnabledFeatures
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (features.Count == 0)
            throw new InvalidOperationException(
                $"No features are enabled. Valid names: {string.Join(", ", FeatureNames.ValidNames)}.");

        var unknown = features.Where(f => !FeatureNames.ValidNames.Contains(f)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"Unrecognised feature name(s): {string.Join(", ", unknown)}. " +
                $"Valid names: {string.Join(", ", FeatureNames.ValidNames)}.");

        return features;
    }
}
