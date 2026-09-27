namespace CasCap.Models.Dtos;

/// <summary>A point-in-time count of active inbound subscriptions on this process.</summary>
public sealed record SignalizrMcpStatusResponse(
    [property: Description("Active application gRPC subscribers on this process; not Signal linked devices, saved cursors, or MCP sessions.")]
    int ConnectedClients,
    [property: Description("UTC time at which the live subscriber count was read.")]
    DateTimeOffset ObservedAtUtc);
