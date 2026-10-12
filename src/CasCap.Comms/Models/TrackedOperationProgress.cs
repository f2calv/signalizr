namespace CasCap.Models;

/// <summary>A snapshot of a tracked operation's progress.</summary>
/// <param name="Completed">Number of items that reached their target state.</param>
/// <param name="Total">Number of items the operation affects.</param>
/// <param name="Pending">Names of the items still in progress.</param>
public sealed record TrackedOperationProgress(int Completed, int Total, IReadOnlyList<string> Pending)
{
    /// <summary>Gets whether every item reached its target state.</summary>
    public bool IsFinished => Completed >= Total;
}
