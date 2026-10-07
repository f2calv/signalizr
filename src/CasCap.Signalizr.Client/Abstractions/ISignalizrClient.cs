namespace CasCap.Signalizr.Client;

/// <summary>Performs group operations and subscribes to inbound messages.</summary>
/// <remarks>Every group argument is an exact Signal group name, including case and spaces.
/// The client URL-encodes names when building REST requests.</remarks>
public interface ISignalizrClient
{
    /// <summary>Sends a message to a group.</summary>
    /// <returns>The Signal server's message timestamp.</returns>
    /// <exception cref="HttpRequestException">
    /// The gateway rejected the send or could not be reached. A 404 means the group is not
    /// configured on that gateway.
    /// </exception>
    public Task<string> SendAsync(string groupName, string message, CancellationToken cancellationToken = default);

    /// <summary>Sends a message and optional binary signal-cli data-URI attachments to a group.</summary>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="message">Message text.</param>
    /// <param name="base64Attachments">
    /// Optional signal-cli-compatible binary data-URI attachments, including images and audio.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Signal server's message timestamp.</returns>
    public Task<string> SendAsync(
        string groupName,
        string message,
        IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads durable attachment bytes by identifier.</summary>
    public Task<byte[]> GetAttachmentAsync(
        string attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>The groups the gateway currently has resolved.</summary>
    public Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a message in a group, replacing any earlier one.</summary>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="reaction">The reaction emoji.</param>
    /// <param name="targetTimestamp">The target message's timestamp, as delivered or as returned by a send.</param>
    /// <param name="targetAuthor">
    /// The target message's sender as delivered, or <see langword="null"/> for a message this
    /// gateway sent.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    public Task SetReactionAsync(
        string groupName,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a message in a group.</summary>
    /// <inheritdoc cref="SetReactionAsync(string, string, long, string?, CancellationToken)"/>
    public Task RemoveReactionAsync(
        string groupName,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a delivered message, replacing any earlier one.</summary>
    /// <param name="message">The delivered message; the gateway resolves its author and timestamp.</param>
    /// <param name="reaction">The reaction emoji.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The message arrived on no configured group.</exception>
    /// <exception cref="HttpRequestException">
    /// The gateway rejected the request or could not be reached. A 404 also means the message is
    /// no longer retained.
    /// </exception>
    public Task SetReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a delivered message.</summary>
    /// <inheritdoc cref="SetReactionAsync(SignalizrMessage, string, CancellationToken)"/>
    public Task RemoveReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Shows the typing indicator in a group and holds it until stopped.</summary>
    /// <remarks>
    /// The gateway refreshes the indicator while it is held, and clears it after its configured
    /// maximum if no stop arrives, so a consumer that fails before stopping cannot leave it showing.
    /// </remarks>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Clears the typing indicator in a group.</summary>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>Creates a poll in a group.</summary>
    /// <param name="groupName">Exact configured Signal group name.</param>
    /// <param name="question">The poll question.</param>
    /// <param name="answers">The answer options; votes refer to them by index.</param>
    /// <param name="allowMultipleSelections">Whether a voter may select more than one answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The poll identifier, which votes carry and closing requires.</returns>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    public Task<string> CreatePollAsync(
        string groupName,
        string question,
        IReadOnlyList<string> answers,
        bool allowMultipleSelections = false,
        CancellationToken cancellationToken = default);

    /// <summary>Closes a poll so it accepts no further votes.</summary>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default);

    /// <summary>Streams inbound messages until the token is cancelled or the stream ends.</summary>
    /// <remarks>
    /// Each message is acknowledged when the consumer asks for the next one, so acknowledgement
    /// means "the previous message was processed" rather than "it arrived". A consumer that stops
    /// enumerating therefore leaves the last message unacknowledged, which is correct: it may not
    /// have been processed.
    /// <para>
    /// The stream is not resubscribed automatically. A caller that wants to survive a gateway
    /// restart must loop, which keeps the reconnect visible rather than hiding a gap in delivery.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<SignalizrMessage> SubscribeAsync(CancellationToken cancellationToken = default);
}
