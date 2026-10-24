# CasCap.Comms.Tests

Source-declared, credential-free tests for [CasCap.Comms](../CasCap.Comms/README.md) and
[CasCap.Comms.AI](../CasCap.Comms.AI/README.md).

## Prerequisites

Use .NET 10, xUnit v3 and Microsoft.Testing.Platform. Debug builds require the declared adjacent
shared, voice and Agent Runtime source dependencies. Release builds resolve those dependencies
from NuGet, including the runtime client and contracts consumed through `CasCap.Comms.AI`.
No Redis server, gateway, model or runtime credential is needed by these fixtures.

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
| `AgentCommsResponderTests` | 12 | 19 | Agent Runtime | Streamed progress, attachments, outcomes, tool failures, runtime failure notices, empty answers, cancellation, diagnostic opt-in, remote overrides, local bypass, unknown commands, plain-text admission and instruction disclosure/splitting |
| `CommsDebugNotifierTests` | 3 | 9 | Comms | Monitor diagnostics disclose prompts, tool arguments and event text only when `MonitorContentEnabled` is set; the reply footer shows only duration and tools unless token counts, session size or session counts are enabled |
| `MessagingMcpQueryServiceTests` | 1 | 1 | Comms | Poll tools activated per call by the MCP server use the configured group |
| `TrackedOperationServiceTests` | 2 | 2 | Comms | Tracked-operation progress, completion and timeout messages |
| **Total** | **64** | **88** | | |

Counts expand the inline-data theories in current source. They are not a claim that the suite was
executed during documentation maintenance.

## Trait Categories

| Category | Meaning |
| --- | --- |
| `Messaging` | Inbound message orchestration |
| `Comms` | Stream delivery, routing, formatting and duplicate suppression |
| `Agent Runtime` | Remote responder protocol and command ownership |

## Skipped Tests

No declared skips or credential-dependent skip conditions.

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

Run from the repository root when you intend to execute the suite.
