namespace CasCap.IntegrationTests;

/// <summary>Runs the server host with only the <see cref="FeatureNames.Comms"/> responder role.</summary>
public sealed class CommsWebApplicationFactory : SignalizrWebApplicationFactory
{
    /// <inheritdoc/>
    protected override string EnabledFeatures => FeatureNames.Comms;
}
