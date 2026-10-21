# CasCap.Signalizr.Tests

Source-declared tests for signalizr's .NET 10 gateway, durable delivery, .NET client helpers and
HTTP MCP contract. Fixtures use synthetic inputs and local/in-memory dependencies, not a live
Signal account.

## Prerequisites

Use .NET 10 with xUnit v3 and Microsoft.Testing.Platform. The project references the application
host; its Debug build requires the declared adjacent shared-library and Agent Runtime sources.
The current host graph lacks Release runtime-client/contracts references; see
[host prerequisites](../CasCap.App.Server/README.md#run).

MCP and REST fixtures open local HTTP listeners. Persistence fixtures use EF Core InMemory or
in-memory SQLite; no external PostgreSQL, Redis, Azure credential or Signal wrapper is required.
Source includes test configuration files, but these fixtures use synthetic options and fake gateways.

## Tests

| Class | Methods | Expanded cases | Scope |
| --- | ---: | ---: | --- |
| `DispatcherMappingTests` | 12 | 12 | Group mapping, text/self-message handling, poll votes and delivery identity |
| `DurableMessagePrunerServiceTests` | 2 | 2 | Globally acknowledged cleanup and hard content-retention cap |
| `GroupResolverTests` | 12 | 13 | Exact names, ambiguous/missing groups and indexed configuration validation |
| `InboundAttachmentServiceTests` | 1 | 1 | Stored binary attachment retrieval |
| `InboundMessageQueueTests` | 7 | 7 | Bounded drop-oldest queue and counters |
| `InboundPollVoteTests` | 2 | 4 | Stored vote option-index serialization and absent selections |
| `InboundSubscriberRegistryTests` | 13 | 13 | Replay, independent cursors, identities, ordered acknowledgements, per-subscriber group filtering and cursor advance past other groups |
| `MessageGatewayTests` | 9 | 13 | Upstream send-request projection, data URIs and reaction-target validation |
| `SignalizrClientConfigTests` | 1 | 1 | Explicit durable subscriber-name requirement |
| `SignalizrClientExtensionsTests` | 8 | 8 | Best-effort interactions, cancellation and startup group checks |
| `SignalizrMetricsTests` | 3 | 3 | Instrument units/descriptions, backlog gauge and outbound counters |
| `SubscriberConfigValidatorTests` | 3 | 4 | Subscriber groups must be served groups, matched exactly, and never empty |
| `GroupNameMigrationTests` | 1 | 1 | SQLite migration preserves rows across schema transitions and rollback |
| `GroupRestTests` | 1 | 5 | Exact group-name query routing over local HTTP |
| `InboundGrpcServiceTests` | 4 | 4 | Acknowledgement timeout, independent consumers, duplicate live identity and unconfigured subscriber refusal |
| `SignalizrMcpTests` | 25 | 41 | Authentication, feature gates, status, groups, bounded history, prompts and controlled sending |
| **Total** | **104** | **132** | |

Counts are statically derived from facts, theories and inline data, not observed test results.
These fixtures do not establish production-scale reliability, long-running stability or loss
behavior during a real wrapper outage.

## Trait Categories

| Category | Expanded cases | Scope |
| --- | ---: | --- |
| `Integration` | 47 | Local HTTP REST/MCP fixtures and SQLite schema migrations |

The remaining 85 cases have no category trait, including the service-level gRPC fixtures.
Filtering out `Category=Integration` therefore does not mean “no database-backed fixture.”

## Skipped Tests

No declared skips or credential-dependent skip conditions.

## Layout

```text
Tests/
├── Unit/
│   ├── DispatcherMappingTests.cs
│   ├── DurableMessagePrunerServiceTests.cs
│   ├── GroupResolverTests.cs
│   ├── InboundAttachmentServiceTests.cs
│   ├── InboundMessageQueueTests.cs
│   ├── InboundPollVoteTests.cs
│   ├── InboundSubscriberRegistryTests.cs
│   ├── MessageGatewayTests.cs
│   ├── SignalizrClientConfigTests.cs
│   ├── SignalizrClientExtensionsTests.cs
│   ├── SignalizrMetricsTests.cs
│   └── SubscriberConfigValidatorTests.cs
├── Integration/
│   ├── GroupNameMigrationTests.cs
│   ├── GroupRestTests.cs
│   ├── InboundGrpcServiceTests.cs
│   └── SignalizrMcpTests.cs
└── Fakes/
    ├── FakeGroupResolver.cs
    ├── FakeMcpMessageGateway.cs
    └── FakeOperatorNotifier.cs
```

## Run

From the repository root, when you intend to execute the suite:

```powershell
dotnet test --project src/CasCap.Signalizr.Tests/CasCap.Signalizr.Tests.csproj
```

For the consumer-facing semantics behind these cases, read the
[delivery contract](../../README.md#receiving) and [MCP guide](../../docs/mcp.md).
