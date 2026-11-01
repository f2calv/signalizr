using CasCap.Abstractions;
using System.Collections.Concurrent;

namespace CasCap.Tests.Fakes;

/// <summary>Records operator notices instead of sending them.</summary>
public sealed class FakeOperatorNotifier : IOperatorNotifier
{
    /// <summary>Every notice queued, in order.</summary>
    public ConcurrentQueue<string> Notices { get; } = new();

    /// <inheritdoc/>
    public void Notify(string notice) => Notices.Enqueue(notice);
}
