namespace CasCap.Services;

/// <summary>Prepares the gateway surface: named-group resolution and account profile policy.</summary>
/// <remarks>
/// Stateless, so every replica runs it. The REST group surface itself is served by the
/// feature-gated controllers once groups resolve.
/// </remarks>
public sealed class GatewayBgService(
    ILogger<GatewayBgService> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<GatewayConfig> gatewayConfig,
    ISignalCliClient client,
    IGroupResolver groups,
    IOperatorNotifier operatorNotifier) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Gateway;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await ResolveGroupsAsync(cancellationToken).ConfigureAwait(false);
        await ApplyProfileAsync(cancellationToken).ConfigureAwait(false);
        operatorNotifier.Notify($"gateway started with {groups.GroupNames.Count} group(s): {string.Join(", ", groups.GroupNames)}");

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{ClassName} started with {ClientType}",
                nameof(GatewayBgService), client.GetType().Name);
        }
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves groups, retrying while the wrapper is unreachable.</summary>
    /// <remarks>
    /// An unreachable wrapper must not crash the process: readiness already excludes the pod from
    /// the Service while the upstream is down, and a crash loop would turn a recoverable outage
    /// into a restart storm. A configuration fault is different - retrying cannot fix a group name
    /// that is missing or ambiguous - so <see cref="InvalidOperationException"/> propagates.
    /// </remarks>
    private async Task ResolveGroupsAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await groups.RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(ex, "{ClassName} could not reach the wrapper, retrying in {Delay}",
                        nameof(GatewayBgService), delay);
                }
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    /// <summary>Applies the configured profile display name, when one is configured.</summary>
    /// <remarks>
    /// A failure is logged rather than thrown: the display name is cosmetic, and the group
    /// surface must not go down because of it.
    /// </remarks>
    private async Task ApplyProfileAsync(CancellationToken cancellationToken)
    {
        if (gatewayConfig.Value.ProfileName is not { Length: > 0 } profileName)
            return;

        try
        {
            // signal-cli splits the name on \0 into given and family name, so the trailing
            // separator clears a stale family name instead of leaving it appended.
            var updated = await client.UpdateProfile(signalCliConfig.Value.PhoneNumber,
                new UpdateProfileRequest { Name = profileName + "\0" }, cancellationToken).ConfigureAwait(false);
            if (updated)
            {
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("{ClassName} applied the configured profile name", nameof(GatewayBgService));
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Warning))
                    logger.LogWarning("{ClassName} the wrapper rejected the profile name", nameof(GatewayBgService));
            }
        }
        catch (HttpRequestException ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(ex, "{ClassName} could not apply the profile name", nameof(GatewayBgService));
        }
    }
}
