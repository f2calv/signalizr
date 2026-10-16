# CasCap.Comms.Tests

Credential-free unit tests for [CasCap.Comms](../CasCap.Comms/README.md) and
[CasCap.Comms.AI](../CasCap.Comms.AI/README.md).

## Purpose

Every orchestration test drives the real `CommunicationsBgService.ExecuteAsync` pipeline through
`CommunicationsBgServiceTestFixture`, over `FakeSignalizrClient`, the speech fakes from
`CasCap.Api.Voice.Testing` and an in-memory Redis. The responder is a real `AgentCommsResponder` over
the typed Agent Runtime client and `FakeAgentRuntimeHttpMessageHandler`, so no test reaches a model,
runtime service, Redis or gateway.

The tests live with the packages in the signalizr repository and run without Redis, a gateway, or an
AI provider.

## Tests

| Class | Methods | Cases | Category | Description |
| --- | ---: | ---: | --- | --- |
| `CommunicationsBgServiceTests` | 17 | 22 | Messaging | Inbound admission, duplicate suppression, durable attachments, voice-processing modes, transcript echo, reply-queue backpressure, the runtime failure notice, spoken-reply attachment, the monitor notice for a failed spoken reply and answering with stream consumption disabled |
| `CommunicationsBgServiceStreamTests` | 14 | 17 | Comms | Direct and responder stream delivery, disabled stream turns, direct-delivery sources, monitor routing, stale and throttled drops, cached media, startup retry, the missing-group fault, resubscription, the no-responder inbound behaviour and the inbound filter |
| `SignalMessageDeduplicatorTests` | 9 | 11 | Comms | Redis-backed duplicate Signal message suppression |
| `MonitorSourcesGroupRouterTests` | 2 | 3 | Comms | Routing of configured operational sources to the monitor group |
| `CommsEventFormatterTests` | 2 | 2 | Comms | The plain and timestamped direct-send formats |
| `InMemoryPollTrackerTests` | 2 | 2 | Messaging | Poll vote summaries and TTL expiry |
| `AgentCommsResponderTests` | 8 | 13 | Agent Runtime | Streamed live progress, attachments, diagnostics, run outcomes, failed-tool footer, failure notices for unreachable, HTTP-error, timed-out, response-less and malformed runs, empty answers, caller cancellation, diagnostic details requested only when an enricher needs them, remote overrides and local bypass |
| `CommsDebugNotifierTests` | 3 | 9 | Comms | Monitor diagnostics disclose prompts, tool arguments and event text only when `MonitorContentEnabled` is set; the reply footer shows only duration and tools unless token counts, session size or session counts are enabled |
| `MessagingMcpQueryServiceTests` | 1 | 1 | Comms | Poll tools activated per call by the MCP server use the configured group |
| `TrackedOperationServiceTests` | 2 | 2 | Comms | Tracked-operation progress, completion and timeout messages |
| **Total** | **60** | **82** | | |

## Trait Categories

| Category | Meaning |
| --- | --- |
| `Messaging` | Inbound message orchestration |
| `Comms` | Stream delivery, routing, formatting and duplicate suppression |
| `Agent Runtime` | Remote responder protocol and command ownership |

## Skipped Tests

None.

## Layout

```text
Tests/
└── Unit/
    ├── CommsEventFormatterTests.cs
    ├── AgentCommsResponderTests.cs
    ├── CommsDebugNotifierTests.cs
    ├── CommunicationsBgServiceStreamTests.cs
    ├── CommunicationsBgServiceTestFixture.cs   # Service over deterministic fakes
    ├── CommunicationsBgServiceTests.cs
    ├── FakeHostEnvironment.cs
    ├── FakeNotificationAttachment.cs
    ├── FakePollTracker.cs
    ├── FakeReceivedNotification.cs
    ├── FakeAgentRuntimeHttpMessageHandler.cs
    ├── FakeSignalMessageDeduplicator.cs
    ├── InMemoryCommsRedis.cs                   # Stream and string commands only
    ├── InMemoryPollTrackerTests.cs
    ├── MessagingMcpQueryServiceTests.cs
    ├── MonitorSourcesGroupRouterTests.cs
    ├── SignalMessageDeduplicatorTests.cs
    ├── TestMetrics.cs
    ├── ThrowingRemoteCache.cs
    └── TrackedOperationServiceTests.cs
```

## Running

```powershell
dotnet test --project src/CasCap.Comms.Tests/CasCap.Comms.Tests.csproj
```
