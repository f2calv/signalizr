using ModelContextProtocol.Server;

namespace CasCap.Services;

/// <summary>Read-only MCP tools over the gateway's existing live state.</summary>
[McpServerToolType]
public sealed class SignalizrMcpQueryService(
    TimeProvider timeProvider,
    IGroupResolver groupResolver,
    IInboundSubscriberRegistry subscribers)
{
    /// <summary>Reads the live inbound subscriber count.</summary>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Count application clients currently connected to this Signalizr process via gRPC; not Signal linked devices or MCP sessions, and not upstream connection health.")]
    public SignalizrStatusResponse GetSignalizrStatus()
        => new(subscribers.Count, timeProvider.GetUtcNow());

    /// <summary>Lists group names from the same resolver used by REST.</summary>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List the exact Signal group names currently served by this Signalizr process, with original case and spaces.")]
    public SignalizrGroupsResponse GetSignalizrGroups()
        => new(groupResolver.GroupNames.Order(StringComparer.Ordinal).ToArray());
}
