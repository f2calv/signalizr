namespace CasCap.Signalizr.Client;

/// <summary>Performs channel operations and subscribes to inbound messages.</summary>
public interface ISignalizrClient
{
    /// <summary>Sends a message to a channel.</summary>
    /// <returns>The Signal server's message timestamp.</returns>
    /// <exception cref="HttpRequestException">
    /// The gateway rejected the send or could not be reached. A 404 means the channel is not
    /// configured on that gateway.
    /// </exception>
    Task<string> SendAsync(string channel, string message, CancellationToken cancellationToken = default);

    /// <summary>Sends a message and optional binary signal-cli data-URI attachments to a channel.</summary>
    /// <param name="channel">Configured Signalizr channel name.</param>
    /// <param name="message">Message text.</param>
    /// <param name="base64Attachments">
    /// Optional signal-cli-compatible binary data-URI attachments, including images and audio.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Signal server's message timestamp.</returns>
    Task<string> SendAsync(
        string channel,
        string message,
        IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads durable attachment bytes by identifier.</summary>
    Task<byte[]> GetAttachmentAsync(
        string attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>The channels the gateway currently has resolved.</summary>
    Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a message in a channel, replacing any earlier one.</summary>
    /// <param name="channel">Configured Signalizr channel name.</param>
    /// <param name="reaction">The reaction emoji.</param>
    /// <param name="targetTimestamp">The target message's timestamp, as delivered or as returned by a send.</param>
    /// <param name="targetAuthor">
    /// The target message's sender as delivered, or <see langword="null"/> for a message this
    /// gateway sent.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    Task SetReactionAsync(
        string channel,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a message in a channel.</summary>
    /// <inheritdoc cref="SetReactionAsync(string, string, long, string?, CancellationToken)"/>
    Task RemoveReactionAsync(
        string channel,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a reaction on a delivered message, replacing any earlier one.</summary>
    /// <param name="message">The delivered message; the gateway resolves its author and timestamp.</param>
    /// <param name="reaction">The reaction emoji.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">The message arrived on no configured channel.</exception>
    /// <exception cref="HttpRequestException">
    /// The gateway rejected the request or could not be reached. A 404 also means the message is
    /// no longer retained.
    /// </exception>
    Task SetReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Removes a reaction from a delivered message.</summary>
    /// <inheritdoc cref="SetReactionAsync(SignalizrMessage, string, CancellationToken)"/>
    Task RemoveReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default);

    /// <summary>Shows the typing indicator in a channel and holds it until stopped.</summary>
    /// <remarks>
    /// The gateway refreshes the indicator while it is held, and clears it after its configured
    /// maximum if no stop arrives, so a consumer that fails before stopping cannot leave it showing.
    /// </remarks>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    Task StartTypingAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>Clears the typing indicator in a channel.</summary>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    Task StopTypingAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>Creates a poll in a channel.</summary>
    /// <param name="channel">Configured Signalizr channel name.</param>
    /// <param name="question">The poll question.</param>
    /// <param name="answers">The answer options; votes refer to them by index.</param>
    /// <param name="allowMultipleSelections">Whether a voter may select more than one answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The poll identifier, which votes carry and closing requires.</returns>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    Task<string> CreatePollAsync(
        string channel,
        string question,
        IReadOnlyList<string> answers,
        bool allowMultipleSelections = false,
        CancellationToken cancellationToken = default);

    /// <summary>Closes a poll so it accepts no further votes.</summary>
    /// <exception cref="HttpRequestException">The gateway rejected the request or could not be reached.</exception>
    Task ClosePollAsync(string channel, string pollId, CancellationToken cancellationToken = default);

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
    IAsyncEnumerable<SignalizrMessage> SubscribeAsync(CancellationToken cancellationToken = default);
}
