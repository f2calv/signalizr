namespace CasCap.Models;

/// <summary>Account-level policy the gateway applies on behalf of every channel.</summary>
/// <remarks>
/// Binds from <c>CasCap:GatewayConfig</c>, which arrives identically from <c>appsettings.json</c>,
/// a mounted file, a projected ConfigMap key or environment variables.
/// <para>
/// The profile is account state shared by every channel, so consumers cannot change it. Two
/// applications setting their own display name on one account would overwrite each other.
/// </para>
/// </remarks>
public sealed record GatewayConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(GatewayConfig)}";

    /// <summary>
    /// Display name applied to the account profile at startup, or <see langword="null"/> to leave
    /// the profile unchanged.
    /// </summary>
    [MaxLength(256)]
    public string? ProfileName { get; init; }

    /// <summary>
    /// Sends per channel within one minute above which the gateway warns of a possible flood, or
    /// <c>0</c> to disable the check.
    /// </summary>
    /// <remarks>
    /// Detection only: sends are never delayed or rejected. The warning goes to the log and to the
    /// operator notices, once per channel per minute.
    /// </remarks>
    [Range(0, 10_000)]
    public int SendRateWarningPerMinute { get; init; } = 30;

    /// <summary>How often a started typing indicator is re-sent while it is held.</summary>
    /// <remarks>Signal clients clear an indicator after about 15 seconds without a refresh.</remarks>
    [Range(1_000, 14_000)]
    public int TypingRefreshIntervalMs { get; init; } = 10_000;

    /// <summary>
    /// Longest a typing indicator is held without an explicit stop, after which the gateway clears it.
    /// </summary>
    /// <remarks>
    /// The lease is what makes cleanup deterministic: a consumer that crashes, is cancelled or
    /// disconnects between start and stop cannot leave the indicator showing indefinitely.
    /// </remarks>
    [Range(1_000, 3_600_000)]
    public int TypingMaxDurationMs { get; init; } = 180_000;
}
