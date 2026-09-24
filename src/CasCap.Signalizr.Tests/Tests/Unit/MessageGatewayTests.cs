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

    [Fact]
    public void UnknownChannelException_NamesTheChannel()
    {
        var exception = new UnknownChannelException("nope");

        Assert.Equal("nope", exception.ChannelName);
        Assert.Contains("nope", exception.Message, StringComparison.Ordinal);
    }
}
