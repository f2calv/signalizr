# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the
[account-level .github repository](https://github.com/f2calv/.github). They are deliberately not
copied here. Clone that repository and add it to the VS Code workspace, or link its instruction
folders into `~/.copilot/`. If the shared files are unavailable, stop rather than guessing the
conventions.

Everything below is specific to this repository.

## Current Baseline

The gateway sends over REST, owns the inbound receive stream and fans it out over a bidirectional
gRPC subscription. The container image, Helm chart and `CasCap.Signalizr.Client` package all build
and are exercised in CI.

Both directions are proven against one registered account, deployed in a cluster. Scale, long-run
stability, multiple subscribers and recovery from a wrapper outage are not. Do not describe the
unbuilt parts — the MCP role, and any redelivery or persistence — as though they exist.

The gateway links to an existing personal account rather than owning a dedicated number, so the
account's own messages arrive as `syncMessage.sentMessage` rather than `dataMessage`. Anything that
filters inbound envelopes must handle both, or the gateway goes blind to its owner.

## Open Source Boundary

signalizr must be usable by a stranger with no access to any private repository.

- Never reference a private repository, a deployment environment, a cluster, a namespace, a manifest
  location or an operational procedure in tracked files, commits, issues or pull requests.
- The Compose quickstart and the chart must both work from a clean clone with public inputs only.
- Describe any private dependency generically and supply its coordinates through configuration.

## Gateway, Not Proxy

Use **gateway** consistently — repository, image, chart, documentation and code. signalizr does not
forward the upstream API; it translates a named-channel contract onto it and owns account policy.

- The send path is a **gateway endpoint**, the inbound path is a **dispatcher** or **fan-out**.
- Do not add an arbitrary pass-through route to the upstream wrapper. Single ownership of the account
  is the reason this service exists.

## One Image, Two Roles

A single image whose role is selected by feature flag, following the account's established pattern:
a `FeatureNames` constant class whose valid names are derived by reflection, `IBgFeature`
implementations registered inside `if (enabledFeatures.Contains(...))` blocks, and controllers gated
with `[FeatureController(...)]` so a disabled feature returns 404 rather than a dependency-injection
failure.

- Keep `DemoClient` small enough that shipping it inside the product image stays justified. If it
  grows its own dependencies or an inbound surface, split it out then.
- Do not add a second image or a `samples/` demo project for the demo client.

## Transports

- **Send is REST.** It must remain callable with `curl` and from a webhook, without a generated
  client.
- **Inbound subscription is gRPC bidirectional streaming.** Bidirectional specifically so consumers
  acknowledge messages; fire-and-forget streaming would reproduce the upstream's silent-drop defect
  one layer up.
- Do not duplicate the send surface across both transports.

## Receive Ownership

The upstream broadcast is lossy by construction — an unbuffered channel with a non-blocking send, so
a consumer that is not parked in a receive misses the message silently.

- Exactly one process owns the receive stream.
- It must drain into a buffered queue and do **no** inline work. Group resolution, an outbound call
  or dispatch inside the read loop will drop messages exactly as a naive consumer does.

## Channel Resolution

- Channels are declared by **name** and resolved to ids at startup; ids are never committed.
- Group names are not unique. Fail loudly on ambiguity at startup rather than picking the first
  match.
- Re-resolve on a not-found failure instead of crash-looping.

## Privacy

- Phone numbers are personal data. They belong in a Secret or a gitignored local file, never in a
  committed values file, and never in a log field, metric label or trace attribute — the number is
  the sender on every outbound call, so naive request logging will capture it.
- Resolved group ids are account-linked identifiers; treat them as sensitive.
- Tests, fixtures, documentation and the Compose environment example use placeholders only.

## Configuration

Loading must be source-agnostic from the first commit: the same shape whether it arrives as a mounted
file, a projected ConfigMap key or environment variables. A Kubernetes-aware code path would make the
Compose target a fork that rots.

## Build and Deploy Scripts

`build.ps1`, `build.sh` and `deploy.ps1` are thin entry points for the public account-level `.github`
repository's `container-workflows` skill. The central PowerShell scripts own build and deployment
behavior and the single Pester suite; Bash entry points invoke PowerShell instead of duplicating the
implementation. Keep repository-specific values derived from the caller or supplied through the
gitignored `deploy.local.psd1`. Change shared behavior and tests centrally, never in a root shim.
