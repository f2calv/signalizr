# CasCap.Signalizr.Client.Testing

An in-memory [`ISignalizrClient`](https://github.com/f2calv/signalizr/blob/main/src/CasCap.Signalizr.Client/Abstractions/ISignalizrClient.cs) for .NET 10 tests of [signalizr](https://github.com/f2calv/signalizr)
consumers, without a gateway, Signal account or network connection.

## Installation

```bash
dotnet add package CasCap.Signalizr.Client.Testing
```

Reference it from test projects only, and match its version to `CasCap.Signalizr.Client`.

## Purpose

`FakeSignalizrClient` implements the full client contract in memory. A test feeds inbound
deliveries to the consumer's subscription, then asserts on every group operation the consumer
performed. There is no network, gRPC channel or Signal account involved.

**Target framework:** `net10.0`

## Public Surface

`FakeSignalizrClient` lives in the `CasCap.Signalizr.Client.Testing` namespace.

| Member | Purpose |
| --- | --- |
| `Groups` | Groups returned by `GetGroupsAsync`; empty by default |
| `Enqueue(message)` | Queues an inbound delivery for `SubscribeAsync` |
| `Complete()` | Ends the subscription cleanly, as a gateway restart would |
| `Attachments` | Bytes served by `GetAttachmentAsync`; an unknown identifier fails with a `404` `HttpRequestException` |
| `Sent`, `SentTo(group)` | Messages sent, with their attachments |
| `Reactions`, `RemovedReactions`, `ReactionCount(emoji)` | Reactions set and removed |
| `Polls`, `ClosedPolls` | Polls created and closed |
| `AttachmentFetches` | Attachment identifiers requested |
| `SubscribeCallCount`, `StartTypingCallCount`, `StopTypingCallCount`, `GetGroupsCallCount` | Call counters |
| `BlockStartTyping()`, `ReleaseStartTyping()` | Hold callers inside `StartTypingAsync`, for example to observe reply-queue backpressure |
| `GroupsFailures` | The number of upcoming `GetGroupsAsync` calls that fail with `HttpRequestException`, as if the gateway were unreachable |
| `InteractionFailure` | When set, reaction and typing calls fail with this exception instead of being recorded |

Returned timestamps and poll identifiers come from the optional `TimeProvider` constructor
argument, so a fake clock gives deterministic values. Reacting to a delivery that has no
`GroupName` throws `ArgumentException`, matching the real client.

## Usage

```csharp
using CasCap.Signalizr.Client;
using CasCap.Signalizr.Client.Testing;

var signalizr = new FakeSignalizrClient { Groups = ["My Test Group Name"] };

signalizr.Enqueue(new SignalizrMessage
{
    DeliveryId = "d1",
    GroupName = "My Test Group Name",
    Sender = "+10000000001",
    Message = "status?",
    Timestamp = 1,
});

signalizr.Complete();

await foreach (var message in signalizr.SubscribeAsync(cancellationToken))
    await signalizr.SendAsync(message.GroupName!, "Test reply", cancellationToken);

var reply = Xunit.Assert.Single(signalizr.SentTo("My Test Group Name"));
Xunit.Assert.Equal("Test reply", reply.Message);
```

In a consumer test, register the fake as `ISignalizrClient` before starting your application fixture,
then assert on its recorded operations. The example above demonstrates the fake's queue and recording
without starting an application.

## Limits

The fake does not model durable cursors, acknowledgement timeouts, retention, automatic reconnects
or Signal send validation. `Complete()` permanently closes its queue after queued deliveries drain;
later subscriptions end immediately. Use the gateway's
[source test guide](https://github.com/f2calv/signalizr/blob/main/src/CasCap.Signalizr.Tests/README.md)
for coverage of the real delivery contract.
