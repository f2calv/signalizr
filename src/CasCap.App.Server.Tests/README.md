# CasCap.App.Server.Tests

Credential-free unit and integration tests for the signalizr ASP.NET Core host and its startup configuration.

## Purpose

The unit tests validate feature parsing directly. The integration tests launch the real host through
`WebApplicationFactory<AppEntryPoint>` with only `DemoClient` enabled and remove hosted services so no Signal,
database, or gRPC dependency runs.

## Tests

| Class | Method count | Test-case count | Description |
| --- | ---: | ---: | --- |
| `FeatureConfigTests` | 7 | 17 | Validates missing, empty, unknown, case-insensitive, and MCP-dependent feature configuration |
| `HostStartupTests` | 2 | 2 | Verifies host options, infrastructure services, controller services, and the startup health endpoint |

## Trait Categories

| Category | Test-case count | Purpose |
| --- | ---: | --- |
| `Integration` | 2 | In-memory ASP.NET Core host and dependency-injection checks |

## Skipped Tests

There are no skipped tests.

## Layout

```text
Tests/
├── Integration/
│   ├── HostStartupTests.cs
│   └── SignalizrWebApplicationFactory.cs
└── Unit/
    └── FeatureConfigTests.cs
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
