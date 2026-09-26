using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using CasCap.Grpc;
using CasCap.Signalizr.Client.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace CasCap.Signalizr.Client;

/// <inheritdoc cref="ISignalizrClient"/>
public sealed class SignalizrClient(
    HttpClient httpClient,
    Inbound.InboundClient inboundClient,
    IOptions<SignalizrClientConfig> config) : ISignalizrClient
{
    /// <inheritdoc/>
    public Task<string> SendAsync(
        string channel,
        string message,
        CancellationToken cancellationToken = default) =>
        SendAsync(channel, message, base64Attachments: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<string> SendAsync(
        string channel,
        string message,
        IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .PostAsJsonAsync($"api/v1/channels/{Uri.EscapeDataString(channel)}/messages",
                new { message, base64Attachments }, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new HttpRequestException(
                $"Channel '{channel}' is not configured on this gateway, or it does not run the " +
                "Gateway role.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<SendResult>(cancellationToken)
            .ConfigureAwait(false);

        return result?.Timestamp
            ?? throw new HttpRequestException("The gateway returned no timestamp for the send.");
    }

    /// <inheritdoc/>
    public Task<byte[]> GetAttachmentAsync(
        string attachmentId,
        CancellationToken cancellationToken = default) =>
        httpClient.GetByteArrayAsync(
            $"api/v1/attachments/{Uri.EscapeDataString(attachmentId)}",
            cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("api/v1/channels", cancellationToken).ConfigureAwait(false);

        // A gateway that does not run the Gateway role does not route this path at all, which is a
        // different problem from being unreachable and must not be reported as one.
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new SignalizrRoleNotEnabledException(
                "This gateway does not serve the send surface. The Gateway role is not enabled on it.");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<List<string>>(cancellationToken)
            .ConfigureAwait(false) ?? [];
    }

    /// <inheritdoc/>
    public Task SetReactionAsync(
        string channel,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Post, channel, "reactions",
            JsonContent.Create(new { reaction, targetTimestamp, targetAuthor }), cancellationToken);

    /// <inheritdoc/>
    public Task RemoveReactionAsync(
        string channel,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Delete, channel, "reactions",
            JsonContent.Create(new { reaction, targetTimestamp, targetAuthor }), cancellationToken);

    /// <inheritdoc/>
    public Task SetReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Post, RequireChannel(message), DeliveryReactionsPath(message),
            JsonContent.Create(new { reaction }), cancellationToken);

    /// <inheritdoc/>
    public Task RemoveReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Delete, RequireChannel(message), DeliveryReactionsPath(message),
            JsonContent.Create(new { reaction }), cancellationToken);

    /// <inheritdoc/>
    public Task StartTypingAsync(string channel, CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Put, channel, "typing", content: null, cancellationToken);

    /// <inheritdoc/>
    public Task StopTypingAsync(string channel, CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Delete, channel, "typing", content: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<string> CreatePollAsync(
        string channel,
        string question,
        IReadOnlyList<string> answers,
        bool allowMultipleSelections = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendChannelRequestCoreAsync(HttpMethod.Post, channel, "polls",
            JsonContent.Create(new { question, answers, allowMultipleSelections }), cancellationToken)
            .ConfigureAwait(false);

        var result = await response.Content
            .ReadFromJsonAsync<PollResult>(cancellationToken)
            .ConfigureAwait(false);

        return result?.PollId
            ?? throw new HttpRequestException("The gateway returned no identifier for the poll.");
    }

    /// <inheritdoc/>
    public Task ClosePollAsync(string channel, string pollId, CancellationToken cancellationToken = default) =>
        SendChannelRequestAsync(HttpMethod.Delete, channel, $"polls/{Uri.EscapeDataString(pollId)}",
            content: null, cancellationToken);

    /// <inheritdoc/>
    public async IAsyncEnumerable<SignalizrMessage> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var call = inboundClient.Subscribe(cancellationToken: cancellationToken);

        await call.RequestStream
            .WriteAsync(new SubscribeRequest { Hello = new Hello { SubscriberName = config.Value.SubscriberName } },
                cancellationToken)
            .ConfigureAwait(false);

        await foreach (var message in call.ResponseStream
            .ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new SignalizrMessage
            {
                DeliveryId = message.DeliveryId,
                Channel = string.IsNullOrEmpty(message.Channel) ? null : message.Channel,
                Sender = string.IsNullOrEmpty(message.Sender) ? null : message.Sender,
                Message = message.Message,
                Timestamp = message.Timestamp,
                Attachments = [.. message.Attachments.Select(attachment => new SignalizrAttachment
                {
                    Id = attachment.Id,
                    ContentType = string.IsNullOrEmpty(attachment.ContentType) ? null : attachment.ContentType,
                    Filename = string.IsNullOrEmpty(attachment.Filename) ? null : attachment.Filename,
                    Size = attachment.Size
                })],
                FromSelf = message.FromSelf,
                PollVote = message.PollVote is { } vote
                    ? new SignalizrPollVote
                    {
                        PollId = vote.PollTimestamp.ToString(CultureInfo.InvariantCulture),
                        OptionIndexes = [.. vote.OptionIndexes]
                    }
                    : null
            };

            // Execution resumes here only when the consumer asks for the next item, so this means
            // "processed" rather than merely "received". Send it before waiting for the next
            // response: with MaxOutstanding=1 the server cannot send that response until this ack.
            await call.RequestStream
                .WriteAsync(new SubscribeRequest { Ack = new Ack { DeliveryId = message.DeliveryId } },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SendChannelRequestAsync(
        HttpMethod method,
        string channel,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendChannelRequestCoreAsync(method, channel, path, content, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendChannelRequestCoreAsync(
        HttpMethod method,
        string channel,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"api/v1/channels/{Uri.EscapeDataString(channel)}/{path}")
        {
            Content = content
        };
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new HttpRequestException(
                $"Channel '{channel}' is not configured on this gateway, or it does not run the " +
                "Gateway role.", null, HttpStatusCode.NotFound);
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            response.Dispose();
            throw;
        }

        return response;
    }

    private static string RequireChannel(SignalizrMessage message) =>
        message.Channel ?? throw new ArgumentException(
            "The message arrived on no configured channel, so there is nothing to react through.", nameof(message));

    private static string DeliveryReactionsPath(SignalizrMessage message) =>
        $"messages/{Uri.EscapeDataString(message.DeliveryId)}/reactions";

    private sealed record SendResult(string Channel, string Timestamp);

    private sealed record PollResult(string Channel, string PollId);
}
