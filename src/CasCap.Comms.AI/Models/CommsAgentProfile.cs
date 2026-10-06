namespace CasCap.Models;

/// <summary>Identifies the remote runtime agent and caller-owned conversation session used by Comms.</summary>
/// <param name="AgentName">The stable tenant-local Agent Runtime name.</param>
/// <param name="SessionId">The stable opaque conversation session identifier.</param>
/// <param name="DefaultPrompt">The prompt used when an attachment arrives without text.</param>
public sealed record CommsAgentProfile(string AgentName, string SessionId, string DefaultPrompt);
