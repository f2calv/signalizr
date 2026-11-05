using CasCap.Common.Abstractions;
using CasCap.Signalizr.Client;
using CasCap.Signalizr.Client.Exceptions;

namespace CasCap.Services;

/// <summary>Sample thin client used to evaluate a running gateway.</summary>
/// <remarks>
/// Ships inside the product image and is off by default, so one image serves every role. It has no
/// Signal dependency and no inbound surface of its own — only a gateway address — which is what
/// keeps shipping it inside the product image justified.
/// </remarks>
public sealed partial class DemoClientBgService(
    ILogger<DemoClientBgService> logger, ISignalizrClient client, TimeProvider timeProvider) : IBgFeature
{
    private int _consecutiveFailures;
    private DateTimeOffset _failingSince;

    /// <inheritdoc/>
    public string FeatureName => FeatureNames.DemoClient;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} started", nameof(DemoClientBgService));

        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await LogGroupsAsync(cancellationToken).ConfigureAwait(false);

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
                OnGatewayFailed(ex, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} stopped", nameof(DemoClientBgService));
    }

    /// <summary>Reports the gateway's groups, which is diagnostic rather than required.</summary>
    /// <remarks>
    /// Send and subscribe are served by different roles and may live on different deployments, so
    /// a gateway without the send surface must not stop the demo subscribing.
    /// </remarks>
    private async Task LogGroupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var groups = await client.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
            // Retries while the subscription is failing would otherwise repeat this line every attempt.
            var level = _consecutiveFailures == 0 ? LogLevel.Information : LogLevel.Debug;
            if (logger.IsEnabled(level))
                logger.Log(level, "{ClassName} gateway has {Count} configured Signal group(s)",
                    nameof(DemoClientBgService), groups.Count);
        }
        catch (SignalizrRoleNotEnabledException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("{ClassName} this gateway serves no send surface, subscribing only",
                    nameof(DemoClientBgService));
        }
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in client.SubscribeAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_consecutiveFailures != 0)
                OnGatewayRecovered();
            LogMessageReceived(logger, nameof(DemoClientBgService), message.Message?.Length ?? 0);
        }

        if (_consecutiveFailures != 0)
            OnGatewayRecovered();
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("{ClassName} subscription ended, resubscribing", nameof(DemoClientBgService));
    }

    private void OnGatewayFailed(Exception exception, TimeSpan delay)
    {
        if (_consecutiveFailures++ == 0)
        {
            _failingSince = timeProvider.GetUtcNow();
            LogGatewayFailing(logger, exception, nameof(DemoClientBgService), delay);
        }
        else
            LogGatewayStillFailing(logger, nameof(DemoClientBgService), _consecutiveFailures, delay);
    }

    private void OnGatewayRecovered()
    {
        var downtime = timeProvider.GetUtcNow() - _failingSince;
        LogGatewayRecovered(logger, nameof(DemoClientBgService), _consecutiveFailures, downtime);
        _consecutiveFailures = 0;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{ClassName} received {Length} character(s)")]
    private static partial void LogMessageReceived(ILogger logger, string className, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} could not reach the gateway, retrying in {Delay}")]
    private static partial void LogGatewayFailing(ILogger logger, Exception exception, string className, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "{ClassName} gateway still unreachable ConsecutiveFailures={ConsecutiveFailures}, retrying in {Delay}")]
    private static partial void LogGatewayStillFailing(ILogger logger, string className, int consecutiveFailures, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{ClassName} gateway reachable again ConsecutiveFailures={ConsecutiveFailures} Downtime={Downtime}")]
    private static partial void LogGatewayRecovered(ILogger logger, string className, int consecutiveFailures, TimeSpan downtime);
}
