namespace CasCap.Services;

/// <summary>
/// <see cref="ICommsResponder"/> that answers Signalizr communications through the remote Agent Runtime.
/// </summary>
/// <remarks>
/// <para>
/// The runtime owns tenant definitions, credentials, overrides, session state, agent construction and tools.
/// Comms owns message parsing, progress interaction, delivery and operator diagnostics.
/// </para>
/// <para>
/// Handles slash commands through typed runtime control operations, poll votes through <see cref="IPollTracker"/>,
/// live delegation feedback and the stats footer and monitor-group timeline produced by
/// <see cref="CommsDebugNotifier"/>.
/// </para>
/// </remarks>
public sealed partial class AgentCommsResponder : ICommsResponder
{
    private const string Hourglass = "\u23F3";
    private const string TwistedArrows = "\U0001F500";

    private readonly ILogger _logger;
    private readonly IOptions<CommsConfig> _commsConfig;
    private readonly CommsAgentProfile _profile;
    private readonly IAgentRuntimeClient _agentRuntimeClient;
    private readonly ISignalizrClient _signalizrClient;
    private readonly IPollTracker _pollTracker;
    private readonly CommsDebugNotifier _debugNotifier;
    private readonly IReadOnlyList<IAgentRunEnricher> _enrichers;

    /// <summary>Initializes a new instance of the <see cref="AgentCommsResponder"/> class.</summary>
    public AgentCommsResponder(ILogger<AgentCommsResponder> logger,
        IOptions<CommsConfig> commsConfig,
        CommsAgentProfile profile,
        IAgentRuntimeClient agentRuntimeClient,
        ISignalizrClient signalizrClient,
        IPollTracker pollTracker,
        CommsDebugNotifier debugNotifier,
        IEnumerable<IAgentRunEnricher> enrichers)
    {
        _logger = logger;
        _commsConfig = commsConfig;
        _profile = profile;
        _agentRuntimeClient = agentRuntimeClient;
        _signalizrClient = signalizrClient;
        _pollTracker = pollTracker;
        _debugNotifier = debugNotifier;
        _enrichers = [.. enrichers];
    }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public string DefaultPrompt => _profile.DefaultPrompt;

    /// <inheritdoc/>
    public async Task<CommsTurn> CreateStreamTurnAsync(CommsEvent commsEvent, IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken)
    {
        // Copy the raw event to the monitor group, so it can be read alongside the agent's reply.
        await _debugNotifier.SendStreamEventDebugAsync(commsEvent, cancellationToken);

        var prompt = $"[{commsEvent.Source}] {commsEvent.Message}";
        if (commsEvent.JsonPayload is not null)
            prompt += $"\n\nJSON: {commsEvent.JsonPayload}";
        return new CommsTurn(prompt, ExtraBase64Attachments: base64Attachments);
    }

    /// <inheritdoc/>
    public Task<CommsTurn?> HandlePollVoteAsync(string voter, SignalizrPollVote pollVote, CancellationToken cancellationToken)
    {
        var pollId = pollVote.PollId;
        var selectedIndices = pollVote.OptionIndexes.ToArray();
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var selectedIndicesText = string.Join(", ", selectedIndices);
            LogPollVoteReceived(_logger, nameof(AgentCommsResponder), pollId, selectedIndicesText);
        }

        // Fetch the poll first so we have its metadata even if it expires between RecordVote and
        // building the prompt.
        var poll = _pollTracker.GetPoll(pollId);
        if (!_pollTracker.RecordVote(pollId, voter, selectedIndices) || poll is null)
        {
            LogPollNotTracked(_logger, nameof(AgentCommsResponder), pollId);
            return Task.FromResult<CommsTurn?>(null);
        }

        // Build a descriptive prompt so the agent knows the poll result. The voter's Signal
        // identifier is personal data and stays out of the prompt.
        const string voterName = "A group member";
        var selectedLabels = selectedIndices
            .Where(i => i >= 0 && i < poll.Answers.Length)
            .Select(i => poll.Answers[i]);

