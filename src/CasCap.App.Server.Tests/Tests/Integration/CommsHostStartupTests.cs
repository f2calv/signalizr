using CasCap.Abstractions;
using CasCap.Common.Abstractions;
using CasCap.Services;

namespace CasCap.IntegrationTests;

/// <summary>Credential-free startup checks for a host running only the Comms responder role.</summary>
[Trait("Category", "Integration")]
public sealed class CommsHostStartupTests(CommsWebApplicationFactory factory)
    : IClassFixture<CommsWebApplicationFactory>
{
    [Fact]
    public void Services_RegisterTheAgentResponderWithoutGatewayRoles()
    {
        var features = factory.Services.GetServices<IBgFeature>().Select(feature => feature.FeatureName).ToList();

        Assert.Contains(FeatureNames.Comms, features);
        Assert.DoesNotContain(FeatureNames.Gateway, features);
        Assert.DoesNotContain(FeatureNames.Receiver, features);
        Assert.IsType<AgentCommsResponder>(factory.Services.GetRequiredService<ICommsResponder>());
        Assert.False(factory.Services.GetRequiredService<IOptions<CommsConfig>>().Value.StreamConsumptionEnabled);
    }

    [Fact]
    public async Task StartupHealthEndpoint_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz/startup", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
