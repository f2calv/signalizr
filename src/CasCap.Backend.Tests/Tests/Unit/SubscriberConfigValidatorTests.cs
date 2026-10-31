using CasCap.Models;
using CasCap.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasCap.Tests;

/// <summary>Covers the startup check that subscriber groups are groups the gateway serves.</summary>
public sealed class SubscriberConfigValidatorTests
{
    private static readonly SubscriberConfigValidator Validator = new(Options.Create(new GroupConfig
    {
        GroupNames = ["Example Group", "Other Group"]
    }));

    [Fact]
    public void Served_groups_pass()
    {
        var result = Validator.Validate(null, new SubscriberConfig
        {
            SubscriberGroups = new() { ["example-comms"] = ["Example Group"] }
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Unknown Group")]
    [InlineData("example group")]
    public void Unserved_or_differently_cased_group_fails(string groupName)
    {
        var result = Validator.Validate(null, new SubscriberConfig
        {
            SubscriberGroups = new() { ["example-comms"] = [groupName] }
        });

        Assert.True(result.Failed);
        Assert.DoesNotContain(groupName, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Subscriber_with_no_groups_fails()
    {
        var result = Validator.Validate(null, new SubscriberConfig
        {
            SubscriberGroups = new() { ["example-comms"] = [] }
        });

        Assert.True(result.Failed);
    }
}
