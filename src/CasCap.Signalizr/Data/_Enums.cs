namespace CasCap.Data;

/// <summary>Database provider selection for durable inbound delivery.</summary>
public enum DatabaseProvider
{
    /// <summary>EF Core InMemory provider for tests and explicitly non-durable local runs.</summary>
    InMemory,

    /// <summary>SQLite for a single receiver replica backed by persistent storage.</summary>
    Sqlite,

    /// <summary>PostgreSQL via Npgsql.</summary>
    Postgres,
}
