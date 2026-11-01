using CasCap.Diagnostics;
using CasCap.Exceptions;
using CasCap.Grpc;
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
        catch (SubscriberNotPermittedException ex)
        {
            operatorNotifier.Notify($"{name} refused: no configured groups");
            throw new RpcException(new Status(StatusCode.PermissionDenied, ex.Message));
        }

        using var ownedSubscription = subscription;
        var ackTimeout = TimeSpan.FromMilliseconds(config.Value.AckTimeoutMs);
        using var streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Acknowledgements arrive independently of deliveries, so they are read concurrently.
        var acknowledgements = ReadAcknowledgementsAsync(requestStream, subscription, streamCancellation.Token);
        var deliveries = DeliverAsync(responseStream, subscription, ackTimeout, streamCancellation.Token);
        var expiry = MonitorAcknowledgementsAsync(subscription, ackTimeout, streamCancellation.Token);

        try
        {
            await Task.WhenAny(acknowledgements, deliveries, expiry).ConfigureAwait(false);
            await streamCancellation.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(acknowledgements, deliveries, expiry).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (streamCancellation.IsCancellationRequested)
        {
            // The client went away or the host is shutting down. Both are ordinary ends to a
            // long-lived subscription, not failures to report.
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded)
        {
            metrics.RecordAcknowledgementTimeout();
            operatorNotifier.Notify($"{name} stopped acknowledging within {ackTimeout}; disconnecting it");
            throw;
        }
        finally
        {
            registry.Unsubscribe(subscription);
        }
    }

    private async Task DeliverAsync(
        IServerStreamWriter<InboundMessage> responseStream,
        InboundSubscription subscription,
        TimeSpan ackTimeout,
        CancellationToken cancellationToken)
    {
        await foreach (var delivery in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await subscription.TryReserveAsync(
                delivery.DeliveryId, ackTimeout, cancellationToken).ConfigureAwait(false))
                throw AcknowledgementTimeout(ackTimeout);

            await responseStream.WriteAsync(ToMessage(delivery), cancellationToken).ConfigureAwait(false);
            metrics.RecordDelivered();
        }
    }

    private static async Task MonitorAcknowledgementsAsync(
        InboundSubscription subscription,
        TimeSpan ackTimeout,
        CancellationToken cancellationToken)
    {
        await subscription.WaitForAcknowledgementTimeoutAsync(ackTimeout, cancellationToken).ConfigureAwait(false);
        throw AcknowledgementTimeout(ackTimeout);
    }

    private static RpcException AcknowledgementTimeout(TimeSpan ackTimeout) =>
        new(new Status(StatusCode.DeadlineExceeded,
            $"No acknowledgement within {ackTimeout}. The subscriber is receiving but not acknowledging."));

    private static async Task ReadAcknowledgementsAsync(
        IAsyncStreamReader<SubscribeRequest> requestStream,
        InboundSubscription subscription,
        CancellationToken cancellationToken)
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

    private static InboundMessage ToMessage(InboundDelivery delivery)
    {
        var message = new InboundMessage
        {
            DeliveryId = delivery.DeliveryId,
            GroupName = delivery.GroupName ?? string.Empty,
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
