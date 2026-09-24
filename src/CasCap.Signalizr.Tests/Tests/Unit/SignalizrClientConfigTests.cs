using CasCap.Signalizr.Client;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace CasCap.Tests;

/// <summary>Verifies durable subscriber identity configuration.</summary>
public sealed class SignalizrClientConfigTests
{
    [Fact]
    public void DefaultSubscriberName_IsInvalid()
    {
        var config = new SignalizrClientConfig();
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(config, new ValidationContext(config), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(SignalizrClientConfig.SubscriberName)));
    }
}
