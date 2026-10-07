namespace CasCap.Abstractions;

/// <summary>Performs message operations on a named group.</summary>
/// <remarks>
/// The gateway owns the Signal account: it supplies the sender number and resolves the group
/// name to a group id, so a caller never names either.
/// </remarks>
public interface IMessageGateway
{
    /// <summary>Sends a message to the named group.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper could not be reached.</exception>
    public Task<SendMessageResponse> SendAsync(
        string groupName, SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a message in the named group, replacing any earlier one.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task SetReactionAsync(
        string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a message in the named group.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task RemoveReactionAsync(
        string groupName, GroupReactionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a delivered message, addressed by its delivery identifier.</summary>
    /// <remarks>The gateway looks up the message's author and timestamp, so the caller supplies neither.</remarks>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="UnknownDeliveryException">No retained message has that identifier in that group.</exception>
    /// <exception cref="NotSupportedException">This process does not hold the persisted deliveries.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task SetDeliveryReactionAsync(
        string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a delivered message, addressed by its delivery identifier.</summary>
    /// <inheritdoc cref="SetDeliveryReactionAsync(string, string, string, CancellationToken)"/>
    public Task RemoveDeliveryReactionAsync(
        string groupName, string deliveryId, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Shows the typing indicator in the named group and holds it until stopped.</summary>
    /// <remarks>
    /// The gateway refreshes the indicator while it is held and clears it after
    /// <see cref="GatewayConfig.TypingMaxDurationMs"/> if no stop arrives.
    /// </remarks>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Clears the typing indicator in the named group.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Creates a poll in the named group.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task<GroupPollResponse> CreatePollAsync(
        string groupName, GroupPollRequest request, CancellationToken cancellationToken = default);

    /// <summary>Closes a poll in the named group so it accepts no further votes.</summary>
    /// <exception cref="UnknownGroupException">The group is not configured or not yet resolved.</exception>
    /// <exception cref="HttpRequestException">The upstream wrapper rejected the request or could not be reached.</exception>
    public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default);
}
