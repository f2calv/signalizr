using System.Collections.Concurrent;

namespace CasCap.Services;

/// <summary>
/// Asks the chat group to approve a tool call with an Approve/Deny poll and decides the answer in code.
/// </summary>
/// <remarks>
/// <para>
/// Votes on an approval poll are claimed through <see cref="IPollVoteHandler"/> before the
/// <see cref="ICommsResponder"/> sees them, so no model can read or answer its own approval request.
/// </para>
/// <para>
/// Pending approvals are process-local. A restart, timeout, cancelled caller or unreachable gateway all
/// mean the tool does not run. That is safe for a single-replica Comms deployment; a second replica would
/// receive votes for polls it does not own and leave them unanswered.
/// </para>
/// </remarks>
public sealed partial class ToolApprovalService(
    ILogger<ToolApprovalService> logger,
    IOptions<CommsConfig> commsConfig,
    IOptions<ToolApprovalConfig> toolApprovalConfig,
    TimeProvider timeProvider,
    ISignalizrClient signalizrClient) : IPollVoteHandler
{
    private const int ApproveIndex = 0;
    private const int DenyIndex = 1;
    private const int MaxArgumentsLength = 200;
    private static readonly string[] Answers = ["Approve", "Deny"];

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new(StringComparer.Ordinal);

    /// <summary>Posts an approval poll for a tool call and waits for an accepted answer.</summary>
    /// <param name="toolName">The exposed tool name.</param>
    /// <param name="requester">Who asked, as shown to approvers; informational only.</param>
    /// <param name="arguments">The serialized tool arguments, or <see langword="null"/> when there are none.</param>
    /// <param name="cancellationToken">Cancellation token of the tool call.</param>
    /// <returns>The outcome; only <see cref="ToolApprovalOutcome.Approved"/> permits the call.</returns>
    public async Task<ToolApprovalOutcome> RequestApprovalAsync(
        string toolName,
        string requester,
        string? arguments,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(toolApprovalConfig.Value.TimeoutMs);
        var question = BuildQuestion(toolName, requester, arguments, timeout);

        string pollId;
        try
        {
            pollId = await signalizrClient.CreatePollAsync(
                commsConfig.Value.GroupName, question, Answers, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            LogPromptUnavailable(logger, ex, nameof(ToolApprovalService), toolName);
            return ToolApprovalOutcome.Unavailable;
        }

        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[pollId] = decision;
        LogApprovalRequested(logger, nameof(ToolApprovalService), toolName, pollId);

        ToolApprovalOutcome outcome;
        try
        {
            var approved = await decision.Task.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
            outcome = approved ? ToolApprovalOutcome.Approved : ToolApprovalOutcome.Denied;
        }
        catch (TimeoutException)
        {
            outcome = ToolApprovalOutcome.TimedOut;
        }
        finally
        {
            _pending.TryRemove(pollId, out _);
            await ClosePollAsync(toolName, pollId).ConfigureAwait(false);
        }

        LogApprovalCompleted(logger, nameof(ToolApprovalService), toolName, pollId, outcome);
        return outcome;
    }

    /// <inheritdoc/>
    public bool TryHandle(string voter, SignalizrPollVote pollVote)
    {
        if (!_pending.TryGetValue(pollVote.PollId, out var decision))
            return false;

        var approverIds = toolApprovalConfig.Value.ApproverIds;
        if (approverIds.Count > 0 && !approverIds.Contains(voter))
        {
            LogVoteIgnored(logger, nameof(ToolApprovalService), pollVote.PollId);
            return true;
        }

        // Deny wins if a client ever delivers both answers.
        if (pollVote.OptionIndexes.Contains(DenyIndex))
            decision.TrySetResult(false);
        else if (pollVote.OptionIndexes.Contains(ApproveIndex))
            decision.TrySetResult(true);
        return true;
    }

    internal static string BuildQuestion(string toolName, string requester, string? arguments, TimeSpan timeout)
    {
        var question = $"Approve {toolName} requested by {requester}?";
        if (!string.IsNullOrWhiteSpace(arguments))
            question += $" Arguments: {(arguments.Length > MaxArgumentsLength ? arguments[..MaxArgumentsLength] + "…" : arguments)}.";
        return question + $" Denied automatically after {timeout.TotalSeconds:0} seconds.";
    }

    // The poll is closed even when the caller was cancelled, so it stops accepting votes.
    private async Task ClosePollAsync(string toolName, string pollId)
    {
        try
        {
            await signalizrClient.ClosePollAsync(commsConfig.Value.GroupName, pollId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            LogPollCloseFailed(logger, ex, nameof(ToolApprovalService), toolName, pollId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} requested approval for {ToolName} with poll {PollId}")]
    private static partial void LogApprovalRequested(ILogger logger, string className, string toolName, string pollId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{ClassName} approval for {ToolName} with poll {PollId} completed as {Outcome}")]
    private static partial void LogApprovalCompleted(ILogger logger, string className, string toolName, string pollId, ToolApprovalOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} ignored a vote from a non-approver on poll {PollId}")]
    private static partial void LogVoteIgnored(ILogger logger, string className, string pollId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} could not post the approval prompt for {ToolName}; the call is denied")]
    private static partial void LogPromptUnavailable(ILogger logger, Exception exception, string className, string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ClassName} could not close approval poll {PollId} for {ToolName}")]
    private static partial void LogPollCloseFailed(ILogger logger, Exception exception, string className, string toolName, string pollId);
}
