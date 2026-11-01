using CasCap.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CasCap.Tests;

/// <summary>Validates group-name schema expansion, mixed writers and rollback using a migrated SQLite database.</summary>
[Trait("Category", "Integration")]
public sealed class GroupNameMigrationTests
{
    [Fact]
    public async Task GroupNameMigration_PreservesRowsAndSynchronizesWriters()
    {
        const string PreviousMigration = "20260926033021_AddSelfFlagAndPollVotes";
        const string ExpandMigration = "20260927020916_AddGroupNameToInboundMessages";
        const string ContractMigration = "20261010031316_CompleteInboundMessageGroupName";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<SignalizrDbContext>().UseSqlite(connection).Options;
        await using var db = new SignalizrDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO inbound_messages(id, channel, message, timestamp, persisted_at_unix_milliseconds)
            VALUES (1, 'My Test Group Name', 'retained text', 123, 456);
            """, cancellationToken);

        await migrator.MigrateAsync(ExpandMigration, cancellationToken);
        var retained = await db.InboundMessages.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal("My Test Group Name", retained.GroupName);
        Assert.Equal("retained text", retained.Message);
        Assert.Equal(123, retained.Timestamp);
        Assert.Equal(456, retained.PersistedAtUnixMilliseconds);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO inbound_messages(id, channel, persisted_at_unix_milliseconds)
            VALUES (2, 'Old Writer Group', 457);
            INSERT INTO inbound_messages(id, group_name, persisted_at_unix_milliseconds)
            VALUES (3, 'New Writer Group', 458);
            UPDATE inbound_messages SET channel = 'Updated Old Writer' WHERE id = 2;
            UPDATE inbound_messages SET group_name = 'Updated New Writer' WHERE id = 3;
            """, cancellationToken);
        Assert.Equal("Updated Old Writer", await ScalarAsync(connection, "SELECT group_name FROM inbound_messages WHERE id = 2"));
        Assert.Equal("Updated New Writer", await ScalarAsync(connection, "SELECT channel FROM inbound_messages WHERE id = 3"));

        await db.Database.ExecuteSqlRawAsync("UPDATE inbound_messages SET group_name = NULL WHERE id = 3", cancellationToken);
        Assert.Equal(DBNull.Value, await ScalarAsync(connection, "SELECT channel FROM inbound_messages WHERE id = 3"));
        await db.Database.ExecuteSqlRawAsync("UPDATE inbound_messages SET channel = NULL WHERE id = 2", cancellationToken);
        Assert.Equal(DBNull.Value, await ScalarAsync(connection, "SELECT group_name FROM inbound_messages WHERE id = 2"));

        await migrator.MigrateAsync(ContractMigration, cancellationToken);
        Assert.Equal(3, await db.InboundMessages.CountAsync(cancellationToken));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('inbound_messages') WHERE name = 'channel'"));

        await migrator.MigrateAsync(ExpandMigration, cancellationToken);
        Assert.Equal("My Test Group Name", await ScalarAsync(connection, "SELECT channel FROM inbound_messages WHERE id = 1"));
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        Assert.Equal("My Test Group Name", await ScalarAsync(connection, "SELECT channel FROM inbound_messages WHERE id = 1"));
        Assert.Equal("retained text", await ScalarAsync(connection, "SELECT message FROM inbound_messages WHERE id = 1"));
        Assert.Equal(3L, await ScalarAsync(connection, "SELECT COUNT(*) FROM inbound_messages"));
        await migrator.MigrateAsync(ContractMigration, cancellationToken);
        Assert.Equal(3, await db.InboundMessages.CountAsync(cancellationToken));
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
