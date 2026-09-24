namespace CasCap.Exceptions;

/// <summary>Thrown when a caller addresses a channel that is not configured or not yet resolved.</summary>
/// <remarks>
/// A distinct type because this is a caller error the gateway can describe precisely, unlike an
/// upstream failure, and the two must not produce the same HTTP status.
/// </remarks>
public sealed class UnknownChannelException(string channelName)
    : Exception($"Channel '{channelName}' is not configured.")
{
    /// <summary>The channel name that could not be resolved.</summary>
    public string ChannelName { get; } = channelName;
}
