namespace CasCap.Services;

/// <summary>Owns the single inbound receive stream and drains it into a buffered queue.</summary>
/// <remarks>
/// Exactly one instance may be active. The upstream broadcast is an unbuffered channel with a
/// non-blocking send, so a second reader causes duplicate delivery while a slow reader causes
/// silent loss. Within a process this is enforced below; across pods it is the replica count,
/// which is why the receiver role must not be scaled out.
/// </remarks>
public sealed class ReceiverBgService(
    ILogger<ReceiverBgService> logger,
    ISignalCliReceiver receiver,
    IInboundMessageQueue queue) : IBgFeature
{
    private int _started;

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

        logger.LogInformation("{ClassName} draining {ReceiverType} into the inbound queue",
            nameof(ReceiverBgService), receiver.GetType().Name);

        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(cancellationToken).ConfigureAwait(false);

                // A stream that ends without cancellation means the transport closed on its own,
                // so reconnect rather than leaving the account unread.
                if (!cancellationToken.IsCancellationRequested)
                    logger.LogWarning("{ClassName} receive stream ended, reconnecting", nameof(ReceiverBgService));

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
                // restart storm.
                logger.LogWarning(ex, "{ClassName} receive stream failed, retrying in {Delay}",
                    nameof(ReceiverBgService), delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }

        queue.Complete();

        logger.LogInformation("{ClassName} stopped after {Enqueued} message(s), {Dropped} dropped",
            nameof(ReceiverBgService), queue.EnqueuedCount, queue.DroppedCount);
    }

    /// <summary>Reads the stream, doing nothing per message but queueing it.</summary>
    /// <remarks>
    /// Deliberately free of logging, resolution and dispatch. Anything done here happens while the
    /// upstream is not being read, and the upstream drops whatever arrives in that window.
    /// </remarks>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in receiver.StreamMessagesAsync(cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            queue.TryEnqueue(message);
        }
    }
}
