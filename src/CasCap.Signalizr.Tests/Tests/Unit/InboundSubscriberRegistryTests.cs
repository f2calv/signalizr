using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using CasCap.Exceptions;
using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasCap.Tests;

/// <summary>Covers durable subscriber registration, replay, and acknowledgement cursors.</summary>
public sealed class InboundSubscriberRegistryTests
{
    [Fact]
    public async Task Subscribing_and_unsubscribing_tracks_the_count()
    {
        using var fixture = new RegistryFixture();
        Assert.Equal(0, fixture.Registry.Count);

        var first = await fixture.Registry.SubscribeAsync("a", TestContext.Current.CancellationToken);
        var second = await fixture.Registry.SubscribeAsync("b", TestContext.Current.CancellationToken);
        Assert.Equal(2, fixture.Registry.Count);

        fixture.Registry.Unsubscribe(first);
        fixture.Registry.Unsubscribe(first);
        Assert.Equal(1, fixture.Registry.Count);

        fixture.Registry.Unsubscribe(second);
        Assert.Equal(0, fixture.Registry.Count);
    }

    [Fact]
    public async Task First_subscription_starts_at_the_current_tail()
    {
        using var fixture = new RegistryFixture();
        await fixture.AddMessageAsync("before");
        var subscription = await fixture.Registry.SubscribeAsync(
            "new-subscriber", TestContext.Current.CancellationToken);
        await fixture.AddMessageAsync("after");
        fixture.Registry.NotifyMessageAvailable();

        var delivery = await ReadOneAsync(subscription);

        Assert.Equal("after", delivery.Message);
    }

    [Fact]
    public async Task Unacknowledged_message_is_replayed_after_reconnect()
    {
        using var fixture = new RegistryFixture();
        var first = await fixture.Registry.SubscribeAsync("durable", TestContext.Current.CancellationToken);
        await fixture.AddMessageAsync("replay-me");
        fixture.Registry.NotifyMessageAvailable();
        var firstAttempt = await ReadOneAsync(first);
        fixture.Registry.Unsubscribe(first);

        var second = await fixture.Registry.SubscribeAsync("durable", TestContext.Current.CancellationToken);
        var replay = await ReadOneAsync(second);

        Assert.Equal(firstAttempt.DeliveryId, replay.DeliveryId);
        Assert.Equal("replay-me", replay.Message);
    }