        var prompt = $"[POLL VOTE] {voterName} chose \"{string.Join(", ", selectedLabels)}\" "
            + $"on poll \"{poll.Question}\" (ID: {poll.PollId}). "
            + "INSTRUCTIONS: 1) Call close_poll with the ID above. 2) Act on the chosen option. 3) Do NOT present more choices unless they are in a new poll.";
        return Task.FromResult<CommsTurn?>(new CommsTurn(prompt));
    }

    /// <inheritdoc/>
    public async Task<CommsCommandOutcome?> TryHandleCommandAsync(string text, CancellationToken cancellationToken)
    {
        if (!CommsAgentCommandParser.TryParse(text, out var command, out var argument))
            return null;

        LogSlashCommand(_logger, nameof(AgentCommsResponder), command);

        if (command is CommsAgentCommand.SessionBypass && !string.IsNullOrWhiteSpace(argument))
            return new CommsCommandOutcome(null, new CommsTurn(argument, BypassSession: true));

        return new CommsCommandOutcome(await HandleCommandAsync(command, argument, cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<CommsReply?> RespondAsync(CommsTurn turn, CancellationToken cancellationToken)
    {
        var (result, debugSteps) = await RunAgentAsync(turn, cancellationToken);
        if (result is null || string.IsNullOrWhiteSpace(result.OutputText))
            return null;

        var footer = await _debugNotifier.FormatStatsFooterAsync(result, cancellationToken);

        // Convert any image attachments from tool results into Signal base64 attachments.
        var attachments = result.Attachments.Count > 0
            ? result.Attachments
                .Select(a => $"data:{a.MimeType};filename={a.FileName ?? "photo"};base64,{a.Base64Content}")
                .ToArray()
            : null;

        return new CommsReply(result.OutputText, footer, attachments, async ct =>
        {
            // Send the detailed pipeline timeline to the monitor group.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var stepsWithResult = debugSteps.Count(s => s.Result is not null);
                var stepsWithUsage = debugSteps.Count(s => s.Result?.Usage is not null);
                LogDebugStats(_logger, nameof(AgentCommsResponder),
                    result.Usage is not null,
                    result.Usage?.InputTokenCount,
                    result.Usage?.OutputTokenCount,
                    debugSteps.Count,
                    stepsWithResult,
                    stepsWithUsage);
            }
            await _debugNotifier.SendDebugStatsAsync(turn.Prompt, result, debugSteps, turn.Timestamp, ct);
        });
    }

    private async Task<(CommsAgentRunResult? Result, List<CommsDebugStep> DebugSteps)> RunAgentAsync(CommsTurn turn,
        CancellationToken cancellationToken)
    {
        try
        {
            LogAgentInferenceStarting(_logger, nameof(AgentCommsResponder), turn.Prompt.Length,
                turn.BinaryContent is not null, _profile.AgentName);

            var debugSteps = new List<CommsDebugStep>();
            var pipelineSw = Stopwatch.StartNew();
            debugSteps.Add(new CommsDebugStep(
                $"\U0001F680 {_profile.AgentName}",
                null,
                TimeSpan.Zero));

            var enrichmentState = new object?[_enrichers.Count];
            for (var i = 0; i < _enrichers.Count; i++)
                enrichmentState[i] = await _enrichers[i].BeforeRunAsync(cancellationToken);

            RunAgentResponse? response = null;
            await foreach (var item in _agentRuntimeClient.StreamAgentAsync(
                _profile.AgentName,
                new RunAgentRequest
                {
                    SessionId = _profile.SessionId,
                    Input = turn.Prompt,
                    BinaryContent = turn.BinaryContent,
                    MimeType = turn.MimeType,
                    BypassSession = turn.BypassSession,
                    IncludeDiagnosticDetails = _commsConfig.Value.MonitorGroupName is { Length: > 0 }
                        || _enrichers.Count > 0,
                },
                cancellationToken))
            {
                if (item.Event is { } executionEvent)
                    await HandleExecutionEventAsync(executionEvent, turn, debugSteps, pipelineSw, cancellationToken);
                if (item.Response is not null)
                    response = item.Response;
            }

            if (response is null)
                return (null, debugSteps);

            var result = CommsAgentRunResult.FromResponse(response);
            for (var i = 0; i < _enrichers.Count; i++)
                await _enrichers[i].AfterRunAsync(result, enrichmentState[i], cancellationToken);

            LogAgentCompleted(_logger, nameof(AgentCommsResponder), result.Elapsed,
                result.Session is { Exists: true } ? "present" : "missing");
            await SetReactionAsync(Hourglass, turn, cancellationToken);

            pipelineSw.Stop();
            debugSteps.Add(new CommsDebugStep(
                $"\U0001F3C1 {_profile.AgentName}",
                result.ModelName,
                pipelineSw.Elapsed,
                result));
            return (result, debugSteps);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogAgentInferenceFailed(_logger, ex, nameof(AgentCommsResponder));
            return (null, []);
        }
    }

    private async Task HandleExecutionEventAsync(
        RunAgentEvent executionEvent,
        CommsTurn turn,
        List<CommsDebugStep> debugSteps,
        Stopwatch pipelineStopwatch,
        CancellationToken cancellationToken)
    {
        if (executionEvent.Type == RunAgentEventTypes.DelegationStarted)
        {
            var depth = executionEvent.Depth ?? 1;
            var depthLabel = depth switch { 1 => "sub-agent", 2 => "sub-sub-agent", _ => $"depth-{depth} agent" };
            var agentName = executionEvent.AgentName ?? "agent";
            var modelName = executionEvent.ModelName ?? "unknown model";
            LogAgentDelegating(_logger, nameof(AgentCommsResponder), agentName, depthLabel, modelName);
            debugSteps.Add(new CommsDebugStep(
                $"{TwistedArrows} {agentName} ({depthLabel})",
                modelName,
                pipelineStopwatch.Elapsed));
            if (_commsConfig.Value.DelegationMessagesEnabled)
            {
                await _signalizrClient.SendAsync(
                    _commsConfig.Value.GroupName,
                    $"{TwistedArrows} Consulting {agentName} ({depthLabel}) \u2022 {modelName}",
                    cancellationToken);
            }
            await SetReactionAsync(TwistedArrows, turn, cancellationToken);
            return;
        }

        if (executionEvent.Type == RunAgentEventTypes.DelegationCompleted)
        {
            debugSteps.Add(new CommsDebugStep(
                $"\u2705 {executionEvent.AgentName ?? "agent"}",
                executionEvent.ModelName,
                pipelineStopwatch.Elapsed,
                executionEvent.Result is { } result ? MapStepResult(result, executionEvent.ModelName) : null));
            return;
        }

        if (executionEvent.Type == RunAgentEventTypes.SessionCompacted)
        {
            var input = executionEvent.InputMessageCount ?? 0;
            var output = executionEvent.OutputMessageCount ?? 0;
            var toolDropped = executionEvent.ToolMessagesDropped ?? 0;
            var windowTrimmed = executionEvent.WindowMessagesTrimmed ?? 0;
            var target = executionEvent.TargetMessageCount ?? 0;
            LogSessionCompaction(_logger, nameof(AgentCommsResponder), input, output, toolDropped, windowTrimmed, target);
            await _debugNotifier.SendCompactionDebugAsync(
                input,
                output,
                toolDropped,
                windowTrimmed,
                target,
                cancellationToken);
        }
    }

    private static CommsAgentRunResult MapStepResult(RunAgentStepResult result, string? modelName)
    {
        var mapped = new CommsAgentRunResult
        {
            OutputText = string.Empty,
            ModelName = modelName ?? string.Empty,
            Elapsed = TimeSpan.FromMilliseconds(result.ElapsedMilliseconds),
            Usage = result.Usage,
            ToolCalls = result.ToolCalls,
        };
        foreach (var (key, value) in result.AdditionalProperties)
            mapped.AdditionalProperties[key] = value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var number)
                ? number
                : value.ToString();
        return mapped;
    }

    /// <summary>Sets a progress reaction on the turn's inbound message, best effort.</summary>
    private Task SetReactionAsync(string reaction, CommsTurn turn, CancellationToken cancellationToken) =>
        turn is { Sender: { } sender, Timestamp: { } timestamp }
            ? _signalizrClient.TrySetReactionAsync(_commsConfig.Value.GroupName, reaction, timestamp, sender,
                ex => LogGroupInteractionFailed(_logger, ex, nameof(AgentCommsResponder)), cancellationToken)
            : Task.CompletedTask;
}
