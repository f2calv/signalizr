namespace CasCap.Constants;

/// <summary>
/// Well-known feature name constants for <see cref="CasCap.Common.Abstractions.IBgFeature.FeatureName"/>
/// and <see cref="CasCap.Models.FeatureConfig.EnabledFeatures"/>.
/// </summary>
/// <remarks>
/// A single container image serves every role; the enabled set selects which services are
/// registered and started. Use these constants instead of string literals so that a feature
/// rename is a single point of change.
/// </remarks>
public static class FeatureNames
{
    /// <summary>One-shot EF Core database migration role.</summary>
    public const string DbMigrator = nameof(DbMigrator);

    /// <summary>Gateway surface — REST send, gRPC subscription, and named-channel resolution.</summary>
    public const string Gateway = nameof(Gateway);

    /// <summary>Owns the single inbound receive stream and fans messages out to subscribers.</summary>
    public const string Receiver = nameof(Receiver);

    /// <summary>Sample thin client used to evaluate a running gateway.</summary>
    public const string DemoClient = nameof(DemoClient);

    /// <summary>Model Context Protocol surface.</summary>
    public const string Mcp = nameof(Mcp);

    /// <summary>
    /// Case-insensitive set of all valid feature names derived from the <c>const string</c> fields
    /// on this class, so a new feature needs no separate registration.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidNames =
        typeof(FeatureNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
