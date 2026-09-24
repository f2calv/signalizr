using CasCap.Common.Abstractions;
using CasCap.Constants;
using CasCap.Signalizr.Client;
using CasCap.Signalizr.Client.Exceptions;

namespace CasCap.Services;

/// <summary>Sample thin client used to evaluate a running gateway.</summary>
/// <remarks>
/// Ships inside the product image and is off by default, so one image serves every role. It has no
/// Signal dependency and no inbound surface of its own — only a gateway address — which is what
/// keeps shipping it inside the product image justified.
/// </remarks>
public sealed class DemoClientBgService(
    ILogger<DemoClientBgService> logger, ISignalizrClient client) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.DemoClient;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} started", nameof(DemoClientBgService));

        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await LogChannelsAsync(cancellationToken).ConfigureAwait(false);

                // The client does not resubscribe on its own, so the reconnect lives here where it
                // is visible: a silent one would hide the gap in delivery it creates.
                await SubscribeAsync(cancellationToken).ConfigureAwait(false);

                delay = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{ClassName} could not reach the gateway, retrying in {Delay}",
                    nameof(DemoClientBgService), delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }

        logger.LogInformation("{ClassName} stopped", nameof(DemoClientBgService));
    }

    /// <summary>Reports the gateway's channels, which is diagnostic rather than required.</summary>
    /// <remarks>
    /// Send and subscribe are served by different roles and may live on different deployments, so
    /// a gateway without the send surface must not stop the demo subscribing.
    /// </remarks>
    private async Task LogChannelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var channels = await client.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{ClassName} gateway has {Count} channel(s): {Channels}",
                nameof(DemoClientBgService), channels.Count, string.Join(", ", channels));
        }
        catch (SignalizrRoleNotEnabledException)
        {
            logger.LogInformation("{ClassName} this gateway serves no send surface, subscribing only",
                nameof(DemoClientBgService));
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in client.SubscribeAsync(cancellationToken).ConfigureAwait(false))
        {
            // Channel and length only. The body is the sender's content and the sender is personal
            // data, so neither belongs in a log, even in a demo.
            logger.LogInformation("{ClassName} received {Length} character(s) on channel {Channel}",
                nameof(DemoClientBgService), message.Message?.Length ?? 0, message.Channel ?? "(none)");
        }

        logger.LogInformation("{ClassName} subscription ended, resubscribing", nameof(DemoClientBgService));
    }
}
