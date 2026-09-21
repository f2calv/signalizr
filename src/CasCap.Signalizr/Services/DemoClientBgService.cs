namespace CasCap.Services;

/// <summary>Sample thin client used to evaluate a running gateway.</summary>
/// <remarks>
/// Ships inside the product image and is off by default, so that one image can serve both roles.
/// It must stay small and must never acquire its own Signal dependency or an inbound surface — it
/// needs only a gateway base address. Not yet implemented; the service currently idles.
/// </remarks>
// TODO: send on a timer through the gateway and print messages received back over the subscription.
public sealed class DemoClientBgService(ILogger<DemoClientBgService> logger) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.DemoClient;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} started (no gateway calls implemented yet)", nameof(DemoClientBgService));
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
