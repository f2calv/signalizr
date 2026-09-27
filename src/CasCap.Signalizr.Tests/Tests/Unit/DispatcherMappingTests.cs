using CasCap.Models.Dtos;
using CasCap.Services;
using CasCap.Tests.Fakes;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers the mapping from an upstream message onto a delivery, which is where an inbound group id
/// becomes a group name and where an unknown group must stay harmless.
/// </summary>
public class DispatcherMappingTests
{
    private const string GroupId = "group.dGVzdA==";
    private const string Account = "+19999999999";

    private static readonly FakeGroupResolver Resolver = new(new() { [GroupId] = "system" });

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
    public void A_message_from_a_configured_group_carries_the_group_name()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Equal("system", delivery.GroupName);
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

        var delivery = DispatcherBgService.CreateDelivery(message, Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Equal("system", delivery.GroupName);
        Assert.Equal("typed on my phone", delivery.Message);
        Assert.Equal(99, delivery.Timestamp);
        Assert.True(delivery.FromSelf);
    }

    [Fact]
    public void A_message_from_another_sender_is_not_marked_as_the_gateways_own()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.False(delivery.FromSelf);
    }

    [Fact]
    public void A_data_message_from_the_gateway_account_is_marked_as_its_own()
    {
        var message = new SignalReceivedMessage
        {
            Envelope = new SignalEnvelope
            {
                SourceNumber = Account,
                Timestamp = 1,
                DataMessage = new SignalDataMessage
                {
                    Message = "echo",
                    Timestamp = 7,
                    GroupInfo = new SignalGroupInfo { GroupId = GroupId }
                }
            }
        };

        var delivery = DispatcherBgService.CreateDelivery(message, Resolver, Account);

        Assert.NotNull(delivery);
        Assert.True(delivery.FromSelf);
    }

    [Fact]
    public void A_poll_vote_is_delivered_with_its_poll_and_selection()
    {
        var message = new SignalReceivedMessage
        {
            Envelope = new SignalEnvelope
            {
                Source = "+10000000000",
                Timestamp = 1,
                DataMessage = new SignalDataMessage
                {
                    Timestamp = 50,
                    GroupInfo = new SignalGroupInfo { GroupId = GroupId },
                    PollVote = new SignalPollUpdateMessage { TargetSentTimestamp = 40, OptionIndexes = [0, 2] }
                }
            }
        };

        var delivery = DispatcherBgService.CreateDelivery(message, Resolver, Account);

        Assert.NotNull(delivery);
        Assert.NotNull(delivery.PollVote);
        Assert.Equal(40, delivery.PollVote.PollTimestamp);
        Assert.Equal([0, 2], delivery.PollVote.OptionIndexes);
    }

    [Fact]
    public void An_ordinary_message_carries_no_poll_vote()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Null(delivery.PollVote);
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

        Assert.Null(DispatcherBgService.CreateDelivery(message, Resolver, Account));
    }

    [Fact]
    public void A_message_from_an_unconfigured_group_has_no_group()
    {
        // Normal, not an error: the account may belong to groups this deployment ignores.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage("group.b3RoZXI="), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Null(delivery.GroupName);
        Assert.Equal("hello", delivery.Message);
    }

    [Fact]
    public void A_direct_message_has_no_group()
    {
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(groupId: null), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Null(delivery.GroupName);
    }

    [Fact]
    public void The_delivery_id_is_assigned_after_persistence_not_mapping()
    {
        // Left empty on purpose: EF assigns the durable sequence during persistence.
        var delivery = DispatcherBgService.CreateDelivery(CreateMessage(GroupId), Resolver, Account);

        Assert.NotNull(delivery);
        Assert.Equal(string.Empty, delivery.DeliveryId);
    }

    [Fact]
    public void Inbound_lookup_requires_the_exact_group_name()
    {
        var group = new SignalGroup { Id = GroupId, Name = "system" };
        var resolved = new Dictionary<string, string>
        {
            ["zulu"] = GroupId,
            ["alpha"] = GroupId
        };

        Assert.Empty(GroupResolver.BuildInboundLookup(resolved, [group]));
    }

    [Fact]
    public void Inbound_lookup_matches_both_group_identifier_forms()
    {
        const string InternalId = "dGVzdA==";
        var groups = new List<SignalGroup>
        {
            new() { Id = GroupId, Name = "CasCap.Signalizr System", InternalId = InternalId }
        };
        var resolved = GroupResolver.Resolve(["CasCap.Signalizr System"], groups);

        var lookup = GroupResolver.BuildInboundLookup(resolved, groups);

        var entry = Assert.Single(lookup);
        Assert.True(entry.Key.Matches(GroupId));
        Assert.True(entry.Key.Matches(InternalId));
        Assert.Equal("CasCap.Signalizr System", entry.Value);
    }
}
