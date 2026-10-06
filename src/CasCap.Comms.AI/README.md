# CasCap.Comms.AI

The AI agent responder for the [CasCap.Comms](../CasCap.Comms/README.md) Signalizr communications
pipeline.

## Purpose

`AgentCommsResponder` implements `ICommsResponder` through `CasCap.AgentRuntime.Client`. It:

- turns chat-bound stream events and inbound messages into agent turns, with conversation sessions,
  model and instruction overrides and optional session bypass;
- owns slash-command syntax while the runtime owns session/override mutations, and handles poll votes through `IPollTracker`;
- reports sub-agent delegation with a reaction swap and, optionally, a status message;
- appends a stats footer to each reply and posts a pipeline timeline, stream-event copies and
  compaction notices to the monitor group through `CommsDebugNotifier`.

The runtime owns tenant definitions, credentials, agent construction, tools and version-qualified
session state. Comms retains Signal delivery, typing, reactions, polls, attachments and diagnostics.

This project is owned and published by the signalizr repository alongside `CasCap.Comms` and
`CasCap.Signalizr.Client`. Applications use an adjacent signalizr project reference in Debug and the
published package in Release.

## Public Surface

| Type | Purpose |
| --- | --- |
| `AgentCommsResponder` | `ICommsResponder` backed by the streamed Agent Runtime client |
| `CommsDebugNotifier` | Stats footer and monitor-group diagnostics |
| `IAgentRunEnricher` | Host seam for measurements around each run, such as GPU energy use |
| `CommsAgentProfile` | Runtime agent name, opaque session identifier and attachment fallback prompt |
| `CommsDebugStep` | One step of the monitor-group pipeline timeline |
| `MessagingMcpQueryService` | MCP poll tools (`create_poll`, `close_poll`, `get_poll_status`) on the chat group |
| `IPollTracker`, `InMemoryPollTracker` | Comms-owned poll lifecycle, votes, result summaries, and TTL expiry |
| `CommsAgentServiceCollectionExtensions` | `AddCommsAgent(agentName, sessionId, defaultPrompt)`, `AddMessagingMcp()` and `AddMessagingMcpStub()` |

## Configuration

The responder reads `CommsConfig` from [CasCap.Comms](../CasCap.Comms/README.md). Configure the
runtime address and timeout under `CasCap:AgentRuntimeClientOptions`; the host attaches workload
authentication and resilience to the `IHttpClientBuilder` returned by `AddCommsAgent`.

`CommsConfig.PollTtlMs` controls how long an agent-created poll remains available for votes.

```csharp
services.AddComms(configuration);
services.AddCommsAgent("CommsAgent", "primary-group", "Describe this attachment.");
services.AddSingleton<IAgentRunEnricher, MyHardwareEnricher>();
```

## Dependencies

| Dependency | Use |
| --- | --- |
| `CasCap.Comms` | The pipeline this responder answers for |
| `CasCap.AgentRuntime.Client` | Streamed turns and typed session/override operations |
| `CasCap.AgentRuntime.Contracts` | Stable run, diagnostic and control DTOs |
| `ModelContextProtocol` | Poll tool attributes |

Debug builds reference adjacent source checkouts; Release builds use the published packages.
