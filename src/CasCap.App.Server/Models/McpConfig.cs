namespace CasCap.Models;

/// <summary>Controls optional message disclosure and sending through MCP.</summary>
public sealed record McpConfig
{
    /// <summary>Configuration section used for options binding.</summary>
    public static string ConfigurationSectionName => $"{nameof(CasCap)}:{nameof(McpConfig)}";

    /// <summary>Allows message text to reach the MCP client and its model provider.</summary>
    /// <remarks>Defaults to false. Enable only for authorized operators on a trusted network.</remarks>
    public bool MessageHistoryEnabled { get; init; }

    /// <summary>Allows MCP callers to send text to configured Signal groups.</summary>
    /// <remarks>Defaults to false. Enable only for authorized operators within a trusted access boundary.</remarks>
    public bool MessageSendingEnabled { get; init; }
}
