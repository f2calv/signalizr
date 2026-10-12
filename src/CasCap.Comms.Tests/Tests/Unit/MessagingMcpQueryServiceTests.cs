namespace CasCap.Tests.Unit;

/// <summary>Verifies the poll tools can be activated the way the MCP server activates tool types.</summary>
[Trait("Category", "Comms")]
public sealed class MessagingMcpQueryServiceTests
{
    [Fact]
    public async Task CreatePoll_ActivatedFromServiceProvider_UsesConfiguredGroup()
    {
        var signalizrClient = new FakeSignalizrClient();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISignalizrClient>(signalizrClient);
        services.AddSingleton(Options.Create(new CommsConfig { GroupName = "Chat Group" }));
        services.AddMessagingMcp();
        await using var provider = services.BuildServiceProvider();

        // The MCP SDK creates tool targets with ActivatorUtilities on every call rather than resolving the registration.
        var service = ActivatorUtilities.CreateInstance<MessagingMcpQueryService>(provider);
        var result = await service.CreatePoll("Lights?", "On, Off", TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(result.PollId));
        Assert.NotNull(provider.GetRequiredService<IPollTracker>().GetPoll(result.PollId));
    }
}
