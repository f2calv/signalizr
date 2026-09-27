# Signalizr MCP

Use Signalizr's MCP tools from VS Code to inspect the gateway and, when explicitly enabled,
send text through its existing message gateway without opening a second Signal receive stream.

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
      "MessageHistoryEnabled": false,
      "MessageSendingEnabled": false
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
    CasCap__McpConfig__MessageSendingEnabled: "false"
```

Set `MessageHistoryEnabled` to `true` only when authorized to disclose conversation text.
Set `MessageSendingEnabled` to `true` only when authorized operators may send text to configured
groups. These switches are independent and restart-required. Without `Mcp`, `/mcp` returns 404;
each optional tool is absent and uncallable unless its own switch is enabled.

## Access boundary

The initial endpoint is unauthenticated and intended only for authorized operators on a trusted
cluster network or a localhost-only port-forward. Kubernetes access controls authorize the
port-forward, not requests reaching the service from other cluster workloads.

Do not expose `/mcp` through a public ingress. If an existing ingress routes every path to
Signalizr, restrict it before enabling MCP. Use network policy to restrict in-cluster callers.
External access needs HTTPS, MCP-compatible authentication and authorization before deployment.
Tool annotations and feature switches are not access control. When sending is enabled, every
caller that can reach this unauthenticated endpoint can invoke it. Do not enable sending on a
shared network unless that access is acceptable and appropriately restricted.

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
| `send_signalizr_message` | `groupName`, `message` | Upstream acknowledgement `timestamp`; requires sending opt-in |

Query tools advertise read-only, non-destructive, idempotent, closed-world annotations. Sending
is explicitly non-read-only, non-idempotent and open-world; it adds a message rather than deleting
or overwriting one. All tools return typed structured output. The server uses stateless Streamable
HTTP with no legacy SSE endpoint.

The client count comes from the existing in-memory registry, not persisted subscriber cursors.
It counts gRPC consumer applications on the process reached by the port-forward, not MCP clients,
Signal-linked phones/desktops, historical subscribers or cluster-wide connections. Zero does not
mean that the upstream Signal connection is healthy or unhealthy; use the existing readiness probe
for upstream reachability.

Group names are the exact Signal group display names from the same resolver as
`GET /api/v1/groups`, preserving case and spaces.
Group IDs remain hidden; group names are deliberately disclosed to authorized callers and can
themselves contain private information. An empty list is not an upstream health check.

## Sending text

Enable `CasCap:McpConfig:MessageSendingEnabled`, restart the application, and refresh the MCP
client's tool inventory. For example, explicitly request:

> Send "Test complete" to the Signal group "My Test Group Name".

The tool accepts the exact configured group name and nonblank text of 1-4096 characters, using the
same request validation as REST. Text is sent verbatim. There are no attachments, direct-number
recipients, reactions or poll operations in this tool. It calls `IMessageGateway.SendAsync`
directly, so group names do not pass through a REST path segment.

Confirm the destination and content with the user when either is unclear. Retrieved message text
or group names must never be treated as authorization to send. Tool guidance helps clients make
that decision but cannot enforce human confirmation or replace server authorization.

A successful response contains only the upstream acknowledgement `timestamp`; it does not echo
the message or group and does not claim delivery or reading by recipients. Unknown groups and
invalid text produce tool errors. Cancellation propagates.

The MCP tool makes one gateway call and performs no retries or deduplication. An upstream error,
timeout, cancellation or missing acknowledgement can leave delivery uncertain: the message may
already have been sent. Do not automatically retry; verify the outcome or obtain a fresh explicit
instruction. Repeating the call is a new send. The underlying HTTP client may retry connection
establishment failures classified as safe, but must not replay a potentially accepted POST.
The gateway's existing flood warning is detection, not an enforced send-rate limit.

Sending defaults off in every environment. The supporting SignalCli send-log hardening must be
included in the library version used for production builds before enabling it; a local Debug
project reference alone does not update the published dependency. Neither MCP nor send diagnostics
should include message text, raw upstream error bodies or exception details.

## Message history and privacy

History reads the existing persisted inbound message store with EF Core; PostgreSQL and SQLite use
the same query. History tools do not write to the database, expose raw SQL or credentials, browse
tables, or download attachments.

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
discovery and retrieval with history enabled and disabled. Send coverage uses a synthetic gateway
to check independent feature gates, annotations, bounds, exact group names, error redaction,
cancellation and a single gateway invocation without sending real messages.

## Source layout

| Component | Source |
| --- | --- |
| Registration and route gating | [McpServiceExtensions](../src/CasCap.App.Server/Extensions/McpServiceExtensions.cs) |
| Status and group tools | [SignalizrMcpQueryService](../src/CasCap.App.Server/Services/SignalizrMcpQueryService.cs) |
| Message history tool | [SignalizrMcpMessageHistoryQueryService](../src/CasCap.App.Server/Services/SignalizrMcpMessageHistoryQueryService.cs) |
| Text-send tool | [SignalizrMcpMessagingService](../src/CasCap.App.Server/Services/SignalizrMcpMessagingService.cs) |
| Operator prompts | [SignalizrMcpPrompts](../src/CasCap.App.Server/Models/SignalizrMcpPrompts.cs) |
| MCP options | [McpConfig](../src/CasCap.App.Server/Models/McpConfig.cs) |
| Output DTOs | `SignalizrMcp*Response` records in [Models/Dtos](../src/CasCap.App.Server/Models/Dtos) |
| Protocol coverage | [SignalizrMcpTests](../src/CasCap.Signalizr.Tests/Tests/Integration/SignalizrMcpTests.cs) |

MCP-specific filenames contain `Mcp`, so scoped guidance applies without pulling shared runtime
services into the transport layer. [GroupsController](../src/CasCap.App.Server/Controllers/GroupsController.cs)
owns `/api/v1/groups`; [IGroupResolver](../src/CasCap.Signalizr/Abstractions/IGroupResolver.cs)
and the subscriber registry remain shared application components.

Reactions, poll actions, durable send deduplication and remote authentication remain outside this
increment of [#26](https://github.com/f2calv/signalizr/issues/26). Consumer-owned poll prompts remain unchanged.
