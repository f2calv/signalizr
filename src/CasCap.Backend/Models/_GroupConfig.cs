namespace CasCap.Models;

/// <summary>Selects the Signal groups served by their exact display names.</summary>
/// <remarks>
/// Binds from <c>CasCap:GroupConfig</c>, which arrives identically from <c>appsettings.json</c>,
/// a mounted file, a projected ConfigMap key or <c>CasCap__GroupConfig__GroupNames__0</c>.
/// <para>
/// Group <b>names</b> are configured, never group ids: an id is an account-linked identifier and is
/// resolved at runtime, so nothing sensitive is committed.
/// </para>
/// </remarks>
public sealed record GroupConfig
{
    /// <summary>Configuration section name used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(GroupConfig)}";

    /// <summary>Exact Signal group names to serve, including case and spaces.</summary>
    public string[] GroupNames { get; init; } = [];
}
