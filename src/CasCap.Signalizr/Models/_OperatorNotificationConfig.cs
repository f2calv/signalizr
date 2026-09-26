namespace CasCap.Models;

/// <summary>Controls the operational notices the gateway posts to the account's "Note to Self".</summary>
/// <remarks>
/// Binds from <c>CasCap:OperatorNotificationConfig</c>, which arrives identically from
/// <c>appsettings.json</c>, a mounted file, a projected ConfigMap key or environment variables.
/// <para>
/// Off by default: on an account linked to a person's phone, "Note to Self" is that person's own
/// conversation, so the notices are only appropriate once the operator opts in.
/// </para>
/// </remarks>
public sealed record OperatorNotificationConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(OperatorNotificationConfig)}";

    /// <summary>Whether operational notices are sent.</summary>
    public bool NotificationsEnabled { get; init; }

    /// <summary>How many unsent notices are held before the oldest is discarded.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;

    /// <summary>Minimum interval between two inbound-queue throttling notices.</summary>
    /// <remarks>Each notice reports every drop since the previous one, so none goes uncounted.</remarks>
    [Range(1_000, 3_600_000)]
    public int ThrottleNoticeIntervalMs { get; init; } = 60_000;
}
