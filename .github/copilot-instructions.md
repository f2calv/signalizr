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
- Require Gateway and Receiver together for MCP. Preserve its read-only tools, separate
  off-by-default history disclosure switch and group-scoped queries.
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
- The application chart takes the application version at packaging. The dashboard chart has its
  own version: bump it for every packaged change, including README-only edits.
- Keep chart fixtures under each chart's `ci/` directory. Do not process dashboard JSON with Helm
  `tpl`; substitute datasource placeholders explicitly.
