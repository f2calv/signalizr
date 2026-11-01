using System.Threading.Channels;

namespace CasCap.Services;

/// <inheritdoc cref="IOperatorNotifier"/>
/// <remarks>
/// Also the Receiver-role background service that drains the notices, so they are sent by the one
/// process that owns the account's receive stream.
/// </remarks>
public sealed class OperatorNotifier(
    ILogger<OperatorNotifier> logger,
    IOptions<SignalCliConfig> signalCliConfig,
    IOptions<OperatorNotificationConfig> config,
    ISignalCliClient client) : IOperatorNotifier, IBgFeature
{
    private readonly Channel<string> _notices = Channel.CreateBounded<string>(
        new BoundedChannelOptions(config.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <inheritdoc/>
    public string FeatureName => FeatureNames.Receiver;

    /// <inheritdoc/>
    public void Notify(string notice)
    {
        if (config.Value.NotificationsEnabled)
            _notices.Writer.TryWrite(notice);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var notice in _notices.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var account = signalCliConfig.Value.PhoneNumber;
                var request = new SignalMessageRequest
                {
                    Number = account,
                    Recipients = [account],
                    Message = $"signalizr: {notice}"
                };
                if (await client.SendMessage(request, cancellationToken).ConfigureAwait(false) is null)
                    logger.LogWarning("{ClassName} the wrapper returned no result for an operator notice", nameof(OperatorNotifier));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A lost notice must not stop the gateway; the log and metrics remain the record.
                logger.LogWarning(ex, "{ClassName} could not send an operator notice", nameof(OperatorNotifier));
            }
        }
    }
}
