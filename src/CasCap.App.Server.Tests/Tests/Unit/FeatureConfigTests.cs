namespace CasCap.Tests;

/// <summary>Covers the feature-name validation that gates which role a container instance runs.</summary>
public sealed class FeatureConfigTests
{
    [Fact]
    public void Bind_MissingSection_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        var config = configuration
            .GetSection(FeatureConfig.ConfigurationSectionName)
            .Get<FeatureConfig>();

        Assert.Null(config);
    }

    [Theory]
    [InlineData("Gateway", 1)]
    [InlineData("Gateway,Receiver", 2)]
    [InlineData(" gateway , RECEIVER ", 2)]
    [InlineData("DemoClient", 1)]
    [InlineData("Gateway,Receiver,Mcp", 3)]
    [InlineData("gateway,receiver,mcp", 3)]
    public void GetEnabledFeatures_ParsesKnownNames(string value, int expectedCount)
    {
        var config = new FeatureConfig { EnabledFeatures = value };

        Assert.Equal(expectedCount, config.GetEnabledFeatures().Count);
    }

    [Fact]
    public void GetEnabledFeatures_IsCaseInsensitive()
    {
        var config = new FeatureConfig { EnabledFeatures = "gateway" };

        Assert.Contains(FeatureNames.Gateway, config.GetEnabledFeatures());
    }

    [Theory]
    [InlineData("Nope")]
    [InlineData("Gateway,Nope")]
    public void GetEnabledFeatures_ThrowsOnUnknownName(string value)
    {
        var config = new FeatureConfig { EnabledFeatures = value };

        var exception = Assert.Throws<InvalidOperationException>(config.GetEnabledFeatures);
        Assert.Contains("Nope", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void GetEnabledFeatures_ThrowsWhenEmpty(string value)
    {
        var config = new FeatureConfig { EnabledFeatures = value };

        Assert.Throws<InvalidOperationException>(config.GetEnabledFeatures);
    }

    [Theory]
    [InlineData("Mcp")]
    [InlineData("Gateway,Mcp")]
    [InlineData("Receiver,Mcp")]
    [InlineData("DemoClient,Mcp")]
    public void GetEnabledFeatures_McpRequiresLiveGatewayAndReceiver(string value)
    {
        var config = new FeatureConfig { EnabledFeatures = value };

        var exception = Assert.Throws<InvalidOperationException>(config.GetEnabledFeatures);

        Assert.Contains("requires Gateway and Receiver", exception.Message);
    }

    [Fact]
    public void ValidNames_ContainsEveryDeclaredFeature()
    {
        Assert.Equal(
            [FeatureNames.DbMigrator, FeatureNames.DemoClient, FeatureNames.Gateway, FeatureNames.Mcp, FeatureNames.Receiver],
            FeatureNames.ValidNames.OrderBy(n => n, StringComparer.Ordinal));
    }
}
