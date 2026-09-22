namespace CasCap.Models;

/// <summary>Maps application-facing channel names onto Signal group names.</summary>
/// <remarks>
/// Binds from <c>CasCap:ChannelConfig</c>, which arrives identically from <c>appsettings.json</c>,
/// a mounted file, a projected ConfigMap key or <c>CasCap__ChannelConfig__Channels__&lt;name&gt;</c>.
/// <para>
/// Group <b>names</b> are configured, never group ids: an id is an account-linked identifier and is
/// resolved at runtime, so nothing sensitive is committed.
/// </para>
/// </remarks>
public sealed record ChannelConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(ChannelConfig)}";

    /// <summary>Channel name to Signal group name. Channel names are matched case-insensitively.</summary>
    public Dictionary<string, string> Channels { get; init; } = [];
}
