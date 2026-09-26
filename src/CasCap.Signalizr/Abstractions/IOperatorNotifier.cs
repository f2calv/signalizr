namespace CasCap.Abstractions;

/// <summary>Posts gateway operational notices to the account's own "Note to Self" conversation.</summary>
/// <remarks>
/// For the operator of the account, not for channel consumers. Notices carry subscriber names,
/// channel names and counts only — never a phone number, a group id or message content.
/// </remarks>
public interface IOperatorNotifier
{
    /// <summary>Queues a notice for delivery.</summary>
    /// <remarks>
    /// Never blocks and never throws: callers include the receive path, which must not wait on an
    /// outbound send. When notices arrive faster than they can be sent, the oldest is discarded.
    /// </remarks>
    void Notify(string notice);
}
