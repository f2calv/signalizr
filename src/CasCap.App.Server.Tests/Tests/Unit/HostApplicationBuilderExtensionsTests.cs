using CasCap.Extensions;
using Microsoft.Extensions.Hosting;

namespace CasCap.Tests;

/// <summary>Covers the opt-in Key Vault source for the Comms responder's Agent Runtime identity.</summary>
public sealed class HostApplicationBuilderExtensionsTests
{
    [Fact]
    public void InitializeConfiguration_KeyVaultPrefixWithoutVault_FailsClosed()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppConfig:AgentRuntimeKeyVaultPrefix"] = "AgentRuntime--Example--Exec",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.InitializeConfiguration(typeof(HostApplicationBuilderExtensions).Assembly));

        Assert.Contains("AppConfig:KeyVaultName", exception.Message);
    }
}
