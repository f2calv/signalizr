# CasCap.Data

Internal, non-packable .NET 10 EF Core model and migrations for signalizr's durable messages,
attachments and subscriber cursors. Application delivery policy belongs to
[CasCap.Backend](../CasCap.Backend/README.md).

## Usage

Reference [CasCap.Data.csproj](CasCap.Data.csproj) to use
[SignalizrDbContext](SignalizrDbContext.cs) and its
[persistence entities](Entities). The backend's
[AddSignalizrDataLayer](../CasCap.Backend/Extensions/DataServiceCollectionExtensions.cs)
selects PostgreSQL, SQLite or explicitly non-durable InMemory and registers the context factory.
Factory-created contexts are caller-owned and must be disposed after each unit of work.

Database configuration, initialization metrics, retention, sending and subscriber coordination
remain in Backend. Consumer clients and wire contracts do not reference this persistence library.
See the [delivery contract](../../README.md#receiving).

## Migrations and Tooling

The context, [five migrations and snapshot](Migrations) and
[design-time factory](SignalizrDbContextDesignTimeFactory.cs) share the `CasCap.Data` assembly.
EF's default migration discovery follows the context assembly; table names and existing migration
identifiers are authoritative.

From the repository root, after restoring the repository's tool manifest:

```powershell
dotnet ef migrations list --project src\CasCap.Data --startup-project src\CasCap.Data --no-connect
```

This is an example, not an executed check. The design-time factory uses an isolated model target
and does not initialize the application. Applying migrations is a separate approved operation
through the host's finite migration mode, not this listing command.

The [backend test guide](../CasCap.Backend.Tests/README.md) covers offline assembly/model discovery,
SQLite schema transitions and application delivery behavior. SQLite and InMemory checks do not
establish PostgreSQL transaction or deployment safety.
