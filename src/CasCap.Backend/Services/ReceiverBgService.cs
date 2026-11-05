namespace CasCap.Services;

/// <summary>Owns the single inbound receive stream and drains it into a buffered queue.</summary>
/// <remarks>
/// Exactly one instance may be active. The upstream broadcast is an unbuffered channel with a
/// non-blocking send, so a second reader causes duplicate delivery while a slow reader causes
/// silent loss. Within a process this is enforced below; across pods it is the replica count,
/// which is why the receiver role must not be scaled out.
/// </remarks>
public sealed partial class ReceiverBgService(
    ILogger<ReceiverBgService> logger,
    ISignalCliReceiver receiver,
    IInboundMessageQueue queue,
    TimeProvider timeProvider) : IBgFeature
{
    private int _started;
    private int _consecutiveFailures;
    private DateTimeOffset _failingSince;

    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Receiver;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException(
                $"{nameof(ReceiverBgService)} is already running. A second reader would compete " +
                "for messages rather than receive copies of them.");
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{ClassName} draining {ReceiverType} into the inbound queue",
                nameof(ReceiverBgService), receiver.GetType().Name);
        }

        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(cancellationToken).ConfigureAwait(false);

                // A stream that ends without cancellation means the transport closed on its own,
                // so reconnect rather than leaving the account unread.
                if (!cancellationToken.IsCancellationRequested)
                    OnReceiveFailed(exception: null, delay: TimeSpan.Zero);

                delay = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transport failure is recoverable and must not crash the pod: readiness already
                // reflects an unreachable wrapper, and a crash loop would turn an outage into a
                // restart storm. Only the transition into failure is a Warning; repeats are Debug.
                OnReceiveFailed(ex, delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }

        queue.Complete();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{ClassName} stopped after {Enqueued} message(s), {Dropped} dropped",
                nameof(ReceiverBgService), queue.EnqueuedCount, queue.DroppedCount);
        }
    }

    /// <summary>Reads the stream, doing nothing per message but queueing it.</summary>
    /// <remarks>
    /// Deliberately free of per-message logging, resolution and dispatch. Anything done here happens
    /// while the upstream is not being read, and the upstream drops whatever arrives in that window.
    /// The only exception is the single recovery event after an outage, behind an integer check.
    /// </remarks>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in receiver.StreamMessagesAsync(cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (_consecutiveFailures != 0)
                OnReceiveRecovered();

            queue.TryEnqueue(message);
        }
    }

    private void OnReceiveFailed(Exception? exception, TimeSpan delay)
    {
        if (_consecutiveFailures++ == 0)
        {
            _failingSince = timeProvider.GetUtcNow();
            if (exception is null)
                LogReceiveEnded(logger, nameof(ReceiverBgService));
            else
                LogReceiveFailing(logger, exception, nameof(ReceiverBgService), delay);
        }
        else
            LogReceiveStillFailing(logger, nameof(ReceiverBgService), _consecutiveFailures, delay);
    }

    // Recovery is observed at the first delivered message: a connectionless transport has no
    // other signal that the wrapper is answering again.
    private void OnReceiveRecovered()
    {
        var downtime = timeProvider.GetUtcNow() - _failingSince;
        LogReceiveRecovered(logger, nameof(ReceiverBgService), _consecutiveFailures, downtime);
        _consecutiveFailures = 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} receive stream failed, retrying in {Delay}")]
    private static partial void LogReceiveFailing(ILogger logger, Exception exception, string className, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} receive stream ended, reconnecting")]
    private static partial void LogReceiveEnded(ILogger logger, string className);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "{ClassName} receive stream still failing ConsecutiveFailures={ConsecutiveFailures}, retrying in {Delay}")]
    private static partial void LogReceiveStillFailing(ILogger logger, string className, int consecutiveFailures, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{ClassName} receive stream recovered ConsecutiveFailures={ConsecutiveFailures} Downtime={Downtime}")]
    private static partial void LogReceiveRecovered(ILogger logger, string className, int consecutiveFailures, TimeSpan downtime);
}
