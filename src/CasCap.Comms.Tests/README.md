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
| `CommunicationsBgServiceTests` | 13 | 18 | Messaging | Inbound admission, duplicate suppression, durable attachments, voice-processing modes, transcript echo and reply-queue backpressure |
| `CommunicationsBgServiceStreamTests` | 14 | 17 | Comms | Direct and responder stream delivery, disabled stream turns, direct-delivery sources, monitor routing, stale and throttled drops, cached media, startup retry, the missing-group fault, resubscription, the no-responder inbound behaviour and the inbound filter |
| `SignalMessageDeduplicatorTests` | 9 | 11 | Comms | Redis-backed duplicate Signal message suppression |
| `MonitorSourcesGroupRouterTests` | 2 | 3 | Comms | Routing of configured operational sources to the monitor group |
| `CommsEventFormatterTests` | 2 | 2 | Comms | The plain and timestamped direct-send formats |
| `InMemoryPollTrackerTests` | 2 | 2 | Messaging | Poll vote summaries and TTL expiry |
| `AgentCommsResponderTests` | 3 | 3 | Agent Runtime | Streamed live progress, attachments, diagnostics, remote overrides and local bypass |
| **Total** | **45** | **56** | | |

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
    ├── MonitorSourcesGroupRouterTests.cs
    ├── SignalMessageDeduplicatorTests.cs
    ├── TestMetrics.cs
    └── ThrowingRemoteCache.cs
```

## Running

```powershell
dotnet test --project src/CasCap.Comms.Tests/CasCap.Comms.Tests.csproj
```
