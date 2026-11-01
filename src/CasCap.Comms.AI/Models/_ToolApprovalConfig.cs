using System.ComponentModel.DataAnnotations;

namespace CasCap.Models;

/// <summary>Human approval of high-impact MCP tool calls through a poll in <see cref="CommsConfig.GroupName"/>.</summary>
/// <remarks>Used by <see cref="CasCap.Services.ToolApprovalService"/> and <see cref="CasCap.Filters.ToolApprovalMcpFilter"/>.</remarks>
public sealed record ToolApprovalConfig : IAppConfig
{
    /// <inheritdoc/>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(ToolApprovalConfig)}";

    /// <summary>Whether gated tools wait for approval.</summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>. Disable only for local development: gated tools then run as soon
    /// as a model calls them.
    /// </remarks>
    public bool ApprovalEnabled { get; init; } = true;

    /// <summary>How long a gated call waits for an answer before it is denied, in milliseconds.</summary>
    /// <remarks>
    /// Defaults to 30 seconds. The calling agent waits for this long, so keep it well inside the caller's run
    /// timeout.
    /// </remarks>
    [Range(1_000, 240_000)]
    public int TimeoutMs { get; init; } = 30_000;

    /// <summary>Signal identifiers whose answers count, as delivered in the voter field of a poll vote.</summary>
    /// <remarks>
    /// Defaults to none, which accepts an answer from any member of <see cref="CommsConfig.GroupName"/>.
    /// Answers from anyone else are ignored rather than treated as a denial. These identifiers are personal
    /// data: keep them in private configuration only.
    /// </remarks>
    public HashSet<string> ApproverIds { get; init; } = [];

    /// <summary>Exposed tool names that need approval even without <see cref="CasCap.Attributes.RequiresApprovalAttribute"/>.</summary>
    public HashSet<string> RequiredTools { get; init; } = [];

    /// <summary>Exposed tool names that run without approval despite <see cref="CasCap.Attributes.RequiresApprovalAttribute"/>.</summary>
    public HashSet<string> WaivedTools { get; init; } = [];
}
