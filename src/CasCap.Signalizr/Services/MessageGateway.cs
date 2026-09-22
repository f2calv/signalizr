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
        if (!channelResolver.TryGetGroupId(channelName, out var groupId))
            throw new UnknownChannelException(channelName);

        var message = CreateRequest(signalCliConfig.Value.PhoneNumber, groupId, request.Message);

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

    /// <summary>Builds the upstream send request for one group.</summary>
    /// <remarks>
    /// Separated from the call so the translation is testable without stubbing the whole client.
    /// </remarks>
    public static SignalMessageRequest CreateRequest(string number, string groupId, string message)
        => new()
        {
            Number = number,
            Recipients = [groupId],
            Message = message
        };
}
