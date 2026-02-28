# Phase 5: MCP Server + Security - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Expose the symbol graph through all five MCP tools (`search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`) over stdio transport, with path allowlisting, audit logging, input validation, and prompt injection defense. Agents can query documentation securely with enforced boundaries.

</domain>

<decisions>
## Implementation Decisions

### Tool Response Shape
- All five tools support `format=json|markdown|tron` parameter (default: JSON)
- TRON = Token Reduced Object Notation — a JSON superset that defines schemas to reduce property name repetition, ideal for LLM token efficiency
- **search_symbols:** Default compact (symbol kind, FQN, summary). `fullDocs=true` includes full parsed doc comment
- **get_symbol:** Default compact. `includeSourceSpans=true` adds file path + line range
- **get_references:** Default compact (referencing symbol ID + location). `includeContext=true` adds surrounding code snippets
- **diff_snapshots:** Default change list (added/removed/modified symbols). `includeDiffs=true` adds inline before/after doc content
- **explain_project:** Richest tool — supports `mode=json|markdown|tron`, depth controls for references and external dependencies, section include/exclude options, and a `chainedEntityDepth` parameter to load child entities in one call (reducing agent round-trips). Default provides most useful details with named handles for loading child entities on demand

### Error & Denial Behavior
- Path denial: Opaque "access denied" by default. Debug/verbose config flag reveals path details and allowlist rule that blocked — for development setups only
- Error codes: Standard JSON-RPC codes for protocol errors, custom app-specific codes in a structured `data` field (e.g., path_denied, snapshot_not_found, invalid_symbol_id)
- Invalid input: Configurable — strict mode (default, fail fast with structured error) and lenient mode (best effort with partial results + warnings array)
- Prompt injection: Sanitize known injection patterns from doc comment content AND add a warning flag when suspicious patterns are detected (defense in depth)

### Audit Logging
- Claude's discretion on log entry format (recommend structured JSONL)
- Tiered verbosity: Default logs metadata (tool name, input params summary, response size, duration, status). Verbose config flag includes full request/response bodies
- Dual output: Stderr for real-time monitoring + configurable file path for persistent audit trail. Both independently configurable
- Configurable redaction: Regex-based redaction patterns in config. Default: no redaction. Opt-in for sensitive environments

### Allowlist Configuration
- Layered config: JSON config file as base, environment variable (`DOCAGENT_ALLOWED_PATHS`) overrides/extends
- Glob pattern support for path matching (full glob, not just prefixes)
- Default when unconfigured: Allow server's current working directory only (reasonable dev default)
- Allow + deny rules: Both allow and deny patterns supported. Deny takes precedence over allow

### Claude's Discretion
- Audit log entry format (JSONL recommended)
- Exact TRON schema definitions for each tool
- Prompt injection pattern detection heuristics
- Specific custom error code numbers and naming
- Config file format and location conventions

</decisions>

<specifics>
## Specific Ideas

- TRON format (https://tron-format.github.io/) — Token Reduced Object Notation, a JSON superset designed for LLM token efficiency. Include as a first-class format option across all tools
- `explain_project` should be the "one call to understand everything" tool — chained entity depth prevents repeated round-trips
- Tiered detail pattern is consistent across all tools: compact default, opt-in flags for richer responses
- Configurable behavior pattern is consistent: strict defaults, debug/verbose/lenient modes for development

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-mcp-server-security*
*Context gathered: 2026-02-27*
