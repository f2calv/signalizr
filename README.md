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
