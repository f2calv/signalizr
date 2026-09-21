namespace CasCap.Services;

/// <summary>Owns the single inbound receive stream and fans messages out to subscribers.</summary>
/// <remarks>
/// Exactly one instance may be active: the upstream broadcast is an unbuffered channel with a
/// non-blocking send, so a second reader causes duplicate delivery while a slow reader causes silent
/// loss. The read loop must drain straight into a buffered queue and do no inline work.
/// Not yet implemented; the service currently idles.
/// </remarks>
// TODO: drain the upstream receive stream into a bounded channel, then dispatch from a separate consumer.
public sealed class ReceiverBgService(ILogger<ReceiverBgService> logger) : IBgFeature
{
    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Receiver;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{ClassName} started (no receive stream implemented yet)", nameof(ReceiverBgService));
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
