namespace CasCap.Abstractions;

/// <summary>Performs message operations on a named channel.</summary>
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

    /// <summary>Sets a reaction on a message in the named channel, replacing any earlier one.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task SetReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a message in the named channel.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task RemoveReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Shows the typing indicator in the named channel.</summary>
    /// <remarks>Signal clears the indicator after a few seconds, so a long task repeats this.</remarks>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task StartTypingAsync(string channelName, CancellationToken cancellationToken = default);

    /// <summary>Clears the typing indicator in the named channel.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task StopTypingAsync(string channelName, CancellationToken cancellationToken = default);

    /// <summary>Creates a poll in the named channel.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task<ChannelPollResponse> CreatePollAsync(
        string channelName, ChannelPollRequest request, CancellationToken cancellationToken = default);

    /// <summary>Closes a poll in the named channel so it accepts no further votes.</summary>
    /// <exception cref="UnknownChannelException">The channel is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    Task ClosePollAsync(string channelName, string pollId, CancellationToken cancellationToken = default);
}