    [Fact]
    public async Task Acknowledged_message_is_not_replayed_after_reconnect()
    {
        using var fixture = new RegistryFixture();
        var first = await fixture.Registry.SubscribeAsync("durable", TestContext.Current.CancellationToken);
        await fixture.AddMessageAsync("ack-me");
        fixture.Registry.NotifyMessageAvailable();
        var acknowledged = await ReadOneAsync(first);
        Assert.True(await first.TryReserveAsync(
            acknowledged.DeliveryId, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.True(await first.AcknowledgeAsync(
            acknowledged.DeliveryId, TestContext.Current.CancellationToken));
        fixture.Registry.Unsubscribe(first);

        var second = await fixture.Registry.SubscribeAsync("durable", TestContext.Current.CancellationToken);
        await fixture.AddMessageAsync("next");
        fixture.Registry.NotifyMessageAvailable();
        var delivery = await ReadOneAsync(second);

        Assert.Equal("next", delivery.Message);
    }

    [Fact]
    public async Task Two_subscribers_maintain_independent_cursors()
    {
        using var fixture = new RegistryFixture();
        var first = await fixture.Registry.SubscribeAsync("first", TestContext.Current.CancellationToken);
        var second = await fixture.Registry.SubscribeAsync("second", TestContext.Current.CancellationToken);
        await fixture.AddMessageAsync("shared");
        fixture.Registry.NotifyMessageAvailable();
        var firstDelivery = await ReadOneAsync(first);
        var secondDelivery = await ReadOneAsync(second);
        Assert.Equal(firstDelivery.DeliveryId, secondDelivery.DeliveryId);

        Assert.True(await first.TryReserveAsync(
            firstDelivery.DeliveryId, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.True(await first.AcknowledgeAsync(
            firstDelivery.DeliveryId, TestContext.Current.CancellationToken));
        fixture.Registry.Unsubscribe(first);
        fixture.Registry.Unsubscribe(second);

        var reconnectedSecond = await fixture.Registry.SubscribeAsync(
            "second", TestContext.Current.CancellationToken);
        var replay = await ReadOneAsync(reconnectedSecond);
        Assert.Equal(secondDelivery.DeliveryId, replay.DeliveryId);
    }

    [Fact]
    public async Task Duplicate_live_subscriber_identity_is_rejected()
    {
        using var fixture = new RegistryFixture();
        _ = await fixture.Registry.SubscribeAsync("duplicate", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SubscriberAlreadyConnectedException>(() =>
            fixture.Registry.SubscribeAsync("duplicate", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delivery_pauses_once_the_outstanding_budget_is_exhausted()
    {
        using var fixture = new RegistryFixture(maxOutstanding: 2);
        var subscription = await fixture.Registry.SubscribeAsync("budget", TestContext.Current.CancellationToken);
        var instant = TimeSpan.FromMilliseconds(50);

        Assert.True(await subscription.TryReserveAsync("1", instant, TestContext.Current.CancellationToken));
        Assert.True(await subscription.TryReserveAsync("2", instant, TestContext.Current.CancellationToken));
        Assert.False(await subscription.TryReserveAsync("3", instant, TestContext.Current.CancellationToken));
        Assert.True(await subscription.AcknowledgeAsync("1", TestContext.Current.CancellationToken));
        Assert.True(await subscription.TryReserveAsync("3", instant, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Acknowledgements_must_advance_in_delivery_order()
    {
        using var fixture = new RegistryFixture(maxOutstanding: 2);
        var subscription = await fixture.Registry.SubscribeAsync("ordered", TestContext.Current.CancellationToken);
        var instant = TimeSpan.FromMilliseconds(50);

        Assert.True(await subscription.TryReserveAsync("1", instant, TestContext.Current.CancellationToken));
        Assert.True(await subscription.TryReserveAsync("2", instant, TestContext.Current.CancellationToken));
        Assert.False(await subscription.AcknowledgeAsync("2", TestContext.Current.CancellationToken));
        Assert.True(await subscription.AcknowledgeAsync("1", TestContext.Current.CancellationToken));
        Assert.True(await subscription.AcknowledgeAsync("2", TestContext.Current.CancellationToken));
        Assert.False(await subscription.AcknowledgeAsync("2", TestContext.Current.CancellationToken));
    }

    private static async Task<InboundDelivery> ReadOneAsync(InboundSubscription subscription)
    {
        await using var enumerator = subscription
            .ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        return enumerator.Current;
    }

    private sealed class RegistryFixture : IDisposable
    {
        private readonly SignalizrMetrics _metrics = new();
        private readonly TestDbContextFactory _dbContextFactory = new();

        public RegistryFixture(int maxOutstanding = 4)
        {
            Registry = new InboundSubscriberRegistry(
                NullLogger<InboundSubscriberRegistry>.Instance,
                Options.Create(new SubscriberConfig
                {
                    ReplayBatchSize = 10,
                    MaxOutstanding = maxOutstanding
                }),
                TimeProvider.System,
                _metrics,
                _dbContextFactory);
        }

        public InboundSubscriberRegistry Registry { get; }

        public async Task AddMessageAsync(string message)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var nextMessageId = await dbContext.InboundMessages
                .Select(candidate => (long?)candidate.Id)
                .MaxAsync() + 1 ?? 1;
            dbContext.InboundMessages.Add(new InboundMessageEntity
            {
                Id = nextMessageId,
                Message = message,
                PersistedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        public void Dispose() => _metrics.Dispose();
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