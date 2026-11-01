using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace CasCap.Filters;

/// <summary>
/// MCP call-tool filter that holds a gated tool until a person approves it through <see cref="ToolApprovalService"/>.
/// </summary>
/// <remarks>
/// A tool is gated when it carries <see cref="RequiresApprovalAttribute"/> or is listed in
/// <see cref="ToolApprovalConfig.RequiredTools"/>, unless it is listed in <see cref="ToolApprovalConfig.WaivedTools"/>.
/// The filter fails closed: a host without <see cref="ToolApprovalService"/>, such as one without the Comms
/// feature, never runs a gated tool. Anything but approval returns a refusal the model can relay, not an error.
/// </remarks>
internal static class ToolApprovalMcpFilter
{
    private const string UnknownRequester = "an unidentified client";

    /// <summary>Runs <paramref name="next"/> only when the matched tool is ungated or approved.</summary>
    internal static async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        if (context.MatchedPrimitive is not McpServerTool tool || context.Services is not { } services)
            return await next(context, cancellationToken).ConfigureAwait(false);

        var config = services.GetRequiredService<IOptions<ToolApprovalConfig>>().Value;
        if (!RequiresApproval(tool, config))
            return await next(context, cancellationToken).ConfigureAwait(false);

        var toolName = tool.ProtocolTool.Name;
        var approvalSvc = services.GetService<ToolApprovalService>();
        var outcome = approvalSvc is null
            ? ToolApprovalOutcome.Unavailable
            : await approvalSvc.RequestApprovalAsync(
                toolName,
                GetRequester(context),
                SerializeArguments(context.Params),
                cancellationToken).ConfigureAwait(false);

        return outcome is ToolApprovalOutcome.Approved
            ? await next(context, cancellationToken).ConfigureAwait(false)
            : CreateRefusal(toolName, outcome, config.TimeoutMs);
    }

    /// <summary>Whether a call to <paramref name="tool"/> must be approved under <paramref name="config"/>.</summary>
    internal static bool RequiresApproval(McpServerTool tool, ToolApprovalConfig config)
    {
        if (!config.ApprovalEnabled)
            return false;

        var toolName = tool.ProtocolTool.Name;
        if (config.WaivedTools.Contains(toolName))
            return false;
        return config.RequiredTools.Contains(toolName) || tool.Metadata.OfType<RequiresApprovalAttribute>().Any();
    }

    /// <summary>Names the requesting agent from the call metadata, falling back to the MCP client name.</summary>
    internal static string GetRequester(RequestContext<CallToolRequestParams> context) =>
        ResolveRequester(context.Params?.Meta, context.Server.ClientInfo?.Name);

    /// <summary>Prefers <see cref="McpToolCallMetaKeys.AgentName"/>, then the client name, then a placeholder.</summary>
    internal static string ResolveRequester(JsonObject? meta, string? clientName)
    {
        if (meta?[McpToolCallMetaKeys.AgentName] is { } agentNode
            && agentNode.GetValueKind() is JsonValueKind.String
            && agentNode.GetValue<string>() is { Length: > 0 } agentName)
            return agentName;

        return clientName is { Length: > 0 } ? clientName : UnknownRequester;
    }

    internal static CallToolResult CreateRefusal(string toolName, ToolApprovalOutcome outcome, int timeoutMs) =>
        new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = outcome switch
                    {
                        ToolApprovalOutcome.Denied =>
                            $"{toolName} did not run: a person in the household chat denied it. Tell the user it was refused and do not retry.",
                        ToolApprovalOutcome.TimedOut =>
                            $"{toolName} did not run: nobody approved it within {timeoutMs / 1000} seconds. Do not retry unless the user asks again.",
                        _ =>
                            $"{toolName} did not run: it needs human approval, which this server cannot request. Do not retry.",
                    },
                },
            ],
        };

    private static string? SerializeArguments(CallToolRequestParams? parameters) =>
        parameters?.Arguments is { Count: > 0 } arguments ? JsonSerializer.Serialize(arguments) : null;
}
