using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace CasCap.Tests;

/// <summary>Covers durable inbound binary retrieval.</summary>
public sealed class InboundAttachmentServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsStoredBinaryContent()
    {
        var factory = new TestDbContextFactory();
        await using (var dbContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            dbContext.InboundMessages.Add(new InboundMessageEntity
            {
                Id = 1,
                Message = "voice",
                PersistedAtUnixMilliseconds = 1,
                Attachments =
                [
                    new InboundAttachmentEntity
                    {
                        Id = "attachment",
                        ContentType = "audio/ogg",
                        Filename = "voice.ogg",
                        Content = [1, 2, 3]
                    }
                ]
            });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = new InboundAttachmentService(factory);

        var content = await service.GetAsync("attachment", TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], content);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<SignalizrDbContext>
    {
        private readonly DbContextOptions<SignalizrDbContext> _options =
            new DbContextOptionsBuilder<SignalizrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .Options;

        public SignalizrDbContext CreateDbContext() => new(_options);

        public ValueTask<SignalizrDbContext> CreateDbContextAsync(
            CancellationToken _ = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
