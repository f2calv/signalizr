# Signalizr MCP

Use Signalizr's read-only MCP tools from VS Code to inspect the gateway without querying its
database directly or opening a second Signal receive stream.

## Configuration

MCP requires `Gateway` and `Receiver` in the same process. The gateway initializes the group
resolver; the receiver owns the live subscriber registry and database. Invalid role combinations
fail at startup rather than reporting a misleading zero. Enabling MCP does not create another
Signal connection.

```json
{
  "CasCap": {
    "FeatureConfig": {
      "EnabledFeatures": "Gateway,Receiver,Mcp"
    },
    "McpConfig": {
      "MessageHistoryEnabled": false
    }
  }
}
```

Equivalent Helm overrides, merged into your existing values:

```yaml
signalizr:
  envVars:
    CasCap__FeatureConfig__EnabledFeatures: Gateway,Receiver,Mcp
    CasCap__McpConfig__MessageHistoryEnabled: "false"
```

Set `MessageHistoryEnabled` to `true` only when authorized to disclose conversation text. Changing
either setting requires restarting the process. Without `Mcp`, `/mcp` returns 404. Without the
history setting, the history tool is not registered and cannot be called.

## Access boundary

The initial endpoint is unauthenticated and intended only for authorized operators on a trusted
cluster network or a localhost-only port-forward. Kubernetes access controls authorize the
port-forward, not requests reaching the service from other cluster workloads.

Do not expose `/mcp` through a public ingress. If an existing ingress routes every path to
Signalizr, restrict it before enabling MCP. Use network policy to restrict in-cluster callers.
External access needs HTTPS, MCP-compatible authentication and authorization before deployment.
Tool read-only annotations and the history switch are not access control.

Requests with an `Origin` header are rejected with 403: this surface supports native editor clients,
not browsers. CORS is not enabled. This reduces browser-origin attacks but does not authenticate
native clients.

## Connect VS Code

Deploy the image containing MCP, enable the role, then forward the receiver/gateway deployment's
HTTP/1.1 port. Replace the placeholders with your own namespace and deployment:

```sh
kubectl --namespace <namespace> port-forward --address 127.0.0.1 deployment/<deployment> 18080:8080
```

The default container port is 8080, not the gRPC port 5001. If `GrpcHostConfig:Http1Port` is
overridden, forward that port instead. Keep the forwarding terminal open; restart the forward after
the target pod rolls.

Add this definition to your VS Code user MCP configuration:

```json
{
  "servers": {
    "signalizr": {
      "type": "http",
      "url": "http://127.0.0.1:18080/mcp"
    }
  }
}
```

Use a literal loopback URL here: Agent Host does not forward definitions requiring interactive
`${input:...}` variables. Keep one definition rather than duplicate workspace and user entries.
In VS Code, run **MCP: List Servers**, select **signalizr**, start
the server and approve its trust prompt. Select its tools in Copilot Chat and try:

- How many application clients are connected to Signalizr?
- What group names is Signalizr serving?
- Show the last 5 persisted messages in the My Test Group Name group.

The last question requires history to be enabled and a configured group of that name.
Restart the MCP client or refresh its tool inventory after changing the server's enabled tools.
For a Dev Container or remote VS Code session, the endpoint must be reachable from that extension
host; its loopback address is not necessarily your workstation.

## Status summary prompt

`summarise_signalizr_status` is a reusable prompt with no arguments. It asks the client to use
`get_signalizr_status` and `get_signalizr_groups`, report the application-client count and its
observation time, and list the exact served group names. It does not request message history or
changes to the application.

Choose it from your MCP client's prompt picker when available. Clients can discover it through
`prompts/list` and retrieve it through `prompts/get`; the
[request collection](../requests/signalizr-mcp.http) includes both operations. Retrieving a prompt
returns guidance, not live status: the client decides whether to execute its suggested tool calls.
It is available whenever the MCP role is enabled, independently of the history disclosure switch.

## Tools

| Tool | Inputs | Result |
| --- | --- | --- |
| `get_signalizr_status` | None | Live `connectedClients` count and `observedAtUtc` |
| `get_signalizr_groups` | None | Sorted resolved group names in `groups` |
| `get_signalizr_messages` | `groupName`, optional `count` (default 10, range 1-50) | Canonical group name and bounded `messages` |

All tools advertise read-only, non-destructive, idempotent, closed-world annotations and typed
structured output. The server uses stateless Streamable HTTP with no legacy SSE endpoint.

