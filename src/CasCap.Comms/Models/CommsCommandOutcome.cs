namespace CasCap.Models;

/// <summary>The result of an <see cref="ICommsResponder"/> handling inbound text as a command.</summary>
/// <param name="Replies">Messages to send back immediately, in order; empty for none.</param>
/// <param name="DeferredTurn">A turn to queue instead of replying immediately, or <see langword="null"/>.</param>
/// <remarks>
/// A command answered immediately is marked with a green tick. A deferred turn is queued without
/// sender context, so it carries no reactions or typing indicator.
/// </remarks>
public sealed record CommsCommandOutcome(IReadOnlyList<string> Replies, CommsTurn? DeferredTurn = null);
