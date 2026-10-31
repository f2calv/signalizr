namespace CasCap.Exceptions;

/// <summary>
/// Thrown when a delivery identifier matches no retained message in the addressed group.
/// </summary>
/// <remarks>
/// Retention removes messages once every subscriber has acknowledged them and the retention window
/// has passed, so an old but once-valid identifier ends up here too.
/// </remarks>
public sealed class UnknownDeliveryException(string groupName, string deliveryId)
    : Exception($"No retained message '{deliveryId}' in group '{groupName}'.")
{
    /// <summary>The group that was addressed.</summary>
    public string GroupName { get; } = groupName;

    /// <summary>The delivery identifier that could not be found.</summary>
    public string DeliveryId { get; } = deliveryId;
}
