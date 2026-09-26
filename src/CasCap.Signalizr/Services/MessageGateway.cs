namespace CasCap.Services;

/// <inheritdoc cref="IMessageGateway"/>
public sealed class MessageGateway(
    ILogger<MessageGateway> logger,
    ISignalCliClient client,
    IChannelResolver channelResolver,
    IOptions<SignalCliConfig> signalCliConfig) : IMessageGateway
{
    /// <inheritdoc/>
    public async Task<SendMessageResponse> SendAsync(
        string channelName, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);

        var message = CreateRequest(
            signalCliConfig.Value.PhoneNumber,
            groupId,
            request.Message,
            request.Base64Attachments);

        // A null response means the wrapper accepted nothing we can identify the message by, which
        // is indistinguishable from a failed send, so it must not be reported as success.
        var response = await client.SendMessage(message, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no send result.");

        // Channel name only: the group id is an account-linked identifier and the message body is
        // the caller's content, neither of which belongs in a log.
        logger.LogInformation("{ClassName} sent a message to channel {Channel}",
            nameof(MessageGateway), channelName);

        return new SendMessageResponse { Channel = channelName, Timestamp = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task SetReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.SendReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction");
    }

    /// <inheritdoc/>
    public async Task RemoveReactionAsync(
        string channelName, ChannelReactionRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var account = signalCliConfig.Value.PhoneNumber;
        EnsureAccepted(await client.RemoveReaction(account, groupId, request.Reaction,
            request.TargetAuthor ?? account, request.TargetTimestamp, cancellationToken).ConfigureAwait(false),
            "reaction removal");
    }

    /// <inheritdoc/>
    public async Task StartTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.ShowTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId,
            cancellationToken).ConfigureAwait(false), "typing indicator");
    }

    /// <inheritdoc/>
    public async Task StopTypingAsync(string channelName, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.HideTypingIndicator(signalCliConfig.Value.PhoneNumber, groupId,
            cancellationToken).ConfigureAwait(false), "typing indicator removal");
    }

    /// <inheritdoc/>
    public async Task<ChannelPollResponse> CreatePollAsync(
        string channelName, ChannelPollRequest request, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        var response = await client.CreatePoll(signalCliConfig.Value.PhoneNumber, new CreatePollRequest
        {
            Question = request.Question,
            Answers = [.. request.Answers],
            Recipient = groupId,
            AllowMultipleSelections = request.AllowMultipleSelections
        }, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("The signal-cli wrapper returned no poll result.");

        logger.LogInformation("{ClassName} created a poll in channel {Channel}",
            nameof(MessageGateway), channelName);

        return new ChannelPollResponse { Channel = channelName, PollId = response.Timestamp };
    }

    /// <inheritdoc/>
    public async Task ClosePollAsync(string channelName, string pollId, CancellationToken cancellationToken = default)
    {
        var groupId = ResolveGroupId(channelName);
        EnsureAccepted(await client.ClosePoll(signalCliConfig.Value.PhoneNumber, new ClosePollRequest
        {
            PollTimestamp = pollId,
            Recipient = groupId
        }, cancellationToken).ConfigureAwait(false), "poll closure");
    }

    private string ResolveGroupId(string channelName) =>
        channelResolver.TryGetGroupId(channelName, out var groupId)
            ? groupId
            : throw new UnknownChannelException(channelName);

    // The upstream reports rejection as false rather than an error status, and the caller must
    // not mistake a refused operation for a completed one.
    private static void EnsureAccepted(bool accepted, string operation)
    {
        if (!accepted)
            throw new HttpRequestException($"The signal-cli wrapper rejected the {operation}.");
    }

    /// <summary>Builds the upstream send request for one group.</summary>
    /// <remarks>
    /// Separated from the call so the translation is testable without stubbing the whole client.
    /// </remarks>
    public static SignalMessageRequest CreateRequest(
        string number,
        string groupId,
        string message,
        IReadOnlyList<string>? base64Attachments = null)
        => new()
        {
            Number = number,
            Recipients = [groupId],
            Message = message,
            Base64Attachments = base64Attachments?.ToArray()
        };
}
