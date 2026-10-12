using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>
/// Observes <see cref="ITrackedOperation"/> instances and writes their progress to the communications
/// stream, so the chat learns when a long-running action actually finishes rather than when it started.
/// </summary>
/// <remarks>
/// Add <see cref="SourceName"/> to <see cref="CommsConfig.DirectDeliverySources"/> so updates are
/// delivered verbatim instead of being turned into agent prompts. Tracking is in-memory; a restart
/// ends observation but not the underlying physical action.
/// </remarks>
public sealed partial class TrackedOperationService(
    ILogger<TrackedOperationService> logger,
    TimeProvider timeProvider,
    IHostEnvironment env,
    IEventSink<CommsEvent> commsSink) : ITrackedOperationService, IHostedService, IDisposable
{
    /// <summary>The <see cref="CommsEvent.Source"/> of progress updates.</summary>
    public const string SourceName = nameof(TrackedOperationService);

    private const int MaxListedNames = 10;

    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public string Start(ITrackedOperation operation)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var task = Task.Run(() => TrackAsync(operationId, operation, _stopping.Token));
        _running[operationId] = task;
        _ = task.ContinueWith(_ => _running.TryRemove(operationId, out var _), TaskScheduler.Default);
        LogOperationStarted(logger, nameof(TrackedOperationService), operationId, operation.Description);
        return operationId;
    }

    internal async Task TrackAsync(string operationId, ITrackedOperation operation, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var lastReported = started;
        var lastReportedCompleted = -1;
        TrackedOperationProgress? progress = null;
        try
        {
            while (true)
            {
                await Task.Delay(operation.PollInterval, timeProvider, cancellationToken);
                try
                {
                    progress = await operation.PollAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogPollFailed(logger, ex, nameof(TrackedOperationService), operationId);
                }

                if (progress?.IsFinished is true)
                {
                    await PublishAsync($"{operation.Description}: done, all {progress.Total} reached their target.", cancellationToken);
                    LogOperationFinished(logger, nameof(TrackedOperationService), operationId, true);
                    return;
                }

                var elapsed = timeProvider.GetElapsedTime(started);
                if (elapsed >= operation.Timeout)
                {
                    await PublishAsync(FormatTimeout(operation, progress, elapsed), cancellationToken);
                    LogOperationFinished(logger, nameof(TrackedOperationService), operationId, false);
                    return;
                }

                if (progress is not null && IsProgressDue(operation, progress.Completed != lastReportedCompleted, timeProvider.GetElapsedTime(lastReported)))
                {
                    await PublishAsync(
                        $"{operation.Description}: {progress.Completed} of {progress.Total} done, still in progress: {FormatNames(progress.Pending)}.",
                        cancellationToken);
                    lastReported = timeProvider.GetTimestamp();
                    lastReportedCompleted = progress.Completed;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogOperationAbandoned(logger, nameof(TrackedOperationService), operationId);
        }
        catch (Exception ex)
        {
            LogPollFailed(logger, ex, nameof(TrackedOperationService), operationId);
        }
    }

    // An unchanged status is repeated less often so a stalled operation still shows signs of life without flooding the chat.
    private static bool IsProgressDue(ITrackedOperation operation, bool changed, TimeSpan sinceLastReport)
        => sinceLastReport >= (changed ? operation.ProgressInterval : operation.ProgressInterval * 3);

    private static string FormatTimeout(ITrackedOperation operation, TrackedOperationProgress? progress, TimeSpan elapsed)
        => progress is null
            ? $"{operation.Description}: stopped waiting after {elapsed.TotalSeconds:0} s, no state could be read."
            : $"{operation.Description}: stopped waiting after {elapsed.TotalSeconds:0} s, {progress.Completed} of {progress.Total} done; not confirmed: {FormatNames(progress.Pending)}.";

    private static string FormatNames(IReadOnlyList<string> names)
        => names.Count <= MaxListedNames
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxListedNames))} and {names.Count - MaxListedNames} more";

    private Task PublishAsync(string message, CancellationToken cancellationToken)
        => commsSink.WriteEvent(new CommsEvent
        {
            Source = SourceName,
            Message = message,
            Environment = env.GetAcronym(),
            TimestampUtc = timeProvider.GetUtcNow().UtcDateTime,
        }, cancellationToken);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_running.Values).WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <inheritdoc/>
    public void Dispose() => _stopping.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} started operation {OperationId}: {Description}")]
    private static partial void LogOperationStarted(ILogger logger, string className, string operationId, string description);

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} finished operation {OperationId}, completed={Completed}")]
    private static partial void LogOperationFinished(ILogger logger, string className, string operationId, bool completed);

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} abandoned operation {OperationId} on shutdown")]
    private static partial void LogOperationAbandoned(ILogger logger, string className, string operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} progress check failed for operation {OperationId}")]
    private static partial void LogPollFailed(ILogger logger, Exception ex, string className, string operationId);
}
