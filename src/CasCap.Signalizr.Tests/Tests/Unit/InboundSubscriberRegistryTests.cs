using CasCap.Exceptions;
using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers fan-out to subscribers. The rules that matter are that one subscriber cannot acknowledge
/// another's message, and that a subscriber which cannot keep up is reported rather than ignored.
/// </summary>
public class InboundSubscriberRegistryTests
{
    private const string FirstDeliveryId = "first";

    private static InboundSubscriberRegistry CreateRegistry(int queueCapacity = 10, int maxOutstanding = 4)
        => new(NullLogger<InboundSubscriberRegistry>.Instance,
            Options.Create(new SubscriberConfig { QueueCapacity = queueCapacity, MaxOutstanding = maxOutstanding }));

    private static InboundDelivery CreateDelivery()
        => new() { DeliveryId = string.Empty, Channel = "system", Message = "hello" };

    [Fact]
    public void Subscribing_and_unsubscribing_tracks_the_count()
    {
        var registry = CreateRegistry();
        Assert.Equal(0, registry.Count);

        var first = registry.Subscribe("a");
        var second = registry.Subscribe("b");
        Assert.Equal(2, registry.Count);

        registry.Unsubscribe(first);
        Assert.Equal(1, registry.Count);

        // Unsubscribing twice is harmless: a stream can end and be cleaned up from both sides.
        registry.Unsubscribe(first);
        Assert.Equal(1, registry.Count);

        registry.Unsubscribe(second);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task Every_subscriber_receives_the_message_with_its_own_delivery_id()
    {
        var registry = CreateRegistry();
        var first = registry.Subscribe("a");
        var second = registry.Subscribe("b");

        Assert.Empty(registry.Broadcast(CreateDelivery()));

        var firstDelivery = await ReadOneAsync(first);
        var secondDelivery = await ReadOneAsync(second);

        Assert.Equal("hello", firstDelivery.Message);
        Assert.Equal("hello", secondDelivery.Message);

        // Distinct ids, so one subscriber acknowledging cannot clear the other's outstanding work.
        Assert.NotEqual(firstDelivery.DeliveryId, secondDelivery.DeliveryId);
        Assert.NotEmpty(firstDelivery.DeliveryId);
    }

    [Fact]
    public void A_subscriber_that_cannot_keep_up_is_reported_rather_than_dropped()
    {
        var registry = CreateRegistry(queueCapacity: 2);
        var subscription = registry.Subscribe("slow");

        Assert.Empty(registry.Broadcast(CreateDelivery()));
        Assert.Empty(registry.Broadcast(CreateDelivery()));

        // The third has nowhere to go. It must surface as a failed subscriber, not vanish.
        var failed = registry.Broadcast(CreateDelivery());

        Assert.Same(subscription, Assert.Single(failed));
    }

    [Fact]
    public async Task Failed_unsubscribe_propagates_the_reason_to_the_subscriber()
    {
        var registry = CreateRegistry();
        var subscription = registry.Subscribe("slow");
        var error = new SubscriberFellBehindException(subscription.Name);

        registry.Unsubscribe(subscription, error);

        await using var reader = subscription.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<SubscriberFellBehindException>(
            () => reader.MoveNextAsync().AsTask());
        Assert.Same(error, exception);
    }

    [Fact]
    public void Broadcasting_with_no_subscribers_is_not_an_error()
    {
        var registry = CreateRegistry();

        Assert.Empty(registry.Broadcast(CreateDelivery()));
    }

    [Fact]
    public async Task Delivery_pauses_once_the_outstanding_budget_is_exhausted()
    {
        var registry = CreateRegistry(maxOutstanding: 2);
        var subscription = registry.Subscribe("a");
        var instant = TimeSpan.FromMilliseconds(50);

        Assert.True(await subscription.TryReserveAsync(FirstDeliveryId, instant, TestContext.Current.CancellationToken));
        Assert.True(await subscription.TryReserveAsync("second", instant, TestContext.Current.CancellationToken));

        // Two delivered and none acknowledged: the third must wait rather than pile on more work.
        Assert.False(await subscription.TryReserveAsync("third", instant, TestContext.Current.CancellationToken));

        Assert.True(subscription.Acknowledge(FirstDeliveryId));

        Assert.True(await subscription.TryReserveAsync("third", instant, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_and_duplicate_acknowledgements_do_not_release_other_deliveries()
    {
        var registry = CreateRegistry(maxOutstanding: 2);
        var subscription = registry.Subscribe("confused");
        var instant = TimeSpan.FromMilliseconds(50);

        Assert.True(await subscription.TryReserveAsync(FirstDeliveryId, instant, TestContext.Current.CancellationToken));
        Assert.True(await subscription.TryReserveAsync("second", instant, TestContext.Current.CancellationToken));

        Assert.True(subscription.Acknowledge(FirstDeliveryId));
        Assert.False(subscription.Acknowledge(FirstDeliveryId));
        Assert.False(subscription.Acknowledge("unknown"));

        Assert.True(await subscription.TryReserveAsync("third", instant, TestContext.Current.CancellationToken));
        Assert.False(await subscription.TryReserveAsync("fourth", instant, TestContext.Current.CancellationToken));
    }

    private static async Task<InboundDelivery> ReadOneAsync(InboundSubscription subscription)
    {
        var enumerator = subscription.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            return enumerator.Current;
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
