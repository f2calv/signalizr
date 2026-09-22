# CasCap.Signalizr.Client

Client for the [signalizr](https://github.com/f2calv/signalizr) gateway: send to a named channel
over REST, subscribe to inbound messages over gRPC.

## Install

```bash
dotnet add package CasCap.Signalizr.Client
```

## Register

```csharp
builder.Services.AddSignalizrClient(builder.Configuration);
```

Binding `CasCap:SignalizrClientConfig`:

```json
{
  "CasCap": {
    "SignalizrClientConfig": {
      "BaseAddress": "http://signalizr:80",
      "GrpcAddress": "http://signalizr:5001",
      "SubscriberName": "my-app"
    }
  }
}
```

Two addresses because the gateway serves REST and gRPC on separate ports: a plaintext endpoint
cannot negotiate protocols without TLS, so one port answers HTTP/1.1 and another HTTP/2.

## Send

```csharp
var timestamp = await client.SendAsync("system", "deployment finished", cancellationToken);
```

Channels are addressed by name. The gateway owns the Signal account, so a caller never names a
group, a group id or a sender number. A `404` means the channel is not configured on that gateway;
`GetChannelsAsync` lists the ones that are.

## Subscribe

```csharp
await foreach (var message in client.SubscribeAsync(cancellationToken))
{
    // Process it. Asking for the next message acknowledges this one.
}
```

Each message is acknowledged when the consumer asks for the next one, so an acknowledgement means
"the previous message was processed" rather than "it arrived". A consumer that stops enumerating
leaves the last message unacknowledged, which is correct: it may not have been processed.

The gateway pauses delivery once too many messages are unacknowledged, and disconnects a subscriber
that stops acknowledging altogether. Neither silently drops anything.

**The stream is not resubscribed automatically.** A caller that wants to survive a gateway restart
must loop, which keeps the reconnect visible rather than hiding the gap in delivery it creates:

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    try
    {
        await foreach (var message in client.SubscribeAsync(cancellationToken))
            Handle(message);
    }
    catch (RpcException ex)
    {
        logger.LogWarning(ex, "subscription ended, reconnecting");
        await Task.Delay(delay, cancellationToken);
    }
}
```

## Privacy

`SignalizrMessage.Sender` is a Signal identifier and `Message` is someone's content. Neither belongs
in a log, a metric label or a trace attribute.
