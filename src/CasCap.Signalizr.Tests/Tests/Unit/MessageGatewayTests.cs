using CasCap.Exceptions;
using CasCap.Models;
using CasCap.Services;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers the translation from a channel message onto the upstream send contract, which is where
/// the gateway decides what a caller can and cannot reach.
/// </summary>
public class MessageGatewayTests
{
    private const string Number = "+10000000000";
    private const string GroupId = "group.dGVzdA==";
    private const string Text = "hello";

    [Fact]
    public void CreateRequest_AddressesTheGroupAndNothingElse()
    {
        var request = MessageGateway.CreateRequest(Number, GroupId, Text);

        Assert.Equal(Number, request.Number);
        Assert.Equal(Text, request.Message);
        Assert.Equal([GroupId], request.Recipients);
    }

    [Fact]
    public void CreateRequest_LeavesEveryOptionalUpstreamFieldUnset()
    {
        var request = MessageGateway.CreateRequest(Number, GroupId, Text);

        // The gateway owns the account, so a caller cannot reach these. If one ever becomes
        // settable it must be a deliberate contract change rather than a default that drifted in.
        Assert.Null(request.Base64Attachments);
        Assert.Null(request.TextMode);
        Assert.Null(request.LinkPreview);
        Assert.Null(request.Mentions);
        Assert.Null(request.NotifySelf);
        Assert.Null(request.Sticker);
        Assert.Null(request.ViewOnce);
        Assert.Null(request.EditTimestamp);
        Assert.Null(request.QuoteAuthor);
        Assert.Null(request.QuoteMessage);
        Assert.Null(request.QuoteTimestamp);
        Assert.Null(request.QuoteMentions);
    }

    [Theory]
    [InlineData(Text)]
    [InlineData("multi\nline")]
    [InlineData("  leading and trailing  ")]
    [InlineData("emoji and punctuation: hello, world!")]
    public void CreateRequest_PassesTheMessageThroughVerbatim(string message)
    {
        // A gateway translates addressing, not content: trimming or normalising here would be
        // invisible to the caller and impossible to opt out of.
        var request = MessageGateway.CreateRequest(Number, GroupId, message);

        Assert.Equal(message, request.Message);
    }

    [Fact]
    public void CreateRequest_GivesEachRequestItsOwnTrackingId()
    {
        var first = MessageGateway.CreateRequest(Number, GroupId, Text);
        var second = MessageGateway.CreateRequest(Number, GroupId, Text);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("data:image/png;filename=chart.png;base64,dGVzdA==")]
    [InlineData("data:audio/ogg;filename=reply.ogg;base64,dGVzdA==")]
    public void CreateRequest_PassesBinaryAttachmentsThroughVerbatim(string attachment)
    {
        string[] attachments = [attachment];

        var request = MessageGateway.CreateRequest(Number, GroupId, Text, attachments);

        Assert.Equal(attachments, request.Base64Attachments);
    }

    [Fact]
    public void UnknownChannelException_NamesTheChannel()
    {
        var exception = new UnknownChannelException("nope");

        Assert.Equal("nope", exception.ChannelName);
        Assert.Contains("nope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetReactionTarget_UsesTheDeliveredSender()
    {
        var (author, timestamp) = MessageGateway.GetReactionTarget("+10000000001", fromSelf: false, 42, Number);

        Assert.Equal("+10000000001", author);
        Assert.Equal(42, timestamp);
    }

    [Fact]
    public void GetReactionTarget_UsesTheAccountForTheGatewaysOwnMessage()
    {
        // A sync message's sender is the account's own device, so the account is the author.
        var (author, _) = MessageGateway.GetReactionTarget("+10000000001", fromSelf: true, 42, Number);

        Assert.Equal(Number, author);
    }

    [Fact]
    public void GetReactionTarget_RejectsADeliveryWithoutATimestamp()
        => Assert.Throws<InvalidOperationException>(
            () => MessageGateway.GetReactionTarget("+10000000001", fromSelf: false, timestamp: null, Number));
}
