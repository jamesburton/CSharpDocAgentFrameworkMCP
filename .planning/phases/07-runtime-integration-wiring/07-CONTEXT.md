# Phase 7: Runtime Integration Wiring - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire up the MCP server so it runs end-to-end at runtime. DI container resolves all services, artifact paths are configurable, and all five MCP tools return real results from the full pipeline (discover → parse → normalize → index → serve). This is integration wiring — no new domain logic, just connecting existing components.

</domain>

<decisions>
## Implementation Decisions

### DI Registration Strategy
- Single `AddDocAgent()` extension method on `IServiceCollection` in `DocAgent.McpServer` project
- `SnapshotStore` and `BM25SearchIndex` registered as **singletons** (expensive to build, immutable)
- `IKnowledgeQueryService` registered as **scoped** (per-request)
- Support both `IOptions<DocAgentServerOptions>` pattern AND `Action<DocAgentServerOptions>` delegate for configuration

### ArtifactsDir Configuration
- Three config sources with priority: CLI argument > environment variable (`DOCAGENT_ARTIFACTS_DIR`) > `appsettings.json` (`DocAgent:ArtifactsDir`)
- Default value: `./artifacts` (relative to working directory) if nothing configured
- **Auto-create** directory if it doesn't exist — fail only on permission errors
- **Eager startup validation** — validate and create directory during DI registration, fail fast if problems

### GetReferencesAsync Behavior
- Return **all edge types** (inherits, implements, calls, references, contains)
- Return **bidirectional** edges — both incoming (who references me) and outgoing (what I reference)
- Accept **optional edge type filter** parameter: `GetReferencesAsync(symbolId, edgeTypes?)` — defaults to all
- **Throw `SymbolNotFoundException`** when the symbol ID doesn't exist in the graph (distinguish from "exists but no references")

### E2E Integration Test Design
- Use a **synthetic minimal .csproj** with known types as test input — fast, deterministic, no external dependencies
- **Both** in-process DI test (build real container, resolve services, call pipeline) AND subprocess stdio smoke test
- **All 5 MCP tools** must return non-error responses: `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`
- Use **temp directory** for artifacts, cleaned up after each test run — no stale state, parallel-safe

### Claude's Discretion
- Exact structure of the synthetic test project (how many types, what relationships)
- `AddDocAgent()` internal implementation details (registration order, factory patterns)
- Subprocess smoke test transport details (how to start/stop the server in test)
- Error message formatting for startup validation failures

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Follow existing .NET conventions (Options pattern, xUnit test patterns, IServiceCollection extensions).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-runtime-integration-wiring*
*Context gathered: 2026-02-27*
