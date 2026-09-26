namespace CasCap.Services;

/// <summary>Prepares the gateway surface: named-channel resolution and account profile policy.</summary>
/// <remarks>
/// Stateless, so every replica runs it. The REST channel surface itself is served by the
/// feature-gated controllers once channels resolve.
/// </remarks>
public sealed class GatewayBgService(
    ILogger<GatewayBgService> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<GatewayConfig> gatewayConfig,
    ISignalCliClient client,
    IChannelResolver channels) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Gateway;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await ResolveChannelsAsync(cancellationToken).ConfigureAwait(false);
        await ApplyProfileAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("{ClassName} started with {ClientType}",
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

    /// <summary>Applies the configured profile display name, when one is configured.</summary>
    /// <remarks>
    /// A failure is logged rather than thrown: the display name is cosmetic, and the channel
    /// surface must not go down because of it.
    /// </remarks>
    private async Task ApplyProfileAsync(CancellationToken cancellationToken)
    {
        if (gatewayConfig.Value.ProfileName is not { Length: > 0 } profileName)
            return;

        try
        {
            var updated = await client.UpdateProfile(signalCliConfig.Value.PhoneNumber,
                new UpdateProfileRequest { Name = profileName }, cancellationToken).ConfigureAwait(false);
            if (updated)
                logger.LogInformation("{ClassName} applied the configured profile name", nameof(GatewayBgService));
            else
                logger.LogWarning("{ClassName} the wrapper rejected the profile name", nameof(GatewayBgService));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{ClassName} could not apply the profile name", nameof(GatewayBgService));
        }
    }
}
