# Copilot Instructions

## Shared Instructions

Shared Copilot instructions, skills and prompts are maintained centrally in the
[account-level .github repository](https://github.com/f2calv/.github). They are deliberately not
copied here. Clone that repository and add it to the VS Code workspace, or link its instruction
folders into `~/.copilot/`. If the shared files are unavailable, stop rather than guessing the
conventions.

Read the [README](../README.md) for product behavior and configuration, and the
[MCP guide](../docs/mcp.md) before changing operator tools. Keep implementation status and usage
documentation there rather than in these repository-specific constraints.

## Architecture Boundaries

- Preserve the gateway contract; do not add arbitrary pass-through routes to the upstream wrapper.
- Keep sending and group interactions on REST, and inbound delivery on bidirectional gRPC with
  acknowledgements. Do not duplicate sending over gRPC.
- Keep exactly one receive owner. Its read loop only enqueues; group resolution, persistence and
  outbound calls belong outside that loop. See [Receiving](../README.md#receiving).
- Handle both `dataMessage` and `syncMessage.sentMessage`. Do not discard the owner's messages
  solely because `FromSelf` is true.
- Use exact Signal group names, including case and spaces, consistently in configuration, APIs,
  MCP, gRPC and persistence. Reject missing or ambiguous group matches rather than choosing one.

## Feature Wiring

- Declare roles through `FeatureNames` and register their `IBgFeature` implementations only when
  enabled. Gate feature controllers with `[FeatureController(...)]`; disabled routes return 404,
  not dependency-injection failures.
- Require Gateway and Receiver together for MCP. Keep query tools read-only; history disclosure
  and text sending each require their own off-by-default switch. Send through `IMessageGateway`,
  never replay an uncertain send automatically, and keep queries group-scoped.
- Keep the small `DemoClient` in the product image rather than adding a second image or sample
  project. Reassess that boundary if it gains independent dependencies or an inbound surface.

## Public Inputs and Privacy

- Keep Compose and Helm usable from a clean clone with public inputs and caller-supplied settings;
  do not introduce a Kubernetes-specific configuration path.
- Treat Signal account numbers and resolved group IDs as sensitive. Group names and message text
  can also contain private information; exclude them from logs and telemetry, including MCP
  payloads. Disclosure through APIs and opt-in operator notices follows the documented boundary.

## Maintenance

- Change shared build/deploy orchestration and its tests in the central `container-workflows`
  skill, not in this repository's root shims.
- This repository owns and publishes `CasCap.Signalizr.Client`, `CasCap.Signalizr.Client.Testing`,
  `CasCap.Comms`, and `CasCap.Comms.AI` under one repository version. Keep same-repository project
  references and package the complete set together; downstream Debug builds use adjacent project
  references and Release builds pin the exact published version.
- `CasCap.Comms` owns the shared Signalizr consumer pipeline and reply queue. `CasCap.Comms.AI`
  owns its optional in-process agent responder until that implementation migrates to the Agent
  Runtime client. Application event producers and application-specific enrichers remain with their
  consuming applications.
- Use the shared Helm guidance for chart authoring, fixtures, packaging, validation, and dashboard
  JSON handling.
- The application chart receives the application version during packaging. The independently
  versioned dashboard chart publishes from its committed chart version.
