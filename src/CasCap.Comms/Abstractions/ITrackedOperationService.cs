namespace CasCap.Abstractions;

/// <summary>Starts long-running operations and reports their progress to the chat group.</summary>
public interface ITrackedOperationService
{
    /// <summary>Starts observing an operation whose commands have already been issued.</summary>
    /// <param name="operation">The operation to observe.</param>
    /// <returns>The identifier of the tracked operation.</returns>
    public string Start(ITrackedOperation operation);
}
