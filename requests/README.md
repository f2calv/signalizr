# Signalizr requests

Use the VS Code REST Client extension to inspect the optional MCP surface without a model.

## Setup

Copy [.env.example](.env.example) to `.env` in this directory and set `SIGNALIZR_BASE_URL` to the
HTTP origin reachable through your [localhost port-forward](../docs/mcp.md#connect-vs-code).
The file is gitignored. Do not add `/mcp` to the base URL.

Open [signalizr-mcp.http](signalizr-mcp.http) and send the initialize request, then the initialized
notification, tool discovery and desired query. The examples use the supported `2025-11-25`
handshake protocol; modern clients may negotiate without an initialize request.

All POSTs here are read-only protocol operations. No session ID is required because the server is
stateless. Responses can be JSON or SSE (`data:` lines containing JSON-RPC); both are accepted.
An initialized notification normally returns 202.

The history example requires its separate opt-in and uses a synthetic `My Test Group Name` group. Replace
the group with one discovered by the group tool. Its response can expose private conversation
text, so do not copy it into issues, logs or commits.
