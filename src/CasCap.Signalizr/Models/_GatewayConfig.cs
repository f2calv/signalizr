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
}