The client count comes from the existing in-memory registry, not persisted subscriber cursors.
It counts gRPC consumer applications on the process reached by the port-forward, not MCP clients,
Signal-linked phones/desktops, historical subscribers or cluster-wide connections. Zero does not
mean that the upstream Signal connection is healthy or unhealthy; use the existing readiness probe
for upstream reachability.

Group names are the exact Signal group display names from the same resolver as
`GET /api/v1/groups`, preserving case and spaces.
Group IDs remain hidden; group names are deliberately disclosed to authorized callers and can
themselves contain private information. An empty list is not an upstream health check.

## Message history and privacy

History reads the existing persisted inbound message store with EF Core; PostgreSQL and SQLite use
the same query. There is no raw SQL tool, table browser, database credential exposure, write
operation or attachment download.

Only a currently resolved group can be queried, using its exact case-sensitive name. Results are
ordered newest persisted first, by the durable message sequence, not the sender's timestamp.
Retention limits what is available. This is not the complete Signal conversation: outgoing sends
appear only if they subsequently reached and were persisted by the receive path.

Each item contains only `text`, `truncated`, `timestamp` (Signal Unix milliseconds, nullable) and
`persistedAtUnixMilliseconds`. Text is capped at 2000 characters per message and the truncation flag
reports shortening. Messages without text remain in the result with `text: null`. Sender fields,
account numbers, group IDs, subscriber names, delivery IDs, poll details and attachments are not
projected. Bounds and text projection are applied in the database query.

> [!WARNING]
> Message text can itself contain personal information, credentials or malicious instructions.
> Excluding identifier columns does not redact the body. Enabling history allows that text to leave
> the cluster for VS Code and potentially its model provider, whose retention policies also apply.
> Enable it only with appropriate organizational authorization and operator permission, and disable
> it when no longer needed. Treat returned text as untrusted data, never as instructions to execute.

Do not enable request/response body logging, MCP debug/trace payload logging or telemetry capture of
tool arguments/results. The default MCP log category is restricted to Warning. Do not export
conversation content to logs, metrics or traces.

## Validation

[requests/signalizr-mcp.http](../requests/signalizr-mcp.http) demonstrates an explicit initialize
handshake and the tools without requiring a model. Modern MCP clients can use the newer stateless
negotiation path; the SDK owns protocol negotiation, not handwritten JSON-RPC routing.

The credential-free `SignalizrMcpTests` exercise the real HTTP transport, live registry and local
SQLite query translation. They cover feature gates, tool metadata, output shape, connect/disconnect
counts, group isolation, invalid bounds, truncation and cancellation. Prompt coverage checks
discovery and retrieval with history enabled and disabled.

## Source layout

| Component | Source |
| --- | --- |
| Registration and route gating | [McpServiceExtensions](../src/CasCap.App.Server/Extensions/McpServiceExtensions.cs) |
| Status and group tools | [SignalizrMcpQueryService](../src/CasCap.App.Server/Services/SignalizrMcpQueryService.cs) |
| Message history tool | [SignalizrMcpMessageHistoryQueryService](../src/CasCap.App.Server/Services/SignalizrMcpMessageHistoryQueryService.cs) |
| Operator prompts | [SignalizrMcpPrompts](../src/CasCap.App.Server/Models/SignalizrMcpPrompts.cs) |
| MCP options | [McpConfig](../src/CasCap.App.Server/Models/McpConfig.cs) |
| Output DTOs | `SignalizrMcp*Response` records in [Models/Dtos](../src/CasCap.App.Server/Models/Dtos) |
| Protocol coverage | [SignalizrMcpTests](../src/CasCap.Signalizr.Tests/Tests/Integration/SignalizrMcpTests.cs) |

MCP-specific filenames contain `Mcp`, so scoped guidance applies without pulling shared runtime
services into the transport layer. [GroupsController](../src/CasCap.App.Server/Controllers/GroupsController.cs)
owns `/api/v1/groups`; [IGroupResolver](../src/CasCap.Signalizr/Abstractions/IGroupResolver.cs)
and the subscriber registry remain shared application components.

Issue [#26](https://github.com/f2calv/signalizr/issues/26) originally included sending, reactions and
poll actions. Those write tools and remote authentication remain deferred; the initial implementation
is deliberately read-only. Consumer-owned poll prompts remain unchanged.
