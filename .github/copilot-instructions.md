# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the
[account-level .github repository](https://github.com/f2calv/.github). They are deliberately not
copied here. Clone that repository and add it to the VS Code workspace, or link its instruction
folders into `~/.copilot/`. If the shared files are unavailable, stop rather than guessing the
conventions.

Everything below is specific to this repository.

## Current Baseline

The gateway sends and runs channel interactions — reactions, typing indicators and polls — over
REST, owns the inbound receive stream, persists it and fans it out over a bidirectional gRPC
subscription with a durable cursor per subscriber. The container image, Helm chart and
`CasCap.Signalizr.Client` package all build and are exercised in CI.

Both directions are proven against one registered account, deployed in a cluster. Scale, long-run
stability, multiple production subscribers and recovery from a wrapper outage are not; the planned
24-hour soak was skipped. Do not describe the unbuilt parts — the MCP role, durable poll tallies and
the shared command dispatcher — as though they exist.

The gateway may own a dedicated number or link to an existing personal account. On a linked account
the owner's own messages arrive as `syncMessage.sentMessage` rather than `dataMessage`. Anything
that filters inbound envelopes must handle both, or the gateway goes blind to its owner. `FromSelf`
marks both forms, so a consumer serving the owner must not discard messages on that flag alone.

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

## Helm Charts

`charts/signalizr` is packaged and validated by `ci.yml` under the application version, but not
pushed; publication is separate while the package keeps its own GHCR ownership. Its `Chart.yaml`
version is a placeholder. `charts/signalizr-dashboards` is versioned by its own `Chart.yaml`: a
default-branch change under that directory runs `deploy-dashboards.yml`, which publishes that
version and bumps the dashboard Application in the private GitOps repository named by the
`GITOPS_REPOSITORY`, `SIGNALIZR_DASHBOARD_MANIFEST_PATH`, `SIGNALIZR_DASHBOARD_NAMESPACE` and
`SIGNALIZR_DASHBOARD_ENVIRONMENT` repository variables. Bump the dashboard chart version with every
packaged change, including README-only edits. For local iteration, `deploy.ps1 -OnlyCharts`
publishes a disposable development version without rolling application pods.

Every chart keeps chart-testing fixtures under `ci/`, and the `helm` job in `ci.yml` lints the chart
against each of them. Dashboard JSON is never passed through Helm `tpl`, because Grafana legend
tokens use the same double-brace syntax; datasource UIDs are substituted with exact `replace` calls.
