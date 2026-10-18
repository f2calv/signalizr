using CasCap.Extensions;
using Microsoft.Extensions.Hosting;

namespace CasCap.Tests;

/// <summary>Covers the opt-in Key Vault source for the Comms responder's Agent Runtime identity.</summary>
public sealed class HostApplicationBuilderExtensionsTests
{
    [Theory]
    [InlineData("AppConfig:AgentRuntimeKeyVaultPrefix", "AgentRuntime--Example--Exec")]
    [InlineData("AppConfig:KeyVaultSecretPrefixes:0", "CasCap--DatabaseConfig")]
    public void InitializeConfiguration_KeyVaultPrefixWithoutVault_FailsClosed(string key, string prefix)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = prefix,
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.InitializeConfiguration(typeof(HostApplicationBuilderExtensions).Assembly));

        Assert.Contains("AppConfig:KeyVaultName", exception.Message);
    }
}
