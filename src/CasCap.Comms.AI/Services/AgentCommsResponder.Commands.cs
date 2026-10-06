namespace CasCap.Services;

public sealed partial class AgentCommsResponder
{
    private async Task<string?> HandleCommandAsync(
        CommsAgentCommand command,
        string argument,
        CancellationToken cancellationToken) => command switch
        {
            CommsAgentCommand.Help => CommsAgentCommandParser.BuildHelpText(),
            CommsAgentCommand.SessionInfo => await GetSessionInfoAsync(cancellationToken),
            CommsAgentCommand.SessionReset => await ResetSessionAsync(cancellationToken),
            CommsAgentCommand.SessionBypass => "Usage: /session bypass <prompt>",
            CommsAgentCommand.SessionCompact => await CompactSessionAsync(argument, cancellationToken),
            CommsAgentCommand.SessionDisable => await SetSessionEnabledAsync(false, cancellationToken),
            CommsAgentCommand.SessionEnable => await SetSessionEnabledAsync(true, cancellationToken),
            CommsAgentCommand.SessionSave => await SaveSnapshotAsync(argument, cancellationToken),
            CommsAgentCommand.SessionLoad => await LoadSnapshotAsync(argument, cancellationToken),
            CommsAgentCommand.SessionDelete => await DeleteSnapshotAsync(argument, cancellationToken),
            CommsAgentCommand.Model => await GetOrSetModelAsync(argument, cancellationToken),
            CommsAgentCommand.Instructions => await GetOrSetInstructionsAsync(argument, cancellationToken),
            _ => null,
        };

    private async Task<string> GetSessionInfoAsync(CancellationToken cancellationToken)
    {
        var session = await _agentRuntimeClient.GetSessionAsync(
            _profile.AgentName,
            _profile.SessionId,
            cancellationToken);
        if (session is null)
            return "Agent is unavailable.";
        if (!session.Exists)
            return "No active session.";

        var lines = new StringBuilder();
        lines.AppendLine($"Session active (persistence: {(session.SessionEnabled ? "on" : "off")}). Size: {session.SizeBytes:N0} bytes.");
        foreach (var entry in session.Entries)
        {
            var detail = entry.MessageCount > 0
                ? $", {entry.MessageCount} messages ({entry.UserMessageCount}u/{entry.AssistantMessageCount}a)"
                : string.Empty;
            lines.AppendLine($"  [{entry.Key}] {entry.ByteSize:N0} bytes{detail}");
        }
        return lines.ToString().TrimEnd();
    }

    private async Task<string> ResetSessionAsync(CancellationToken cancellationToken) =>
        await _agentRuntimeClient.ResetSessionAsync(_profile.AgentName, _profile.SessionId, cancellationToken)
            ? "Session reset. The next message will start a fresh conversation."
            : "Agent is unavailable.";

    private async Task<string> CompactSessionAsync(string argument, CancellationToken cancellationToken)
    {
        if (!int.TryParse(argument, out var retainMessageCount) || retainMessageCount <= 0)
            return "Usage: /session compact <count> (positive integer)";
        var result = await _agentRuntimeClient.CompactSessionAsync(
            _profile.AgentName,
            _profile.SessionId,
            new CompactAgentSessionRequest { RetainMessageCount = retainMessageCount },
            cancellationToken);
        if (result is null)
            return "Agent is unavailable.";
        if (!result.SessionExists)
            return "No active session to compact.";
        if (!result.HistoryAvailable)
            return "No in-memory chat history found in the current session.";
        return result.RemovedMessageCount > 0
            ? $"Session compacted: removed {result.RemovedMessageCount} message(s), {retainMessageCount} retained."
            : $"Session already has {retainMessageCount} or fewer messages — nothing to compact.";
    }

