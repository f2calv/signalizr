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
    public async Task<string> SendAsync(
        string channel, string message, CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .PostAsJsonAsync($"api/v1/channels/{Uri.EscapeDataString(channel)}/messages",
                new { message }, cancellationToken)
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
    public async IAsyncEnumerable<SignalizrMessage> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var call = inboundClient.Subscribe(cancellationToken: cancellationToken);

        await call.RequestStream
            .WriteAsync(new SubscribeRequest { Hello = new Hello { SubscriberName = config.Value.SubscriberName } },
                cancellationToken)
            .ConfigureAwait(false);

        // Acknowledge the previous message only once the consumer asks for the next one, so an ack
        // means "processed" rather than "received". A consumer that stops enumerating leaves the
        // last message unacknowledged, which is the honest outcome.
        string? unacknowledged = null;

        await foreach (var message in call.ResponseStream
            .ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (unacknowledged is not null)
            {
                await call.RequestStream
                    .WriteAsync(new SubscribeRequest { Ack = new Ack { DeliveryId = unacknowledged } },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            unacknowledged = message.DeliveryId;

            yield return new SignalizrMessage
            {
                DeliveryId = message.DeliveryId,
                Channel = string.IsNullOrEmpty(message.Channel) ? null : message.Channel,
                Sender = string.IsNullOrEmpty(message.Sender) ? null : message.Sender,
                Message = message.Message,
                Timestamp = message.Timestamp
            };
        }
    }

    private sealed record SendResult(string Channel, string Timestamp);
}
