namespace CasCap.Abstractions;

/// <summary>Buffers inbound messages between the single receive loop and the dispatcher.</summary>
/// <remarks>
/// The queue exists so the receive loop can return to reading immediately. The upstream broadcast
/// is an unbuffered channel with a non-blocking send, so any work done before the next read is work
/// during which messages are silently lost.
/// </remarks>
public interface IInboundMessageQueue
{
    /// <summary>Messages dropped because the queue was full.</summary>
    /// <remarks>
    /// Dropping is visible by design. The alternative, blocking the writer, would push back onto
    /// the receive loop and lose messages upstream instead, where nothing can count them.
    /// </remarks>
    long DroppedCount { get; }

    /// <summary>Messages accepted into the queue.</summary>
    long EnqueuedCount { get; }

    /// <summary>Queues a message, never blocking.</summary>
    /// <returns><see langword="false"/> once the queue is closed.</returns>
    bool TryEnqueue(SignalReceivedMessage message);

    /// <summary>Reads queued messages until the token is cancelled or the queue is closed.</summary>
    IAsyncEnumerable<SignalReceivedMessage> DequeueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the queue so a reader completes once the backlog is drained.</summary>
    void Complete();
}
