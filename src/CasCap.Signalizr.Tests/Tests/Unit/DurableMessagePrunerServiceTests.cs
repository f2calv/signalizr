using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using CasCap.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasCap.Tests;

/// <summary>Covers bounded retention for durable message content.</summary>
public sealed class DurableMessagePrunerServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbContextFactory = new();
    private readonly SignalizrMetrics _metrics = new();
    private readonly DateTimeOffset _now = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Hard_retention_deletes_unacknowledged_messages_older_than_thirty_days()
    {
        await SeedAsync(
            new InboundMessageEntity { Id = 1, Message = "expired", PersistedAtUtc = _now.AddDays(-31) },
            new InboundMessageEntity { Id = 2, Message = "retained", PersistedAtUtc = _now.AddDays(-29) });
        var service = CreateService();

        await service.PruneOnceAsync(TestContext.Current.CancellationToken);

        await using var dbContext = await _dbContextFactory
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal([2L], await dbContext.InboundMessages
            .Select(message => message.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Acknowledged_retention_deletes_only_rows_behind_every_cursor()
    {
        await SeedAsync(
            new InboundMessageEntity { Id = 1, Message = "acknowledged", PersistedAtUtc = _now.AddHours(-25) },
            new InboundMessageEntity { Id = 2, Message = "not-everywhere", PersistedAtUtc = _now.AddHours(-25) });
        await using (var dbContext = await _dbContextFactory
            .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            dbContext.SubscriberCursors.AddRange(
                new SubscriberCursorEntity
                {
                    SubscriberName = "first",
                    LastAcknowledgedMessageId = 2,
                    UpdatedAtUtc = _now
                },
                new SubscriberCursorEntity
                {
                    SubscriberName = "second",
                    LastAcknowledgedMessageId = 1,
                    UpdatedAtUtc = _now
                });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = CreateService();

        await service.PruneOnceAsync(TestContext.Current.CancellationToken);

        await using var verification = await _dbContextFactory
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal([2L], await verification.InboundMessages
            .Select(message => message.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private DurableMessagePrunerService CreateService() => new(
        NullLogger<DurableMessagePrunerService>.Instance,
        Options.Create(new DatabaseConfig
        {
            Provider = DatabaseProvider.InMemory,
            DurabilityRequired = false,
            AcknowledgedMessageRetentionHours = 24,
            MessageRetentionDays = 30
        }),
        new FixedTimeProvider(_now),
        _metrics,
        _dbContextFactory);

    private async Task SeedAsync(params InboundMessageEntity[] messages)
    {
        await using var dbContext = await _dbContextFactory
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        dbContext.InboundMessages.AddRange(messages);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose() => _metrics.Dispose();

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SignalizrDbContext>
    {
        private readonly DbContextOptions<SignalizrDbContext> _options =
            new DbContextOptionsBuilder<SignalizrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .Options;

        public SignalizrDbContext CreateDbContext() => new(_options);

        public ValueTask<SignalizrDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}