using CasCap.Abstractions;
using CasCap.Grpc;
using CasCap.Models;
using CasCap.Models.Dtos;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace CasCap.Services;

/// <summary>Serves the inbound subscription stream.</summary>
/// <remarks>
/// The stream is bidirectional so a subscriber acknowledges each message. Delivery pauses once
/// <see cref="SubscriberConfig.MaxOutstanding"/> messages are unacknowledged, which is what stops a
/// subscriber that reads the socket but never processes anything from being sent more work.
/// </remarks>
public sealed class InboundGrpcService(
    ILogger<InboundGrpcService> logger,
    IInboundSubscriberRegistry registry,
    IOptions<SubscriberConfig> config) : Inbound.InboundBase
{
    public override async Task Subscribe(
        IAsyncStreamReader<SubscribeRequest> requestStream,
        IServerStreamWriter<InboundMessage> responseStream,
        ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        // The first frame names the subscriber. Anything else means the client is not speaking
        // this contract, so fail immediately rather than serving an unidentifiable stream.
        if (!await requestStream.MoveNext(cancellationToken).ConfigureAwait(false) ||
            requestStream.Current.PayloadCase is not SubscribeRequest.PayloadOneofCase.Hello)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "The first message must be Hello."));
        }

        var name = requestStream.Current.Hello.SubscriberName;
        if (string.IsNullOrWhiteSpace(name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "SubscriberName is required."));

        using var subscription = registry.Subscribe(name);
        var ackTimeout = TimeSpan.FromMilliseconds(config.Value.AckTimeoutMs);
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Acknowledgements arrive independently of deliveries, so they are read concurrently.
        var acknowledgements = ReadAcknowledgementsAsync(requestStream, subscription, streamCancellation.Token);

        try
        {
            await foreach (var delivery in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await subscription.TryReserveAsync(
                    delivery.DeliveryId, ackTimeout, cancellationToken).ConfigureAwait(false))
                {
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                        $"No acknowledgement within {ackTimeout}. The subscriber is receiving but not acknowledging."));
                }

                // The Grpc.Core compatibility contract does not support cancellable server writes;
                // cancellation closes the call through ServerCallContext instead.
                await responseStream.WriteAsync(ToMessage(delivery)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away or the host is shutting down. Both are ordinary ends to a
            // long-lived subscription, not failures to report.
        }
        catch (SubscriberFellBehindException ex)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, ex.Message));
        }
        finally
        {
            registry.Unsubscribe(subscription);
            await streamCancellation.CancelAsync().ConfigureAwait(false);
            await acknowledgements.ConfigureAwait(false);
        }
    }

    private async Task ReadAcknowledgementsAsync(
        IAsyncStreamReader<SubscribeRequest> requestStream,
        InboundSubscription subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                if (requestStream.Current.PayloadCase is SubscribeRequest.PayloadOneofCase.Ack)
                    subscription.Acknowledge(requestStream.Current.Ack.DeliveryId);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation ends the acknowledgement reader in step with the delivery loop.
        }
        catch (Exception ex)
        {
            // The subscriber's half of the stream ended abnormally. The delivery loop already
            // stops when its budget runs out, so this only needs recording.
            logger.LogDebug(ex, "{ClassName} acknowledgement stream for {Subscriber} ended",
                nameof(InboundGrpcService), subscription.Name);
        }
    }

    private static InboundMessage ToMessage(InboundDelivery delivery) => new()
    {
        DeliveryId = delivery.DeliveryId,
        Channel = delivery.Channel ?? string.Empty,
        Sender = delivery.Sender ?? string.Empty,
        Message = delivery.Message ?? string.Empty,
        Timestamp = delivery.Timestamp ?? 0
    };
}
