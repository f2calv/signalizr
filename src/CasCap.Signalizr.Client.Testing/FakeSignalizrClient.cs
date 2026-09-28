namespace CasCap.Signalizr.Client.Testing;

/// <summary>
/// In-memory <see cref="ISignalizrClient"/> that feeds deliveries to a subscriber and records every
/// group operation, so a consumer can be tested without a gateway.
/// </summary>
/// <remarks>
/// Sends, reactions and polls always succeed. Timestamps and poll identifiers come from the supplied
/// <see cref="TimeProvider"/>, so a test using a fake clock gets deterministic values.
/// </remarks>
/// <param name="timeProvider">Clock for returned timestamps, or <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
public sealed class FakeSignalizrClient(TimeProvider? timeProvider = null) : ISignalizrClient
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Channel<SignalizrMessage> _inbox = Channel.CreateUnbounded<SignalizrMessage>();
    private TaskCompletionSource _startTypingGate = CompletedGate();
    private int _subscribeCallCount;
    private int _startTypingCallCount;
    private int _stopTypingCallCount;

    /// <summary>One reaction set or removed through the client.</summary>
    /// <param name="GroupName">Exact Signal group name.</param>
    /// <param name="Emoji">The reaction emoji.</param>
    /// <param name="TargetTimestamp">The target message's timestamp.</param>
    /// <param name="TargetAuthor">The target message's sender, or <see langword="null"/> for a message this gateway sent.</param>
    public sealed record Reaction(string GroupName, string Emoji, long TargetTimestamp, string? TargetAuthor);

    /// <summary>One poll created through <see cref="CreatePollAsync"/>.</summary>
    /// <param name="GroupName">Exact Signal group name.</param>
    /// <param name="PollId">The identifier returned to the caller.</param>
    /// <param name="Question">The poll question.</param>
    /// <param name="Answers">The answer options.</param>
    /// <param name="AllowMultipleSelections">Whether a voter may select more than one answer.</param>
    public sealed record Poll(string GroupName, string PollId, string Question, IReadOnlyList<string> Answers,
        bool AllowMultipleSelections);

    /// <summary>Groups returned by <see cref="GetGroupsAsync"/>.</summary>
    /// <remarks>Defaults to none, so a consumer that validates its groups at startup must be given them.</remarks>
    public IReadOnlyList<string> Groups { get; set; } = [];

    /// <summary>Messages sent through the client, in order.</summary>
    public ConcurrentQueue<(string GroupName, string Message, IReadOnlyList<string>? Attachments)> Sent { get; } = new();

    /// <summary>Reactions set through the client, in order.</summary>
    public ConcurrentQueue<Reaction> Reactions { get; } = new();

    /// <summary>Reactions removed through the client, in order.</summary>
    public ConcurrentQueue<Reaction> RemovedReactions { get; } = new();

    /// <summary>Polls created through the client, in order.</summary>
    public ConcurrentQueue<Poll> Polls { get; } = new();

    /// <summary>Polls closed through the client, in order.</summary>
    public ConcurrentQueue<(string GroupName, string PollId)> ClosedPolls { get; } = new();

    /// <summary>Attachment identifiers requested through <see cref="GetAttachmentAsync"/>, in order.</summary>
    public ConcurrentQueue<string> AttachmentFetches { get; } = new();

    /// <summary>Attachment bytes served by <see cref="GetAttachmentAsync"/>, keyed by attachment identifier.</summary>
    public ConcurrentDictionary<string, byte[]> Attachments { get; } = new();

    /// <summary>The number of subscriptions opened.</summary>
    public int SubscribeCallCount => Volatile.Read(ref _subscribeCallCount);

    /// <summary>The number of <see cref="StartTypingAsync"/> calls.</summary>
    public int StartTypingCallCount => Volatile.Read(ref _startTypingCallCount);

    /// <summary>The number of <see cref="StopTypingAsync"/> calls.</summary>
    public int StopTypingCallCount => Volatile.Read(ref _stopTypingCallCount);

    /// <summary>Queues an inbound delivery for the subscription.</summary>
    public void Enqueue(SignalizrMessage message) => _inbox.Writer.TryWrite(message);

    /// <summary>Ends the subscription stream cleanly once queued deliveries are consumed, as a gateway restart would.</summary>
    /// <remarks>The inbox cannot be reopened, so later subscriptions end immediately.</remarks>
    public void Complete() => _inbox.Writer.TryComplete();

    /// <summary>Counts the reactions set with <paramref name="emoji"/>.</summary>
    public int ReactionCount(string emoji) => Reactions.Count(r => r.Emoji == emoji);

    /// <summary>Messages sent to <paramref name="groupName"/>, in order.</summary>
    public IEnumerable<(string GroupName, string Message, IReadOnlyList<string>? Attachments)> SentTo(string groupName) =>
        Sent.Where(sent => sent.GroupName == groupName);

    /// <summary>Holds callers inside <see cref="StartTypingAsync"/> until <see cref="ReleaseStartTyping"/>.</summary>
    /// <remarks>Lets a test hold a consumer's reply loop, for example to fill a bounded queue and observe backpressure.</remarks>
    public void BlockStartTyping() =>
        _startTypingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases callers held by <see cref="BlockStartTyping"/>.</summary>
    public void ReleaseStartTyping() => _startTypingGate.TrySetResult();

    /// <inheritdoc/>
    public Task<string> SendAsync(string groupName, string message, CancellationToken cancellationToken = default) =>
        SendAsync(groupName, message, base64Attachments: null, cancellationToken);

    /// <inheritdoc/>
    public Task<string> SendAsync(string groupName, string message, IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((groupName, message, base64Attachments));
        return Task.FromResult(NowTimestamp());
    }

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">No bytes were registered in <see cref="Attachments"/> for the identifier.</exception>
    public Task<byte[]> GetAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        AttachmentFetches.Enqueue(attachmentId);
        return Attachments.TryGetValue(attachmentId, out var content)
            ? Task.FromResult(content)
            : Task.FromException<byte[]>(new HttpRequestException("The attachment is not retained.", null, HttpStatusCode.NotFound));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Groups);

    /// <inheritdoc/>
    public Task SetReactionAsync(string groupName, string reaction, long targetTimestamp,
        string? targetAuthor = null, CancellationToken cancellationToken = default)
    {
        Reactions.Enqueue(new Reaction(groupName, reaction, targetTimestamp, targetAuthor));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveReactionAsync(string groupName, string reaction, long targetTimestamp,
        string? targetAuthor = null, CancellationToken cancellationToken = default)
    {
        RemovedReactions.Enqueue(new Reaction(groupName, reaction, targetTimestamp, targetAuthor));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        SetReactionAsync(RequireGroup(message), reaction, message.Timestamp, message.Sender, cancellationToken);

    /// <inheritdoc/>
    public Task RemoveReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        RemoveReactionAsync(RequireGroup(message), reaction, message.Timestamp, message.Sender, cancellationToken);

    /// <inheritdoc/>
    public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _startTypingCallCount);
        return _startTypingGate.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _stopTypingCallCount);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string> CreatePollAsync(string groupName, string question, IReadOnlyList<string> answers,
        bool allowMultipleSelections = false, CancellationToken cancellationToken = default)
    {
        var pollId = NowTimestamp();
        Polls.Enqueue(new Poll(groupName, pollId, question, answers, allowMultipleSelections));
        return Task.FromResult(pollId);
    }

    /// <inheritdoc/>
    public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default)
    {
        ClosedPolls.Enqueue((groupName, pollId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SignalizrMessage> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _subscribeCallCount);
        await foreach (var message in _inbox.Reader.ReadAllAsync(cancellationToken))
            yield return message;
    }

    private string NowTimestamp() =>
        _timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    //Mirrors the client contract: a delivery that arrived on no configured group cannot be reacted to.
    private static string RequireGroup(SignalizrMessage message) =>
        message.GroupName ?? throw new ArgumentException("The message arrived on no configured group.", nameof(message));

    private static TaskCompletionSource CompletedGate()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }
}
