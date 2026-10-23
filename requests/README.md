# Signalizr requests

Use the VS Code REST Client extension to inspect signalizr's MCP endpoint or gateway REST API.
These are manual examples, not automated tests; sending, reactions, typing and polls have real effects.

## Setup

Copy [.env.example](.env.example) to `.env` in this directory and set `SIGNALIZR_BASE_URL` to the
HTTP origin reachable through your [localhost port-forward](../docs/mcp.md#connect-vs-code).
Set `SIGNALIZR_MCP_USERNAME` and `SIGNALIZR_MCP_PASSWORD` to the gateway's `CasCap:ApiAuthConfig`
credentials; MCP requests send them as HTTP Basic authentication.
The file is gitignored. Do not add `/mcp` to the base URL.
The MCP collection requires `Gateway,Receiver,Mcp` and configured Basic credentials.
The REST collection requires `Gateway` for groups and `Receiver` for attachments.
Neither collection starts or configures the host.

## REST API

[signalizr-rest.http](signalizr-rest.http) covers group reads, sends, reactions, typing, polls,
binary attachments and one intentionally invalid request. Group operations return JSON for reads,
sends and poll creation, or empty 204 responses for other successful mutations. Validation errors
use `application/problem+json`; mapped group/delivery/upstream failures use Problem Details.
Disabled feature routes return 404. Attachment success is `application/octet-stream`, including
an empty retained attachment; a missing attachment is 404.

These REST controllers do **not** enforce MCP Basic authentication. Use only an approved loopback
port-forward or an independently protected network boundary; do not expose them publicly or assume
adding an Authorization header secures the service.

Set `SIGNALIZR_GROUP_NAME_URLENCODED` to the exact configured group name encoded as one query value
(spaces as `%20`, literal plus as `%2B`, ampersand as `%26`). The example name does not provision a
group. Supply retained delivery, attachment and poll IDs locally for their respective requests.
The synthetic `targetTimestamp` must be replaced with an approved gateway-authored message's actual
timestamp before timestamp-based reactions. No request consumes another request's response or
automatically cleans up its effects. Every operation requires separate execution approval.

## MCP

Open [signalizr-mcp.http](signalizr-mcp.http) and send the initialize request, then the initialized
notification, tool discovery and desired query. The examples use the supported `2025-11-25`
handshake protocol; modern clients may negotiate without an initialize request.

Discovery, query and prompt POSTs are read-only. The final text-send example is a real write
operation and requires `MessageSendingEnabled=true`; confirm its destination and text before
executing it. Do not retry automatically after an uncertain outcome.

No session ID is required because the server is stateless. Responses can be JSON or SSE (`data:`
lines containing JSON-RPC); both are accepted. An initialized notification normally returns 202.

The prompt examples list available prompts and retrieve `summarise_signalizr_status`. Retrieval
returns a reusable instruction message; it does not call tools or read live application data.

The history example requires its separate opt-in and uses a synthetic `My Test Group Name` group. Replace
the group with one discovered by the group tool. Its response can expose private conversation
text, so do not copy it into issues, logs or commits.

See the [MCP access boundary](../docs/mcp.md#access-boundary) before using any non-loopback endpoint.
REST sending and gRPC subscriptions are different contracts; this collection only exercises MCP.
