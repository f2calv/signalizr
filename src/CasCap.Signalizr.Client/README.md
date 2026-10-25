# CasCap.Signalizr.Client

Client for the [signalizr](https://github.com/f2calv/signalizr) gateway: send, react, show typing
and run polls on a named group over REST, and subscribe to inbound messages over gRPC from .NET 10.

## Installation

```bash
dotnet add package CasCap.Signalizr.Client
```

Use a package version compatible with your gateway; the client and gateway share the repository
version.

## Configuration Examples

Minimal local gateway configuration; the addresses default to these loopback endpoints, but the
subscriber name must be set explicitly:

```json
{
  "CasCap": {
    "SignalizrClientConfig": {
      "BaseAddress": "http://localhost:8080",
      "GrpcAddress": "http://localhost:5001",
      "SubscriberName": "example-consumer"
    }
  }
}
```

For a gateway reachable at a service address, set both endpoints explicitly:

```json
{
  "CasCap": {
    "SignalizrClientConfig": {
      "BaseAddress": "http://gateway.example.com",
      "GrpcAddress": "http://gateway.example.com:5001",
      "SubscriberName": "example-consumer"
    }
  }
}
```

Register with the application's existing configuration and resolve [`ISignalizrClient`](https://github.com/f2calv/signalizr/blob/main/src/CasCap.Signalizr.Client/Abstractions/ISignalizrClient.cs) where needed:

```csharp
using CasCap.Signalizr.Client;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddSignalizrClient(builder.Configuration);
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
var timestamp = await client.SendAsync("My Test Group Name", "deployment finished", cancellationToken);
```

Binary attachments use signal-cli-compatible data URIs and are MIME-agnostic:

```csharp
string[] attachments =
[
  $"data:audio/ogg;filename=reply.ogg;base64,{Convert.ToBase64String(audioBytes)}"
];
var timestamp = await client.SendAsync(
  "My Test Group Name",
  "voice reply",
  attachments,
  cancellationToken);
```

Groups are addressed by exact Signal group display name, preserving case and spaces.
The client URL-encodes names in the `groupName` query parameter. The gateway owns the Signal account,
so a caller never supplies a group id or sender number. A `404` on sending means the group is not
configured or the Gateway role is absent; `GetGroupsAsync` lists configured names and throws
`SignalizrRoleNotEnabledException` when the send surface is absent.
An uncertain send must not be retried automatically: it may already have reached Signal.

## Interact

Reactions, typing indicators and polls use the same group names:

```csharp
// React to a delivered message; the gateway resolves its sender and timestamp.
await client.SetReactionAsync(message, "👀", cancellationToken);

// Show typing while work runs. The gateway keeps it alive and clears it if the stop never comes.
await client.StartTypingAsync("My Test Group Name", cancellationToken);
await client.StopTypingAsync("My Test Group Name", cancellationToken);

// React to a message this gateway sent by omitting the author.
var sent = await client.SendAsync("My Test Group Name", "done", cancellationToken);
await client.SetReactionAsync("My Test Group Name", "✅",
  long.Parse(sent, System.Globalization.CultureInfo.InvariantCulture),
  cancellationToken: cancellationToken);

var pollId = await client.CreatePollAsync("My Test Group Name", "Deploy now?", ["Yes", "No"],
  cancellationToken: cancellationToken);
await client.ClosePollAsync("My Test Group Name", pollId, cancellationToken);
```

A vote arrives on the subscription as a `SignalizrMessage` whose `PollVote` names the poll and the
selected answer indexes.

Reactions and typing indicators are feedback about work, not the work itself. The `Try*` extension
methods report an unreachable gateway or a client timeout instead of throwing, so a missed
indicator cannot stop a consumer. Cancellation you requested still propagates:

```csharp
await client.TrySetReactionAsync("My Test Group Name", "⏳", message.Timestamp, message.Sender,
  onFailure: ex => logger.LogWarning(ex, "reaction not shown"), cancellationToken: cancellationToken);
await client.TryStartTypingAsync("My Test Group Name", cancellationToken: cancellationToken);
await client.TryStopTypingAsync("My Test Group Name", cancellationToken: cancellationToken);
```

At startup, `WaitForGroupsAsync` retries an unreachable gateway until it answers. It then throws
`SignalizrGroupNotConfiguredException` if a required group is missing, and returns the optional
groups it does not serve:

```csharp
var missingOptional = await client.WaitForGroupsAsync(
  requiredGroups: ["My Test Group Name"],
  optionalGroups: ["My Test Monitor Group Name"],
  retryDelay: TimeSpan.FromSeconds(2),
  cancellationToken: cancellationToken);
```

The exception message omits the group names, which can carry private information.

`SignalizrMessage.FromSelf` marks messages the gateway's own account sent, so a consumer can ignore
its own traffic without knowing the account. On an account linked to a person's phone the owner's
messages are marked too; a consumer serving that owner must not drop them on this flag alone.

The account profile is shared by every group, so the client cannot change it. The gateway applies
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

    await HandleAsync(message, cancellationToken);
}
```

`ProcessAsync` and `HandleAsync` represent your application's processing; finish it before advancing
the iterator.

Each message is acknowledged when the consumer asks for the next one, so an acknowledgement means
"the previous message was processed" rather than "it arrived". A consumer that stops enumerating
leaves the last message unacknowledged, which is correct: it may not have been processed.
Closing the request stream ends the subscription and releases its live identity; a request-stream
failure also terminates the response loop. Unacknowledged deliveries remain eligible for replay.

Subscriptions do not select or authorize a group on the server. Inspect `GroupName` before handling
a delivery; it can be null for direct messages or messages outside the configured groups.

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
            await HandleAsync(message, cancellationToken);
    }
    catch (Grpc.Core.RpcException ex) when (!cancellationToken.IsCancellationRequested)
    {
        logger.LogWarning(ex, "subscription ended, reconnecting");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}
```

The gRPC transport limits connection establishment to 10 seconds. While a subscription is active,
it sends an HTTP/2 keepalive ping after 60 seconds without incoming frames and closes the connection
if the peer does not answer within another 30 seconds. This surfaces a half-open connection to the
caller's reconnect loop without imposing a maximum subscription lifetime. The gateway or any
HTTP/2 intermediary must support keepalive pings. These limits do not change REST send behavior or
automatically replay sends.

## Privacy

`SignalizrMessage.Sender` is a Signal identifier and `Message` is someone's content. Neither belongs
in a log, a metric label or a trace attribute.
REST and gRPC are not authenticated by this client or by the gateway. A subscription receives all
configured groups; filtering locally is not an authorization boundary. Use a trusted network or
secure transport boundary. MCP Basic credentials are unrelated to these client endpoints.

## Testing

The companion [CasCap.Signalizr.Client.Testing guide](https://github.com/f2calv/signalizr/blob/main/src/CasCap.Signalizr.Client.Testing/README.md)
package provides `FakeSignalizrClient`. It is an in-memory implementation that feeds deliveries to
a consumer and records every group operation, for test projects that need no gateway.
