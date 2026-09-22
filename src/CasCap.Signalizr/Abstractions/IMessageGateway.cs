namespace CasCap.Abstractions;

/// <summary>Sends a message to a named channel.</summary>
/// <remarks>
/// The gateway owns the Signal account: it supplies the sender number and resolves the channel
/// name to a group id, so a caller never names either.
/// </remarks>
public interface IMessageGateway
{
    /// <summary>Sends a message to the named channel.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper could not be reached.</exception>
    Task<SendMessageResponse> SendAsync(
        string channelName, SendMessageRequest request, CancellationToken cancellationToken = default);
}
