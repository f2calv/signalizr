namespace CasCap.Abstractions;

/// <summary>
/// A long-running action, such as moving shutters, whose completion is observed after the call that
/// started it has returned.
/// </summary>
public interface ITrackedOperation
{
    /// <summary>Gets a short user-facing description, for example <c>Closing 12 shutters</c>.</summary>
    public string Description { get; }

    /// <summary>Gets the delay between progress polls.</summary>
    public TimeSpan PollInterval => TimeSpan.FromSeconds(5);

    /// <summary>Gets the minimum delay between progress messages; an unchanged status is repeated after three intervals.</summary>
    public TimeSpan ProgressInterval => TimeSpan.FromSeconds(20);

    /// <summary>Gets the time after which the operation is reported as incomplete.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Observes the current state, issuing any follow-up commands the operation needs.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current progress.</returns>
    public ValueTask<TrackedOperationProgress> PollAsync(CancellationToken cancellationToken);
}
