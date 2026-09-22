using CasCap.Models.Dtos;
using CasCap.Services;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers channel-name resolution, whose failure modes decide whether a message reaches the
/// intended group or a different one.
/// </summary>
public class ChannelResolverTests
{
    private static SignalGroup Group(string id, string name)
        => new() { Id = id, Name = name };

    [Fact]
    public void Resolve_MapsChannelNameToGroupId()
    {
        var resolved = ChannelResolver.Resolve(
            new Dictionary<string, string> { ["alerts"] = "Ops Alerts" },
            [Group("group.aaa", "Ops Alerts"), Group("group.bbb", "Something Else")]);

        Assert.Equal("group.aaa", resolved["alerts"]);
    }

    [Fact]
    public void Resolve_LooksUpChannelNamesCaseInsensitively()
    {
        var resolved = ChannelResolver.Resolve(
            new Dictionary<string, string> { ["Alerts"] = "Ops Alerts" },
            [Group("group.aaa", "Ops Alerts")]);

        Assert.True(resolved.ContainsKey("alerts"));
    }

    [Fact]
    public void Resolve_ThrowsWhenTwoGroupsShareTheConfiguredName()
    {
        // Signal permits duplicate group names, so first-match would silently route to whichever
        // the wrapper happened to list first - and nothing downstream could detect it.
        var exception = Assert.Throws<InvalidOperationException>(() => ChannelResolver.Resolve(
            new Dictionary<string, string> { ["alerts"] = "Ops Alerts" },
            [Group("group.aaa", "Ops Alerts"), Group("group.bbb", "Ops Alerts")]));

        Assert.Contains("matches 2 groups", exception.Message);
    }

    [Fact]
    public void Resolve_ThrowsWhenNoGroupMatches()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ChannelResolver.Resolve(
            new Dictionary<string, string> { ["alerts"] = "Ops Alerts" },
            [Group("group.bbb", "Something Else")]));

        Assert.Contains("matches no group", exception.Message);
    }

    [Fact]
    public void Resolve_ComparesGroupNamesOrdinally()
    {
        // A near-match is far more likely to be a different group than a spelling variant.
        Assert.Throws<InvalidOperationException>(() => ChannelResolver.Resolve(
            new Dictionary<string, string> { ["alerts"] = "Ops Alerts" },
            [Group("group.aaa", "ops alerts")]));
    }

    [Fact]
    public void Resolve_ReportsEveryFailureAtOnce()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ChannelResolver.Resolve(
            new Dictionary<string, string> { ["a"] = "Missing One", ["b"] = "Missing Two" },
            []));

        Assert.Contains("Missing One", exception.Message);
        Assert.Contains("Missing Two", exception.Message);
    }

    [Fact]
    public void Resolve_ReturnsEmptyWhenNoChannelsAreConfigured()
    {
        Assert.Empty(ChannelResolver.Resolve(
            new Dictionary<string, string>(), [Group("group.aaa", "Ops Alerts")]));
    }
}
