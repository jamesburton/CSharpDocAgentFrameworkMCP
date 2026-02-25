# Security notes (MCP + agents)

MCP servers are privileged. Treat them like local automation with explicit guardrails.

## V1 stance
- Prefer **stdio** transport (local process boundary).
- No arbitrary filesystem access tools.
- Tool inputs validated, normalized, and logged.

## Defensive controls
- Repository root allowlist
- Path traversal prevention
- Output redaction hooks (secrets)
- Audit logging for tool calls
- Rate limiting / timeouts (where applicable)

## Prompt injection
Assume untrusted inputs can try to coerce the agent/tool.
Mitigations:
- minimal tool surface
- structured outputs
- explicit policies in agent guidance