    private async Task<string> SetSessionEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var updated = await UpdateOverridesAsync(
            current => new UpdateAgentOverridesRequest
            {
                SessionEnabled = enabled,
                ModelName = current.ModelName,
                Instructions = current.Instructions,
            },
            cancellationToken);
        if (updated is null)
            return "Agent is unavailable.";
        return enabled
            ? "Session persistence enabled."
            : "Session persistence disabled. Each message will start a fresh conversation.";
    }

    private async Task<string> SaveSnapshotAsync(string snapshotName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotName))
            return "Usage: /session save <name>";
        return await _agentRuntimeClient.SaveSessionSnapshotAsync(
            _profile.AgentName,
            _profile.SessionId,
            snapshotName,
            cancellationToken)
            ? $"Session saved as \"{snapshotName}\"."
            : "No active session to save.";
    }

    private async Task<string> LoadSnapshotAsync(string snapshotName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotName))
            return "Usage: /session load <name>";
        return await _agentRuntimeClient.LoadSessionSnapshotAsync(
            _profile.AgentName,
            _profile.SessionId,
            snapshotName,
            cancellationToken)
            ? $"Snapshot \"{snapshotName}\" loaded into the active session."
            : $"No snapshot named \"{snapshotName}\" found.";
    }

    private async Task<string> DeleteSnapshotAsync(string snapshotName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotName))
            return "Usage: /session delete <name>";
        return await _agentRuntimeClient.DeleteSessionSnapshotAsync(
            _profile.AgentName,
            _profile.SessionId,
            snapshotName,
            cancellationToken)
            ? $"Snapshot \"{snapshotName}\" deleted."
            : $"No snapshot named \"{snapshotName}\" found.";
    }

    private async Task<string> GetOrSetModelAsync(string modelName, CancellationToken cancellationToken)
    {
        var current = await GetOverridesAsync(cancellationToken);
        if (current is null)
            return "Agent is unavailable.";
        if (string.IsNullOrWhiteSpace(modelName))
            return $"Current model override: {current.ModelName ?? "(none — using provider default)"}";
        var updated = await SetOverridesAsync(
            new UpdateAgentOverridesRequest
            {
                SessionEnabled = current.SessionEnabled,
                ModelName = modelName,
                Instructions = current.Instructions,
            },
            cancellationToken);
        return updated is null ? "Agent is unavailable." : $"Model overridden to: {updated.ModelName}";
    }

    private async Task<string> GetOrSetInstructionsAsync(string instructions, CancellationToken cancellationToken)
    {
        var current = await GetOverridesAsync(cancellationToken);
        if (current is null)
            return "Agent is unavailable.";
        if (string.IsNullOrWhiteSpace(instructions))
        {
            if (current.Instructions is null)
                return "Current instructions override: (none — using configured default)";
            var preview = current.Instructions.Length > 200
                ? current.Instructions[..200] + "..."
                : current.Instructions;
            return $"Current instructions override ({current.Instructions.Length} chars): {preview}";
        }

        var updated = await SetOverridesAsync(
            new UpdateAgentOverridesRequest
            {
                SessionEnabled = current.SessionEnabled,
                ModelName = current.ModelName,
                Instructions = instructions,
            },
            cancellationToken);
        return updated is null
            ? "Agent is unavailable."
            : $"Instructions overridden ({updated.Instructions?.Length ?? 0} chars).";
    }

    private async Task<AgentOverridesResponse?> UpdateOverridesAsync(
        Func<AgentOverridesResponse, UpdateAgentOverridesRequest> update,
        CancellationToken cancellationToken)
    {
        var current = await GetOverridesAsync(cancellationToken);
        return current is null ? null : await SetOverridesAsync(update(current), cancellationToken);
    }

    private Task<AgentOverridesResponse?> GetOverridesAsync(CancellationToken cancellationToken) =>
        _agentRuntimeClient.GetOverridesAsync(_profile.AgentName, _profile.SessionId, cancellationToken);

    private Task<AgentOverridesResponse?> SetOverridesAsync(
        UpdateAgentOverridesRequest request,
        CancellationToken cancellationToken) =>
        _agentRuntimeClient.SetOverridesAsync(_profile.AgentName, _profile.SessionId, request, cancellationToken);
}
