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
        string groupName,
        string message,
        CancellationToken cancellationToken = default) =>
        SendAsync(groupName, message, base64Attachments: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<string> SendAsync(
        string groupName,
        string message,
        IReadOnlyList<string>? base64Attachments,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .PostAsJsonAsync($"api/v1/groups/{Uri.EscapeDataString(groupName)}/messages",
                new { message, base64Attachments }, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new HttpRequestException(
                $"Group '{groupName}' is not configured on this gateway, or it does not run the " +
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
    public async Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("api/v1/groups", cancellationToken).ConfigureAwait(false);

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
        string groupName,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Post, groupName, "reactions",
            JsonContent.Create(new { reaction, targetTimestamp, targetAuthor }), cancellationToken);

    /// <inheritdoc/>
    public Task RemoveReactionAsync(
        string groupName,
        string reaction,
        long targetTimestamp,
        string? targetAuthor = null,
        CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Delete, groupName, "reactions",
            JsonContent.Create(new { reaction, targetTimestamp, targetAuthor }), cancellationToken);

    /// <inheritdoc/>
    public Task SetReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Post, RequireGroup(message), DeliveryReactionsPath(message),
            JsonContent.Create(new { reaction }), cancellationToken);

    /// <inheritdoc/>
    public Task RemoveReactionAsync(SignalizrMessage message, string reaction, CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Delete, RequireGroup(message), DeliveryReactionsPath(message),
            JsonContent.Create(new { reaction }), cancellationToken);

    /// <inheritdoc/>
    public Task StartTypingAsync(string groupName, CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Put, groupName, "typing", content: null, cancellationToken);

    /// <inheritdoc/>
    public Task StopTypingAsync(string groupName, CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Delete, groupName, "typing", content: null, cancellationToken);

    /// <inheritdoc/>
    public async Task<string> CreatePollAsync(
        string groupName,
        string question,
        IReadOnlyList<string> answers,
        bool allowMultipleSelections = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendGroupRequestCoreAsync(HttpMethod.Post, groupName, "polls",
            JsonContent.Create(new { question, answers, allowMultipleSelections }), cancellationToken)
            .ConfigureAwait(false);

        var result = await response.Content
            .ReadFromJsonAsync<PollResult>(cancellationToken)
            .ConfigureAwait(false);

        return result?.PollId
            ?? throw new HttpRequestException("The gateway returned no identifier for the poll.");
    }

    /// <inheritdoc/>
    public Task ClosePollAsync(string groupName, string pollId, CancellationToken cancellationToken = default) =>
        SendGroupRequestAsync(HttpMethod.Delete, groupName, $"polls/{Uri.EscapeDataString(pollId)}",
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
                GroupName = string.IsNullOrEmpty(message.GroupName) ? null : message.GroupName,
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

    private async Task SendGroupRequestAsync(
        HttpMethod method,
        string groupName,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendGroupRequestCoreAsync(method, groupName, path, content, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendGroupRequestCoreAsync(
        HttpMethod method,
        string groupName,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"api/v1/groups/{Uri.EscapeDataString(groupName)}/{path}")
        {
            Content = content
        };
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new HttpRequestException(
                $"Group '{groupName}' is not configured on this gateway, or it does not run the " +
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

    private static string RequireGroup(SignalizrMessage message) =>
        message.GroupName ?? throw new ArgumentException(
            "The message arrived on no configured groupName, so there is nothing to react through.", nameof(message));

    private static string DeliveryReactionsPath(SignalizrMessage message) =>
        $"messages/{Uri.EscapeDataString(message.DeliveryId)}/reactions";

    private sealed record SendResult(string GroupName, string Timestamp);

    private sealed record PollResult(string GroupName, string PollId);
}
