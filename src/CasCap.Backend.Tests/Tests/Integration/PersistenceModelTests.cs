using CasCap.Data;
using CasCap.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies persistence assembly and migration metadata without connecting to a database.</summary>
[Trait("Category", "Integration")]
public sealed class PersistenceModelTests
{
    [Fact]
    public void DataAssembly_PreservesMigrationsAndModel()
    {
        using var context = new SignalizrDbContextDesignTimeFactory().CreateDbContext([]);
        string[] expected =
        [
            "20260924023005_InitialCreate",
            "20260924072036_AddInboundAttachments",
            "20260926033021_AddSelfFlagAndPollVotes",
            "20260927020916_AddGroupNameToInboundMessages",
            "20261010031316_CompleteInboundMessageGroupName",
        ];

        Assert.Equal("CasCap.Data", typeof(SignalizrDbContext).Assembly.GetName().Name);
        Assert.NotSame(typeof(SignalizrDbContext).Assembly, typeof(MessageGateway).Assembly);
        Assert.Equal(expected, context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
