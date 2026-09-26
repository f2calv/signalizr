using CasCap.Abstractions;
using CasCap.Exceptions;
using CasCap.Diagnostics;
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
    SignalizrMetrics metrics,
    IOptions<SubscriberConfig> config,
    IOperatorNotifier operatorNotifier) : Inbound.InboundBase
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

        InboundSubscription subscription;
        try
        {
            subscription = await registry.SubscribeAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (SubscriberAlreadyConnectedException ex)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
        }

        using var ownedSubscription = subscription;
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
                    metrics.RecordAcknowledgementTimeout();
                    operatorNotifier.Notify($"{name} stopped acknowledging within {ackTimeout}; disconnecting it");
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                        $"No acknowledgement within {ackTimeout}. The subscriber is receiving but not acknowledging."));
                }

                await responseStream.WriteAsync(ToMessage(delivery)).ConfigureAwait(false);
                metrics.RecordDelivered();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client went away or the host is shutting down. Both are ordinary ends to a
            // long-lived subscription, not failures to report.
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
                {
                    await subscription.AcknowledgeAsync(
                        requestStream.Current.Ack.DeliveryId,
                        cancellationToken).ConfigureAwait(false);
                }
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

    private static InboundMessage ToMessage(InboundDelivery delivery)
    {
        var message = new InboundMessage
        {
            DeliveryId = delivery.DeliveryId,
            Channel = delivery.Channel ?? string.Empty,
            Sender = delivery.Sender ?? string.Empty,
            Message = delivery.Message ?? string.Empty,
            Timestamp = delivery.Timestamp ?? 0,
            FromSelf = delivery.FromSelf
        };
        if (delivery.PollVote is { } vote)
        {
            message.PollVote = new PollVote { PollTimestamp = vote.PollTimestamp };
            message.PollVote.OptionIndexes.AddRange(vote.OptionIndexes);
        }
        message.Attachments.AddRange(delivery.Attachments.Select(attachment => new CasCap.Grpc.InboundAttachment
        {
            Id = attachment.Id,
            ContentType = attachment.ContentType ?? string.Empty,
            Filename = attachment.Filename ?? string.Empty,
            Size = attachment.Size
        }));
        return message;
    }
}
