namespace CasCap.Services;

/// <summary>Hosts the gateway surface — REST send, gRPC subscription and named-channel resolution.</summary>
/// <remarks>
/// Stateless, so every replica runs it. Not yet implemented; the service currently idles so that the
/// feature-flag host can be exercised end to end.
/// </remarks>
// TODO: implement the REST send endpoint and the gRPC bidirectional subscription surface.
public sealed class GatewayBgService(ILogger<GatewayBgService> logger) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Gateway;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} started (no surface implemented yet)", nameof(GatewayBgService));
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
