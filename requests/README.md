# Signalizr requests

Use the VS Code REST Client extension to inspect signalizr's optional MCP endpoint without a model,
or deliberately send text when that separate capability is enabled.

## Setup

Copy [.env.example](.env.example) to `.env` in this directory and set `SIGNALIZR_BASE_URL` to the
HTTP origin reachable through your [localhost port-forward](../docs/mcp.md#connect-vs-code).
Set `SIGNALIZR_MCP_USERNAME` and `SIGNALIZR_MCP_PASSWORD` to the gateway's `CasCap:ApiAuthConfig`
credentials; every request sends them as HTTP Basic authentication.
The file is gitignored. Do not add `/mcp` to the base URL.
The gateway must already be running with `Gateway,Receiver,Mcp` and configured Basic credentials;
this collection does not start or configure it.

## Usage

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
