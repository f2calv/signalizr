# signalizr

A Signal Messenger **gateway** — a single, controlled owner of one Signal account that other
applications send through and subscribe to, instead of each one holding its own connection.

> **Status: scaffolding.** This repository currently contains repository conventions only. No
> application code, container image, chart or package has been written yet. Everything below
> describes the intended shape, not shipped behaviour.

## Why

[signal-cli](https://github.com/AsamK/signal-cli) and the
[signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api) wrapper make it possible for
an application to drive a Signal account. That works well for one application and degrades badly for
several:

- **Account safety.** Signal enforces send limits. Several independent processes driving one account
  can trip them, and a throttled account takes out every consumer at once. A single owner can queue,
  rate-limit, de-duplicate and apply backpressure.
- **Receive reliability.** The wrapper broadcasts each inbound message to every connected receive
  socket over an unbuffered channel with a non-blocking send. A consumer that is not parked inside a
  receive at that instant simply misses the message, with no retry and no error. More subscribers
  means more silent loss, for everyone.
- **Duplication.** The same polling, reconnect, filtering and routing code ends up reimplemented in
  every consuming application.

signalizr exists to be the only process talking to the account, exposing a small contract to
everything else.

## Gateway, not proxy

signalizr does not forward the upstream API. It translates a **named-channel** contract onto it,
inverts the inbound delivery topology, and owns account policy. Callers address a channel by name and
never see a phone number or a group id. Arbitrary pass-through is deliberately not offered — that is
the point of having a single owner.

## One image, two roles

A single container image, with the role selected by feature flag:

| Feature | Default | Role |
| --- | --- | --- |
| `Gateway` | on | REST send surface, gRPC subscription surface, named-channel resolution |
| `Receiver` | on | Owns the single receive stream and fans out to subscribers |
| `DemoClient` | off | Sample thin client, for evaluating the gateway without writing one |
| `Mcp` | off | Model Context Protocol surface |

## Transports

| Surface | Transport | Rationale |
| --- | --- | --- |
| Send | REST | `curl`-able, webhook-able, and usable without generating a client |
| Inbound subscription | gRPC bidirectional streaming | Long-lived and typed, with an ack channel so delivery is not fire-and-forget |

Send is not duplicated across both transports.

## Sending

The send surface is addressed by channel name. A caller never names a group, a group id or a
sender number, because the gateway owns the account.

```bash
curl -X POST http://localhost:8080/api/v1/channels/system/messages \
  -H 'content-type: application/json' \
  -d '{"message":"deployment finished"}'
```

```json
{ "channel": "system", "timestamp": "1758518400000" }
```

`GET /api/v1/channels` lists the channels currently resolved to a group, which is the quickest way
to check configuration. It returns names only.

| Status | Meaning |
| --- | --- |
| `200` | Sent. The timestamp identifies the message for a later reaction, receipt or edit |
| `400` | The message was missing or empty |
| `404` | No such channel, or the role serving this request is not the gateway |
| `502` | The signal-cli wrapper could not be reached; retry |

`404` covers both an unknown channel and a non-gateway role, because a pod that does not run the
gateway does not route these paths at all. The unknown-channel body lists the configured channels.

## Receiving

The upstream broadcast is lossy by construction: an unbuffered channel with a non-blocking send, so
a consumer that is not parked inside a receive at that instant misses the message, with no retry and
no error. Everything below follows from that.

One process owns the receive stream. Its loop does nothing per message except put it on a bounded
queue — no resolution, no outbound call, no dispatch — because any work done there happens while the
upstream is not being read.

The queue drops rather than blocks when full. Blocking the writer would push back onto the receive
loop and lose messages upstream, where nothing can count them; dropping loses them here, where the
count is reported. It drops the oldest, on the basis that a stale message is worth less than a
current one. Size it with `CasCap:ReceiverConfig:QueueCapacity`.

A separate dispatcher drains that queue and fans each message out to connected subscribers. Keeping
it separate is the point: resolving the channel and delivering to subscribers is per-message work,
and doing it in the receive loop would stop the upstream being read.

## Subscribing

`Subscribe` is a bidirectional stream on `signalizr.v1.Inbound`. A subscriber sends `Hello` once,
then an `Ack` per message; the server streams `InboundMessage`.

Bidirectional rather than server-streaming because the acknowledgement is what makes delivery
observable. A fire-and-forget stream would let the server treat a message as delivered the moment it
was written to the socket, which is the upstream wrapper's defect reproduced one layer up.

Two limits protect the dispatcher from one slow subscriber, and both fail loudly:

| Setting | Effect when exceeded |
| --- | --- |
| `CasCap:SubscriberConfig:QueueCapacity` | The subscriber is disconnected rather than having messages dropped |
| `CasCap:SubscriberConfig:MaxOutstanding` | Delivery pauses until acknowledgements arrive |
| `CasCap:SubscriberConfig:AckTimeoutMs` | The stream ends with `DEADLINE_EXCEEDED` |

Each subscriber receives its own `delivery_id` for the same message, so one subscriber's
acknowledgement can never clear another's.

### Ports

gRPC listens on its own port. A plaintext endpoint cannot negotiate protocols, because there is no
ALPN without TLS, so one port answers HTTP/1.1 or HTTP/2 but never both — sharing one fails at the
first gRPC call with `HTTP_1_1_REQUIRED`.

| Port | Protocol | Serves |
| --- | --- | --- |
| `CasCap:GrpcHostConfig:Http1Port`, default `8080` | HTTP/1.1 | REST and the health probes |
| `CasCap:GrpcHostConfig:Http2Port`, default `5001` | HTTP/2 | gRPC |

Both are configurable so several applications can run side by side locally. Only the Receiver role
opens the gRPC port, because only it holds the inbound stream.

## Deployment

Two first-class targets, sharing one configuration shape:

- **Docker Compose** — the quickstart. Brings up the Signal REST wrapper, the gateway and a demo
  client together.
- **Helm** — an umbrella chart pairing the upstream wrapper with the gateway.

Configuration is loaded through the standard provider chain, so the same JSON works whether it
arrives as a mounted file, a projected ConfigMap key, or environment variables.

## Privacy

Phone numbers are personal data and are treated as secrets: they belong in a Kubernetes Secret or a
local environment file, never in a committed values file, and never in a log field, metric label or
trace attribute. Resolved Signal group ids are account-linked identifiers and are likewise never
committed — they are resolved at runtime from group names. Everything tracked in this repository uses
placeholders.

## Related

| Project | Role |
| --- | --- |
| [signal-cli](https://github.com/AsamK/signal-cli) | Registers as a Signal client |
| [signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api) | REST and JSON-RPC wrapper around signal-cli |
| [CasCap.Api.SignalCli](https://github.com/f2calv/CasCap.Api.SignalCli) | .NET client for the wrapper, consumed by this gateway |

## License

Released into the public domain under [The Unlicense](LICENSE).
