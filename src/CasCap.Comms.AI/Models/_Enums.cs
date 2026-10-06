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
}
