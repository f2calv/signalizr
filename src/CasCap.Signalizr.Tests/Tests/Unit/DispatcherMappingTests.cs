using CasCap.Models.Dtos;
using CasCap.Services;
using CasCap.Tests.Fakes;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers the mapping from an upstream message onto a delivery, which is where an inbound group id
/// becomes a channel name and where an unknown group must stay harmless.
/// </summary>
public class DispatcherMappingTests
{
    private const string GroupId = "group.dGVzdA==";

    private static readonly FakeChannelResolver Resolver = new(new() { [GroupId] = "system" });

    private static SignalReceivedMessage CreateMessage(string? groupId, string? text = "hello")
        => new()
        {
            Envelope = new SignalEnvelope
            {
                Source = "+10000000000",
                Timestamp = 1,
                DataMessage = new SignalDataMessage
                {
                    Message = text,
                    Timestamp = 42,
                    GroupInfo = groupId is null ? null : new SignalGroupInfo { GroupId = groupId }
                }
            }
        };

    [Fact]
    public void A_message_from_a_configured_group_carries_the_channel_name()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver);

        Assert.Equal("system", delivery.Channel);
        Assert.Equal("hello", delivery.Message);
        Assert.Equal(42, delivery.Timestamp);
        Assert.Equal("+10000000000", delivery.Sender);
    }

    [Fact]
    public void A_message_from_an_unconfigured_group_has_no_channel()
    {
        // Normal, not an error: the account may belong to groups this deployment ignores.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage("group.b3RoZXI="), Resolver);

        Assert.Null(delivery.Channel);
        Assert.Equal("hello", delivery.Message);
    }

    [Fact]
    public void A_direct_message_has_no_channel()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(groupId: null), Resolver);

        Assert.Null(delivery.Channel);
    }

    [Fact]
    public void The_delivery_id_is_assigned_during_fan_out_not_mapping()
    {
        // Left empty on purpose: the registry stamps one identifier per subscriber.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver);

        Assert.Equal(string.Empty, delivery.DeliveryId);
    }

    [Fact]
    public void Two_channel_names_for_one_group_invert_deterministically()
    {
        var resolved = new Dictionary<string, string>
        {
            ["zulu"] = GroupId,
            ["alpha"] = GroupId
        };

        // Sending to either name is unambiguous, receiving is not, so the inbound name is chosen
        // by ordinal order rather than by dictionary enumeration order.
        Assert.Equal("zulu", ChannelResolver.Invert(resolved, [])[GroupId]);
    }

    [Fact]
    public void Inversion_indexes_both_group_identifier_forms()
    {
        // GET /v1/groups returns the prefixed id, which sending needs, but an inbound message
        // carries the unprefixed internal id. Indexing only the first means nothing ever resolves.
        const string InternalId = "dGVzdA==";
        var groups = new List<SignalGroup>
        {
            new() { Id = GroupId, Name = "CasCap.Signalizr System", InternalId = InternalId }
        };
        var resolved = new Dictionary<string, string> { ["system"] = GroupId };

        var inverted = ChannelResolver.Invert(resolved, groups);

        Assert.Equal("system", inverted[GroupId]);
        Assert.Equal("system", inverted[InternalId]);
    }

    [Fact]
    public void Inversion_tolerates_a_group_without_an_internal_id()
    {
        var groups = new List<SignalGroup> { new() { Id = GroupId, Name = "system" } };
        var resolved = new Dictionary<string, string> { ["system"] = GroupId };

        var inverted = ChannelResolver.Invert(resolved, groups);

        Assert.Equal("system", inverted[GroupId]);
        Assert.Single(inverted);
    }
}
