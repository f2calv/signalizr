namespace CasCap.Models;

/// <summary>Slash commands owned by the communications agent adapter.</summary>
public enum CommsAgentCommand
{
    /// <summary>Lists available commands.</summary>
    Help,
    /// <summary>Displays active session status.</summary>
    SessionInfo,
    /// <summary>Resets active session state.</summary>
    SessionReset,
    /// <summary>Runs one prompt without session state.</summary>
    SessionBypass,
    /// <summary>Compacts active session history.</summary>
    SessionCompact,
    /// <summary>Disables session persistence.</summary>
    SessionDisable,
    /// <summary>Enables session persistence.</summary>
    SessionEnable,
    /// <summary>Saves a named session snapshot.</summary>
    SessionSave,
    /// <summary>Loads a named session snapshot.</summary>
    SessionLoad,
    /// <summary>Deletes a named session snapshot.</summary>
    SessionDelete,
    /// <summary>Gets or sets the runtime model override.</summary>
    Model,
    /// <summary>Gets or sets the runtime instruction override.</summary>
    Instructions,
    /// <summary>Clears the runtime instruction override.</summary>
    InstructionsReset,
}

/// <summary>Outcome of an Agent Runtime run requested by the communications responder.</summary>
public enum CommsAgentRunOutcome
{
    /// <summary>The run returned an answer.</summary>
    Succeeded,
    /// <summary>The run returned an answer, but at least one tool failed; the footer names them.</summary>
    CompletedWithToolErrors,
    /// <summary>The run completed without answer text; nothing is sent.</summary>
    EmptyOutput,
    /// <summary>The response stream ended without a final response.</summary>
    NoResponse,
    /// <summary>The runtime could not be reached.</summary>
    Unreachable,
    /// <summary>The runtime returned an unsuccessful HTTP status.</summary>
    HttpError,
    /// <summary>The runtime request timed out.</summary>
    Timeout,
    /// <summary>Any other failure, such as a malformed response stream.</summary>
    Error,
}

/// <summary>Result of asking the chat group to approve a tool call.</summary>
public enum ToolApprovalOutcome
{
    /// <summary>An accepted approver chose to run the tool.</summary>
    Approved,
    /// <summary>An accepted approver chose not to run the tool.</summary>
    Denied,
    /// <summary>No accepted answer arrived in time, so the tool did not run.</summary>
    TimedOut,
    /// <summary>The approval prompt could not be shown, so the tool did not run.</summary>
    Unavailable,
}
