# CasCap.Signalizr.Client

Client for the [signalizr](https://github.com/f2calv/signalizr) gateway: send, react, show typing
and run polls on a named channel over REST, and subscribe to inbound messages over gRPC.

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

`SubscriberName` is the durable cursor key, not a process-instance identifier. Configure one name
per logical consumer and keep it stable across restarts. Do not append `Environment.MachineName`, a
pod name, process ID, or generated GUID: each new value creates a new cursor that begins at the
current tail, losing replay from the previous identity. Only one live stream may own a durable name;
use a stable replica identity only when each replica intentionally needs its own copy and cursor.

## Send

```csharp
var timestamp = await client.SendAsync("system", "deployment finished", cancellationToken);
```

Binary attachments use signal-cli-compatible data URIs and are MIME-agnostic:

```csharp
string[] attachments =
[
  $"data:audio/ogg;filename=reply.ogg;base64,{Convert.ToBase64String(audioBytes)}"
];
var timestamp = await client.SendAsync(
  "system",
  "voice reply",
  attachments,
  cancellationToken);
```

Channels are addressed by name. The gateway owns the Signal account, so a caller never names a
group, a group id or a sender number. A `404` means the channel is not configured on that gateway;
`GetChannelsAsync` lists the ones that are.

## Interact

Reactions, typing indicators and polls use the same channel names:

```csharp
// React to a delivered message; the gateway resolves its sender and timestamp.
await client.SetReactionAsync(message, "👀", cancellationToken);

// Show typing while work runs. The gateway keeps it alive and clears it if the stop never comes.
await client.StartTypingAsync("system", cancellationToken);
await client.StopTypingAsync("system", cancellationToken);

// React to a message this gateway sent by omitting the author.
var sent = await client.SendAsync("system", "done", cancellationToken);
await client.SetReactionAsync("system", "✅", long.Parse(sent, CultureInfo.InvariantCulture),
  cancellationToken: cancellationToken);

var pollId = await client.CreatePollAsync("system", "Deploy now?", ["Yes", "No"],
  cancellationToken: cancellationToken);
await client.ClosePollAsync("system", pollId, cancellationToken);
```

A vote arrives on the subscription as a `SignalizrMessage` whose `PollVote` names the poll and the
selected answer indexes.

`SignalizrMessage.FromSelf` marks messages the gateway's own account sent, so a consumer can ignore
its own traffic without knowing the account. On an account linked to a person's phone the owner's
messages are marked too; a consumer serving that owner must not drop them on this flag alone.

The account profile is shared by every channel, so the client cannot change it. The gateway applies
its configured profile name instead.

## Subscribe

```csharp
await foreach (var message in client.SubscribeAsync(cancellationToken))
{
  foreach (var attachment in message.Attachments)
  {
    var content = await client.GetAttachmentAsync(attachment.Id, cancellationToken);
    await ProcessAsync(attachment.ContentType, content, cancellationToken);
  }

    // Process it. Asking for the next message acknowledges this one.
}
```

Each message is acknowledged when the consumer asks for the next one, so an acknowledgement means
"the previous message was processed" rather than "it arrived". A consumer that stops enumerating
leaves the last message unacknowledged, which is correct: it may not have been processed.

The stream carries attachment descriptors rather than binary content. `GetAttachmentAsync` downloads
the raw bytes over REST. Content remains available until gateway retention removes the parent
message, preserving replay and independent-subscriber semantics.

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
