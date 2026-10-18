using System.Collections.Frozen;

namespace CasCap.Extensions;

/// <summary>Parses and documents the slash-command surface owned by the communications adapter.</summary>
public static class CommsAgentCommandParser
{
    private static readonly FrozenDictionary<string, CommsAgentCommand> Commands =
        new Dictionary<string, CommsAgentCommand>(StringComparer.OrdinalIgnoreCase)
        {
            ["/help"] = CommsAgentCommand.Help,
            ["/session info"] = CommsAgentCommand.SessionInfo,
            ["/session reset"] = CommsAgentCommand.SessionReset,
            ["/session bypass"] = CommsAgentCommand.SessionBypass,
            ["/session compact"] = CommsAgentCommand.SessionCompact,
            ["/session disable"] = CommsAgentCommand.SessionDisable,
            ["/session enable"] = CommsAgentCommand.SessionEnable,
            ["/session save"] = CommsAgentCommand.SessionSave,
            ["/session load"] = CommsAgentCommand.SessionLoad,
            ["/session delete"] = CommsAgentCommand.SessionDelete,
            ["/model"] = CommsAgentCommand.Model,
            ["/instructions"] = CommsAgentCommand.Instructions,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<CommsAgentCommand, string> Descriptions =
        new Dictionary<CommsAgentCommand, string>
        {
            [CommsAgentCommand.Help] = "List all available commands.",
            [CommsAgentCommand.SessionInfo] = "Show session size, message count, and state keys.",
            [CommsAgentCommand.SessionReset] = "Discard the active session.",
            [CommsAgentCommand.SessionBypass] = "Run a one-off prompt. Usage: /session bypass <prompt>",
            [CommsAgentCommand.SessionCompact] = "Keep the newest N messages. Usage: /session compact <count>",
            [CommsAgentCommand.SessionDisable] = "Disable session persistence.",
            [CommsAgentCommand.SessionEnable] = "Enable session persistence.",
            [CommsAgentCommand.SessionSave] = "Save a snapshot. Usage: /session save <name>",
            [CommsAgentCommand.SessionLoad] = "Load a snapshot. Usage: /session load <name>",
            [CommsAgentCommand.SessionDelete] = "Delete a snapshot. Usage: /session delete <name>",
            [CommsAgentCommand.Model] = "Get or set the model override. Usage: /model <modelName>",
            [CommsAgentCommand.Instructions] = "Show the effective instructions, or override them. Usage: /instructions [text]",
        }.ToFrozenDictionary();

    /// <summary>The longest single chat message a command reply is sent as, including its part header.</summary>
    public const int MaxReplyLength = 1800;

    /// <summary>Gets whether <paramref name="text"/> is addressed to the command surface rather than the agent.</summary>
    public static bool IsCommandLike(string text) => text.TrimStart().StartsWith('/');

    /// <summary>Attempts to parse one command and its trimmed argument.</summary>
    public static bool TryParse(string text, out CommsAgentCommand command, out string argument)
    {
        command = default;
        argument = string.Empty;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('/'))
            return false;

        foreach (var (prefix, candidate) in Commands.OrderByDescending(pair => pair.Key.Length))
        {
            if (!trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            command = candidate;
            argument = trimmed.Length > prefix.Length
                ? trimmed[(prefix.Length + 1)..].Trim()
                : string.Empty;
            return true;
        }
        return false;
    }

    /// <summary>Builds user-facing help from the owned command surface.</summary>
    public static string BuildHelpText() => string.Join(
        Environment.NewLine,
        Commands.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} — {Descriptions[pair.Value]}"));

    /// <summary>
    /// Splits a reply into messages of at most <paramref name="maxLength"/> characters, preferring line
    /// and then word boundaries, and numbers the parts when more than one is needed.
    /// </summary>
    public static IReadOnlyList<string> SplitReply(string text, int maxLength = MaxReplyLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        if (text.Length <= maxLength)
            return [text];

        // Reserve room for a "(nn/nn) " header so numbered parts never exceed the limit.
        const int headerReserve = 12;
        var chunkLength = maxLength - headerReserve;
        var parts = new List<string>();
        var remaining = text.AsSpan().Trim();
        while (remaining.Length > chunkLength)
        {
            var window = remaining[..chunkLength];
            var cut = window.LastIndexOf('\n');
            if (cut < chunkLength / 2)
                cut = window.LastIndexOf(' ');
            if (cut < chunkLength / 2)
                cut = chunkLength;
            parts.Add(remaining[..cut].TrimEnd().ToString());
            remaining = remaining[cut..].TrimStart();
        }
        if (!remaining.IsEmpty)
            parts.Add(remaining.ToString());

        return [.. parts.Select((part, index) => $"({index + 1}/{parts.Count}) {part}")];
    }
}
