namespace CasCap.Signalizr.Client;

/// <summary>Sends to a named channel and subscribes to inbound messages.</summary>
public interface ISignalizrClient
{
    /// <summary>Sends a message to a channel.</summary>
    /// <returns>The Signal server's message timestamp.</returns>
    /// <exception cref="HttpRequestException">
    /// The gateway rejected the send or could not be reached. A 404 means the channel is not
    /// configured on that gateway.
    /// </exception>
    Task<string> SendAsync(string channel, string message, CancellationToken cancellationToken = default);

    /// <summary>The channels the gateway currently has resolved.</summary>
    Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken = default);

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
