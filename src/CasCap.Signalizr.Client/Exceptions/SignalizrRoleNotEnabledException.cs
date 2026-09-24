namespace CasCap.Signalizr.Client;

/// <summary>Thrown when a gateway does not run the role needed to serve a request.</summary>
/// <remarks>
/// Distinct from an unreachable gateway. One image serves several roles, and a role that is off
/// does not route its paths at all, so the surface returns 404 rather than failing to start. A
/// caller can often continue: the send and subscribe surfaces belong to different roles and may
/// live on different deployments.
/// </remarks>
public sealed class SignalizrRoleNotEnabledException(string message) : Exception(message);
