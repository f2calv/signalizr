# CasCap.Comms.AI

The AI agent responder for the [CasCap.Comms](../CasCap.Comms/README.md) Signalizr communications
pipeline.

## Purpose

`AgentCommsResponder` implements `ICommsResponder` through `CasCap.AgentRuntime.Client`. It:

- turns chat-bound stream events and inbound messages into agent turns, with conversation sessions,
  model and instruction overrides and optional session bypass;
- owns slash-command syntax while the runtime owns session/override mutations, and handles poll votes through `IPollTracker`;
- never forwards text starting with `/` to the model: an unrecognised command, including an unknown
  subcommand such as `/session help`, answers with the `/help` list. Command replies longer than one
  chat message are split into numbered parts such as `(1/3)`;
- shows the effective instructions for `/instructions` (the session override, else the active
  definition) when the runtime tenant enables `InstructionsDisclosureEnabled`, and otherwise only
  the session's own override;
- reports sub-agent delegation with a reaction swap and, optionally, a status message;
- appends a stats footer to each reply and posts a pipeline timeline, stream-event notices and
  compaction notices to the monitor group through `CommsDebugNotifier`;
- answers a failed runtime call with a short notice such as `Assistant unavailable: the agent
  runtime returned HTTP 503.`, which is sent but never spoken and marks the message with ❌. An empty
  answer is treated as the agent choosing to stay quiet and sends nothing;
- names every tool that failed, including inside delegated agents, on a `⚠️ Tool failed:` footer
  line, because the model's answer may not mention it, and marks failed calls with ❌ in the
  monitor timeline;
- counts every run in `{prefix}.comms.agent.runs`, tagged with a bounded `outcome`
  (`Succeeded`, `CompletedWithToolErrors`, `EmptyOutput`, `NoResponse`, `Unreachable`, `HttpError`,
  `Timeout`, `Error`).

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
| `CommsAgentMetrics` | Run counter by `CommsAgentRunOutcome` |
| `IAgentRunEnricher` | Host seam for measurements around each run, such as GPU energy use; `RequiresDiagnosticDetails` opts in to tool arguments and runtime property bags |
| `CommsAgentProfile` | Runtime agent name, opaque session identifier and attachment fallback prompt |
| `CommsDebugStep` | One step of the monitor-group pipeline timeline |
| `MessagingMcpQueryService` | MCP poll tools (`create_poll`, `close_poll`, `get_poll_status`) on the chat group |
| `IPollTracker`, `InMemoryPollTracker` | Comms-owned poll lifecycle, votes, result summaries, and TTL expiry; process-local, so a restart forgets open polls |
| `CommsAgentServiceCollectionExtensions` | `AddCommsAgent()` and `AddMessagingMcp()` |

## Configuration

The responder reads `CommsConfig` from [CasCap.Comms](../CasCap.Comms/README.md). Configure the
runtime address and timeout under `CasCap:AgentRuntimeClientOptions`; the host attaches workload
authentication and resilience to the `IHttpClientBuilder` returned by `AddCommsAgent`.

`CommsConfig.PollTtlMs` controls how long an agent-created poll remains available for votes.

The run counter uses `CasCap:CommsMetricsConfig:MetricNamePrefix` (default `comms`). Set it to the
host's exported meter prefix so its OpenTelemetry pipeline collects the counter.

```csharp
services.AddComms(configuration);
services.AddCommsAgent();
services.AddSingleton<IAgentRunEnricher, MyHardwareEnricher>();
```

## Dependencies

| Dependency | Use |
| --- | --- |
| `CasCap.Comms` | The pipeline this responder answers for |
| `CasCap.AgentRuntime.Client` | Streamed turns and typed session/override operations |
| `CasCap.AgentRuntime.Contracts` | Stable run, diagnostic and control DTOs |
| `ModelContextProtocol` | Poll tool attributes |

Debug builds reference the adjacent Agent Runtime source checkout. Add exact Release package
references only after `CasCap.AgentRuntime.Client` and `CasCap.AgentRuntime.Contracts` have immutable
published versions.
