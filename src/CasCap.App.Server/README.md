# CasCap.App.Server

The .NET 10 ASP.NET Core host for signalizr. Operators select gateway, receive, migration, demo
or communications roles from the same application image; applications consume its REST and gRPC
contracts rather than referencing this executable.

## Run

Start with the repository [Compose prerequisites](../../README.md#quick-start) or
[Helm deployment guide](../../charts/signalizr/README.md). A registered/linked Signal account and
exact group names are required for gateway operation.

For a configured source checkout:

```powershell
dotnet run --project src/CasCap.App.Server/CasCap.App.Server.csproj --configuration Debug
```

Run from the repository root. Debug builds require the adjacent source dependencies declared by the
project graph, including the remote Agent Runtime client and contracts. Release builds and the
Dockerfile resolve the same dependencies from NuGet, so they work from a clean clone.

Do not use the container's `/var/lib/signalizr` database path for a Windows source run. Override it
with a writable local SQLite path as below.

## Configuration

JSON settings bind from `CasCap`; environment variables replace `:` with `__`.
The host loads standard appsettings/environment settings, local settings, user secrets and
command-line configuration through its shared configuration provider. Keep account credentials in
untracked settings or secret stores.

| Feature | Purpose and requirements |
| --- | --- |
| `Gateway` | REST send/group operations; wrapper address, account number and exact group names |
| `Receiver` | Single receive owner, durable persistence, attachments and gRPC subscriptions |
| `Mcp` | Requires Gateway and Receiver together, plus Basic credentials; see the [MCP guide](../../docs/mcp.md) |
| `DemoClient` | Subscribes to an existing gateway; requires both gateway addresses and a stable subscriber name; never sends a reply |
| `Comms` | Runs alone as a gateway client; Redis, chat group, subscriber identity and Agent Runtime configuration |
| `DbMigrator` | Applies SQLite/PostgreSQL migrations and stops the host; rejects InMemory |

`CasCap:FeatureConfig:EnabledFeatures` is a comma-separated, case-insensitive list.
Empty and unknown values fail startup. The base settings select `Gateway,Receiver`.
Disabled feature controllers are removed from routing and return `404`.

| Setting | Default / operator action |
| --- | --- |
| `SignalCliConfig:BaseAddress` | `http://localhost:8180` in base appsettings; point at your wrapper |
| `SignalCliConfig:PhoneNumber` | Synthetic placeholder; supply the registered account |
| `GroupConfig:GroupNames` | Empty; configure exact names, preserving spaces and case; missing, duplicate or ambiguous matches fail resolution |
| `GrpcHostConfig:Http1Port`, `Http2Port` | `8080`, `5001`; applied when Receiver is enabled |
| `DatabaseConfig:Provider` | `Sqlite`; alternatively `Postgres`; InMemory requires `DurabilityRequired=false` and is for tests |
| `DatabaseConfig:ConnectionString` | Required for relational providers; base appsettings uses `/var/lib/signalizr/signalizr.db` |
| `DatabaseConfig:DatabaseName` | Optional PostgreSQL database override |
| `DatabaseConfig:MigrateOnStartup` | `true`; set `false` when using a separate completed-before-start migrator |
| `DatabaseConfig:AcknowledgedMessageRetentionHours`, `MessageRetentionDays` | `24`, `30`; hard retention also removes unacknowledged messages and attachments |
| `ReceiverConfig:QueueCapacity`, `MaxAttachmentBytes` | `1000`, `104857600` (100 MiB per attachment) |
| `SubscriberConfig:ReplayBatchSize`, `MaxOutstanding`, `AckTimeoutMs` | `100`, `32`, `30000` |
| `SubscriberConfig:SubscriberGroups` | Empty; map each subscriber name to the exact groups it receives. Unlisted names are refused, and every group must also be in `GroupConfig:GroupNames` |
| `GatewayConfig:ProfileName` | `null`; optionally apply an account-wide display name at startup |
| `GatewayConfig:SendRateWarningPerMinute` | `30`; detection only, not send throttling |
| `GatewayConfig:TypingRefreshIntervalMs`, `TypingMaxDurationMs` | `10000`, `180000`; per-process typing leases |
| `OperatorNotificationConfig:NotificationsEnabled` | `false`; Note-to-Self startup, subscriber and drop/flood notices |

REST and health use HTTP/1.1; gRPC needs its separate HTTP/2 port for plaintext connections.
For roles without Receiver, use normal ASP.NET Core URL configuration rather than expecting
`GrpcHostConfig` to create listeners.

## Configuration Examples

Minimal single-process development override; merge into untracked local configuration and replace
the synthetic account/group with your own:

```json
{
  "CasCap": {
    "FeatureConfig": { "EnabledFeatures": "Gateway,Receiver" },
    "SignalCliConfig": {
      "BaseAddress": "http://localhost:8180",
      "PhoneNumber": "+10000000000"
    },
    "GroupConfig": { "GroupNames": ["Example Chat"] },
    "DatabaseConfig": {
      "Provider": "Sqlite",
      "ConnectionString": "Data Source=signalizr.local.db"
    }
  }
}
```

PostgreSQL with externally ordered migrations; provide the connection string separately through
`CasCap__DatabaseConfig__ConnectionString`:

```json
{
  "CasCap": {
    "DatabaseConfig": {
      "Provider": "Postgres",
      "DatabaseName": "example_gateway",
      "MigrateOnStartup": false,
      "AcknowledgedMessageRetentionHours": 24,
      "MessageRetentionDays": 30
    },
    "ReceiverConfig": { "QueueCapacity": 2000 },
    "SubscriberConfig": { "MaxOutstanding": 64, "AckTimeoutMs": 60000 }
  }
}
```

Run the same database settings with `EnabledFeatures=DbMigrator` first. Do not overlap Receiver
instances or allow concurrent migrations against the same database.

## Comms Responder

The `Comms` role registers caching, [CasCap.Comms](../CasCap.Comms/README.md) and
[CasCap.Comms.AI](../CasCap.Comms.AI/README.md). Unlike the library default, this application's base
appsettings sets `CommsConfig:StreamConsumptionEnabled=false`: it answers inbound messages without
consuming another application's event stream. Redis is still needed for duplicate suppression.

Configure `CasCap:SignalizrClientConfig` with both gateway addresses and a durable `SubscriberName`;
set the chat group and `AgentRuntimeClientOptions:BaseAddress`. Agent Runtime workload authentication
binds from `CasCap:AgentRuntimeAzureAuthConfig`; supply the execution identity and scope required by
your runtime rather than borrowing the gateway's Signal account.

Optional `AppConfig:KeyVaultName` loads secrets using `AppConfig:AzureEntra*` credentials and maps
`--` to `:`. `AppConfig:AgentRuntimeKeyVaultPrefix` selects only this host's reserved Agent Runtime
identity prefix. Startup rejects a prefix without a vault, or a configured vault without a usable
credential. Other applications' runtime identities are excluded from loading.

## Access and Diagnostics

REST, attachments, gRPC and health are unauthenticated; restrict network access. MCP alone uses
Basic authentication. See [authentication and privacy](../../README.md#authentication-and-privacy)
before exposing the application.

The host maps `/healthz`, `/healthz/startup`, `/healthz/ready` and `/healthz/live`; wrapper reachability
belongs to readiness. See [observability](../../README.md#observability) and the
[host test guide](../CasCap.App.Server.Tests/README.md) for instrumentation and source-declared coverage.
