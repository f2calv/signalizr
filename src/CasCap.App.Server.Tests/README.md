# CasCap.App.Server.Tests

Source-declared unit and integration tests for the .NET 10 signalizr ASP.NET Core host and startup
configuration. These tests do not require a live Signal account or Agent Runtime.

## Prerequisites

Use the .NET 10 SDK with xUnit v3 on Microsoft.Testing.Platform. Debug project references require
the adjacent shared-library and Agent Runtime sources used by the host; Release builds resolve them
from NuGet.

## Purpose

The unit tests validate feature parsing directly. The integration tests launch the real host through
`WebApplicationFactory<AppEntryPoint>` with `DemoClient`, `Comms`, or Gateway/Receiver enabled and remove
hosted services so no external Signal, database, gRPC or Agent Runtime dependency runs. The gateway
fixture replaces sending and group resolution with fakes and stores synthetic attachment rows in an
isolated EF Core InMemory database.
The configuration guard test exercises a missing Key Vault name and does not contact Azure.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `FeatureConfigTests` | 11 | 24 | Validates missing, empty, unknown, case-insensitive, MCP-dependent and exclusive Comms/migration feature configuration |
| `HostStartupTests` | 2 | 2 | Verifies lifecycle options, infrastructure/controller services, and the startup health endpoint |
| `CommsHostStartupTests` | 2 | 2 | Verifies a `Comms`-only host registers the agent responder without gateway roles and with stream consumption off |
| `HostApplicationBuilderExtensionsTests` | 1 | 1 | Rejects an Agent Runtime secret prefix without a configured Key Vault |
| [`GatewayApiContractTests`](Tests/Integration/GatewayApiContractTests.cs) | 8 | 26 | All group operation routes, typed JSON/no-content results, validation Problem Details, error mappings under non-JSON Accept headers, cancellation, feature gating, attachment bytes and API Explorer metadata |
| **Total** | **24** | **55** | |

Counts are derived from `[Fact]`, `[Theory]` and inline data in source, not an execution report.

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 30 | In-memory ASP.NET Core host, HTTP contract and dependency-injection checks |

## Skipped Tests

There are no declared skipped tests or credential-dependent skip conditions.

## Layout

```text
Tests/
├── Integration/
│   ├── CommsHostStartupTests.cs
│   ├── CommsWebApplicationFactory.cs
│   ├── GatewayApiContractTests.cs
│   ├── GatewayWebApplicationFactory.cs
│   ├── HostStartupTests.cs
│   └── SignalizrWebApplicationFactory.cs
└── Unit/
    ├── FeatureConfigTests.cs
    └── HostApplicationBuilderExtensionsTests.cs
```

## Dependencies

| Dependency | Purpose |
| --- | --- |
| `CasCap.App.Server` | Application and configuration under test |
| `Microsoft.AspNetCore.Mvc.Testing` | In-memory ASP.NET Core test host |
| `xunit.v3` | Test framework and Microsoft.Testing.Platform runner |

## Run

```powershell
dotnet test --project src/CasCap.App.Server.Tests/CasCap.App.Server.Tests.csproj
```

Run from the repository root when you intend to execute the suite. The command builds and runs
tests; documentation validation does not require it.

Gateway contract checks substitute `IMessageGateway` completely: they verify HTTP dispatch and
response contracts, not upstream Signal delivery or retry behavior. Attachment checks exercise the
real query service over synthetic local rows, not downloads from the wrapper. The host has no
OpenAPI document generator configured; metadata assertions inspect MVC API Explorer.
