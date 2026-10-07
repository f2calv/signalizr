using CasCap.Models;
using CasCap.Models.Dtos;
using CasCap.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CasCap.Tests;

/// <summary>
/// Covers group-name resolution, whose failure modes decide whether a message reaches the
/// intended group or a different one.
/// </summary>
public class GroupResolverTests
{
    private static SignalGroup Group(string id, string name)
        => new() { Id = id, Name = name };

    [Fact]
    public void Resolve_UsesExactSignalGroupName()
    {
        var resolved = GroupResolver.Resolve(
            ["Ops Alerts"],
            [Group("group.aaa", "Ops Alerts"), Group("group.bbb", "Something Else")]);

        Assert.Equal("group.aaa", resolved["Ops Alerts"]);
        Assert.False(resolved.ContainsKey("alerts"));
    }

    [Fact]
    public void Resolve_PreservesCaseAndSpaces()
    {
        var resolved = GroupResolver.Resolve(
            ["Ops Alerts"],
            [Group("group.aaa", "Ops Alerts")]);

        Assert.False(resolved.ContainsKey("ops alerts"));
        Assert.Equal(["Ops Alerts"], resolved.Keys);
    }

    [Fact]
    public void Resolve_ThrowsWhenTwoGroupsShareTheConfiguredName()
    {
        // Signal permits duplicate group names, so first-match would silently route to whichever
        // the wrapper happened to list first - and nothing downstream could detect it.
        var exception = Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve(
            ["Ops Alerts"],
            [Group("group.aaa", "Ops Alerts"), Group("group.bbb", "Ops Alerts")]));

        Assert.Contains("matches 2 groups", exception.Message);
    }

    [Fact]
    public void Resolve_ThrowsWhenNoGroupMatches()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve(
            ["Ops Alerts"],
            [Group("group.bbb", "Something Else")]));

        Assert.Contains("matches no group", exception.Message);
    }

    [Fact]
    public void Resolve_ComparesGroupNamesOrdinally()
    {
        // A near-match is far more likely to be a different group than a spelling variant.
        Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve(
            ["Ops Alerts"],
            [Group("group.aaa", "ops alerts")]));
    }

    [Fact]
    public void Resolve_ReportsEveryFailureAtOnce()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve(
            ["Missing One", "Missing Two"],
            []));

        Assert.Equal(2, exception.Message.Split("matches no group").Length - 1);
        Assert.DoesNotContain("Missing One", exception.Message);
    }

    [Fact]
    public void Resolve_ReturnsEmptyWhenNoGroupsAreConfigured()
    {
        Assert.Empty(GroupResolver.Resolve(
            [], [Group("group.aaa", "Ops Alerts")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_RejectsBlankNames(string name)
        => Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve([name], []));

    [Fact]
    public void Resolve_RejectsDuplicateConfiguredNames()
        => Assert.Throws<InvalidOperationException>(() => GroupResolver.Resolve(
            ["Ops Alerts", "Ops Alerts"], [Group("group.aaa", "Ops Alerts")]));

    [Fact]
    public void Resolve_DistinguishesGroupsDifferingOnlyByCase()
    {
        var resolved = GroupResolver.Resolve(
            ["Ops Alerts", "ops alerts"],
            [Group("group.aaa", "Ops Alerts"), Group("group.bbb", "ops alerts")]);

        Assert.Equal("group.aaa", resolved["Ops Alerts"]);
        Assert.Equal("group.bbb", resolved["ops alerts"]);
    }

    [Fact]
    public void Configuration_RejectsUnknownProperties()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["UnexpectedSetting"] = "unexpected" }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            configuration.Get<GroupConfig>(options => options.ErrorOnUnknownConfiguration = true));
    }

    [Fact]
    public void Configuration_BindsIndexedGroupNames()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["GroupNames:0"] = "My Test Group Name",
                ["GroupNames:1"] = "My Test Monitor Group Name"
            }).Build();

        var config = configuration.Get<GroupConfig>(options => options.ErrorOnUnknownConfiguration = true);

        Assert.NotNull(config);
        Assert.Equal(["My Test Group Name", "My Test Monitor Group Name"], config.GroupNames);
    }
}
