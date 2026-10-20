# CasCap.App.Server.Tests

Source-declared unit and integration tests for the .NET 10 signalizr ASP.NET Core host and startup
configuration. These tests do not require a live Signal account or Agent Runtime.

## Prerequisites

Use the .NET 10 SDK with xUnit v3 on Microsoft.Testing.Platform. Debug project references require
the adjacent shared-library and Agent Runtime sources used by the host. The host's current
Release dependency gap also affects this test project's Release build; see the
[host build prerequisites](../CasCap.App.Server/README.md#run).

## Purpose

The unit tests validate feature parsing directly. The integration tests launch the real host through
`WebApplicationFactory<AppEntryPoint>` with only `DemoClient`, or only `Comms`, enabled and remove hosted
services so no Signal, database, gRPC or Agent Runtime dependency runs.
The configuration guard test exercises a missing Key Vault name and does not contact Azure.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `FeatureConfigTests` | 9 | 21 | Validates missing, empty, unknown, case-insensitive, MCP-dependent and Comms-exclusive feature configuration |
| `HostStartupTests` | 2 | 2 | Verifies host options, infrastructure services, controller services, and the startup health endpoint |
| `CommsHostStartupTests` | 2 | 2 | Verifies a `Comms`-only host registers the agent responder without gateway roles and with stream consumption off |
| `HostApplicationBuilderExtensionsTests` | 1 | 1 | Rejects an Agent Runtime secret prefix without a configured Key Vault |
| **Total** | **14** | **26** | |

Counts are derived from `[Fact]`, `[Theory]` and inline data in source, not an execution report.

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 4 | In-memory ASP.NET Core host and dependency-injection checks |

## Skipped Tests

There are no declared skipped tests or credential-dependent skip conditions.

## Layout

```text
Tests/
├── Integration/
│   ├── CommsHostStartupTests.cs
│   ├── CommsWebApplicationFactory.cs
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
