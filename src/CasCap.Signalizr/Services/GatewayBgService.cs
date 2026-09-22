namespace CasCap.Services;

/// <summary>Hosts the gateway surface — REST send, gRPC subscription and named-channel resolution.</summary>
/// <remarks>
/// Stateless, so every replica runs it. Not yet implemented; the service currently idles so that the
/// feature-flag host can be exercised end to end.
/// </remarks>
// TODO: implement the REST send endpoint and the gRPC bidirectional subscription surface.
public sealed class GatewayBgService(
    ILogger<GatewayBgService> logger, ISignalCliClient client, IChannelResolver channels) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Gateway;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await ResolveChannelsAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("{ClassName} started with {ClientType} (no surface implemented yet)",
            nameof(GatewayBgService), client.GetType().Name);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves channels, retrying while the wrapper is unreachable.</summary>
    /// <remarks>
    /// An unreachable wrapper must not crash the process: readiness already excludes the pod from
    /// the Service while the upstream is down, and a crash loop would turn a recoverable outage
    /// into a restart storm. A configuration fault is different - retrying cannot fix a group name
    /// that is missing or ambiguous - so <see cref="InvalidOperationException"/> propagates.
    /// </remarks>
    private async Task ResolveChannelsAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await channels.RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "{ClassName} could not reach the wrapper, retrying in {Delay}",
                    nameof(GatewayBgService), delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }
}
