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
            [CommsAgentCommand.Instructions] = "Get or set instructions. Usage: /instructions <text>",
        }.ToFrozenDictionary();

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
}
