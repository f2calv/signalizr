namespace CasCap.Exceptions;

/// <summary>
/// Thrown when a delivery identifier matches no retained message in the addressed channel.
/// </summary>
/// <remarks>
/// Retention removes messages once every subscriber has acknowledged them and the retention window
/// has passed, so an old but once-valid identifier ends up here too.
/// </remarks>
public sealed class UnknownDeliveryException(string channelName, string deliveryId)
    : Exception($"No retained message '{deliveryId}' in channel '{channelName}'.")
{
    /// <summary>The channel that was addressed.</summary>
    public string ChannelName { get; } = channelName;

    /// <summary>The delivery identifier that could not be found.</summary>
    public string DeliveryId { get; } = deliveryId;
}
