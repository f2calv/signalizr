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

        Assert.NotNull(delivery);
        Assert.Equal("system", delivery.Channel);
        Assert.Equal("hello", delivery.Message);
        Assert.Equal(42, delivery.Timestamp);
        Assert.Equal("+10000000000", delivery.Sender);
    }

    [Fact]
    public void A_message_the_account_sent_itself_is_delivered()
    {
        // The gateway is a linked device, so anything the owner types on their primary device
        // arrives as a sync rather than a data message. Ignoring these would make the gateway
        // blind to its own owner.
        var message = new SignalReceivedMessage
        {
            Envelope = new SignalEnvelope
            {
                Source = "+10000000000",
                Timestamp = 1,
                SyncMessage = new SignalSyncMessage
                {
                    SentMessage = new SignalDataMessage
                    {
                        Message = "typed on my phone",
                        Timestamp = 99,
                        GroupInfo = new SignalGroupInfo { GroupId = GroupId }
                    }
                }
            }
        };

        var delivery = DispatcherBgService.CreateDelivery(message, Resolver);

        Assert.NotNull(delivery);
        Assert.Equal("system", delivery.Channel);
        Assert.Equal("typed on my phone", delivery.Message);
        Assert.Equal(99, delivery.Timestamp);
    }

    [Fact]
    public void An_envelope_with_no_content_is_not_delivered()
    {
        // A receipt or typing indicator. Delivering it as an empty message would make every
        // consumer filter it out.
        var message = new SignalReceivedMessage
        {
            Envelope = new SignalEnvelope { Source = "+10000000000", Timestamp = 1 }
        };

        Assert.Null(DispatcherBgService.CreateDelivery(message, Resolver));
    }

    [Fact]
    public void A_message_from_an_unconfigured_group_has_no_channel()
    {
        // Normal, not an error: the account may belong to groups this deployment ignores.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage("group.b3RoZXI="), Resolver);

        Assert.NotNull(delivery);
        Assert.Null(delivery.Channel);
        Assert.Equal("hello", delivery.Message);
    }

    [Fact]
    public void A_direct_message_has_no_channel()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(groupId: null), Resolver);

        Assert.NotNull(delivery);
        Assert.Null(delivery.Channel);
    }

    [Fact]
    public void The_delivery_id_is_assigned_during_fan_out_not_mapping()
    {
        // Left empty on purpose: the registry stamps one identifier per subscriber.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver);

        Assert.NotNull(delivery);
        Assert.Equal(string.Empty, delivery.DeliveryId);
    }

    [Fact]
    public void Two_channel_names_for_one_group_resolve_inbound_deterministically()
    {
        var group = new SignalGroup { Id = GroupId, Name = "system" };
        var resolved = new Dictionary<string, string>
        {
            ["zulu"] = GroupId,
            ["alpha"] = GroupId
        };

        // Sending to either name is unambiguous, receiving is not, so the inbound name is chosen
        // by ordinal order rather than by dictionary enumeration order.
        Assert.Equal("zulu", Assert.Single(ChannelResolver.BuildInboundLookup(resolved, [group])).Value);
    }

    [Fact]
    public void Inbound_lookup_matches_both_group_identifier_forms()
    {
        const string InternalId = "dGVzdA==";
        var groups = new List<SignalGroup>
        {
            new() { Id = GroupId, Name = "CasCap.Signalizr System", InternalId = InternalId }
        };
        var resolved = new Dictionary<string, string> { ["system"] = GroupId };

        var lookup = ChannelResolver.BuildInboundLookup(resolved, groups);

        var entry = Assert.Single(lookup);
        Assert.Equal(GroupId, entry.Key.Id);
        Assert.Equal(InternalId, entry.Key.InternalId);
        Assert.Equal("system", entry.Value);
    }
}
