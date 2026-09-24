using System.ComponentModel.DataAnnotations;

namespace CasCap.Data;

/// <summary>Database configuration for durable inbound delivery.</summary>
public sealed record DatabaseConfig : IAppConfig, IValidatableObject
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(DatabaseConfig)}";

    /// <summary>Database provider used for inbound messages and subscriber cursors.</summary>
    /// <remarks>Defaults to <see cref="DatabaseProvider.Sqlite"/>.</remarks>
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    /// <summary>Provider-specific connection string.</summary>
    /// <remarks>
    /// Required for <see cref="DatabaseProvider.Sqlite"/> and <see cref="DatabaseProvider.Postgres"/>.
    /// </remarks>
    public string? ConnectionString { get; init; }

    /// <summary>Whether pending EF Core migrations are applied during startup.</summary>
    /// <remarks>Defaults to <see langword="true"/> for the single-replica receiver deployment.</remarks>
    public bool MigrateOnStartup { get; init; } = true;

    /// <summary>Whether startup must reject the non-durable InMemory provider.</summary>
    /// <remarks>Defaults to <see langword="true"/>. Tests explicitly disable this guard.</remarks>
    public bool DurabilityRequired { get; init; } = true;

    /// <summary>Hours to retain messages after every registered subscriber has acknowledged them.</summary>
    /// <remarks>Unacknowledged messages never expire automatically.</remarks>
    [Range(1, 8_760)]
    public int AcknowledgedMessageRetentionHours { get; init; } = 24;

    /// <summary>Maximum days to retain message content, including unacknowledged messages.</summary>
    /// <remarks>Defaults to 30 days and bounds the at-least-once replay window.</remarks>
    [Range(1, 365)]
    public int MessageRetentionDays { get; init; } = 30;

    /// <summary>Milliseconds between acknowledged-message pruning passes.</summary>
    [Range(1_000, 86_400_000)]
    public int PruneIntervalMs { get; init; } = 300_000;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DurabilityRequired && Provider is DatabaseProvider.InMemory)
        {
            yield return new ValidationResult(
                $"{nameof(DatabaseProvider.InMemory)} cannot be used when {nameof(DurabilityRequired)} is true.",
                [nameof(Provider), nameof(DurabilityRequired)]);
        }

        if (Provider is not DatabaseProvider.InMemory && string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield return new ValidationResult(
                $"{nameof(ConnectionString)} is required when {nameof(Provider)} is {Provider}.",
                [nameof(ConnectionString)]);
        }
    }
}
