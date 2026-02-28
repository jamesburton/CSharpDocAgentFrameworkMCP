# Feature Research

**Domain:** Agent-native code documentation / code intelligence framework (.NET/C#, MCP server)
**Researched:** 2026-02-26
**Confidence:** HIGH (project plans) / MEDIUM (competitive landscape via WebSearch)

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that agents and integrators assume exist. Missing these means the tool is not usable as a code intelligence substrate.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Symbol search (keyword / BM25) | Agents need to locate types/members by name. No search = no navigation. | MEDIUM | BM25 over symbol names + doc text. `search_symbols(query)` MCP tool. Already in plan. |
| Symbol detail retrieval by ID | Agents must dereference a found symbol to get its full definition, signature, and docs. | LOW | `get_symbol(symbolId)`. Requires stable `SymbolId` as pre-requisite. |
| XML doc ingestion and binding | C# developers generate `GenerateDocumentationFile` output. Agents expect docs to be readable, not raw XML. | MEDIUM | `XmlDocParser` with proper symbol binding. Summary, param, returns, remarks, exceptions. |
| Roslyn symbol graph (types, members, namespaces) | Compiler-accurate type information. Without this, tool answers are no better than file-read heuristics. | HIGH | `RoslynSymbolCollector`. File spans, parent/child relationships, accessibility, kind. |
| Stable symbol identity across builds | If IDs change between runs, agent caches break and diff tools produce noise. | HIGH | `SymbolId` spec — assembly-qualified + kind + member context. Foundation for everything else. |
| Deterministic snapshot serialization | Agents cache and compare snapshots. Non-determinism breaks diffs, trust, and reproducibility. | MEDIUM | Same input → identical `SymbolGraphSnapshot` bytes. Hash/fingerprint. |
| MCP tool surface (stdio) | Agents expect MCP protocol compliance. Anything non-MCP requires custom client plumbing. | MEDIUM | `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`. Stdio first. |
| References query | Agents need to trace callers and usages, not just declarations. | MEDIUM | `get_references(symbolId)`. Depends on Roslyn symbol graph. |
| Basic security (path allowlist + audit log) | Multi-tenant or CI use exposes the tool to untrusted input. No allowlist = privilege escalation risk. | LOW | Default-deny paths. Log every tool call with input/output. |
| Snapshot versioning and schema migration | Symbol graph schema will evolve. Agents loading a stale snapshot must know it's stale. | LOW | Schema version field + explicit migration path. |

### Differentiators (Competitive Advantage)

Features that set DocAgentFramework apart from raw file reading, a generic MCP wrapper, or Sourcegraph as a hosted service.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Compiler-grade symbol semantics (not text heuristics) | Roslyn semantic model gives exact types, nullability, constraints, generic arity — things no text parser or embedding can produce reliably. Agents get answers that are provably correct at compile time. | HIGH | This is the core thesis. Everything flows from Roslyn truth. Dependency: Roslyn symbol graph. |
| Symbol-level semantic diff engine | Text diffs are noise. Semantic diffs expose API surface changes, nullability flips, constraint additions — risk-scored and structured. An agent can decide "is this change safe to ship" without reading source. | HIGH | `diff_snapshots(a, b)`. Covers signature, accessibility, generic constraints, nullability, inheritance. Dependency: stable SymbolId + snapshot format. |
| Unusual Change Review skill | Automates the "suspicious edit" review loop: compare snapshots → flag divergence between API changes and doc/test changes → propose remediation. This is a workflow no competitor exposes as a composable MCP tool. | HIGH | `review_changes` MCP tool. Depends on semantic diff engine and Roslyn analyzers. |
| Roslyn analyzer integration (doc-change parity enforcement) | Catch public API changes not reflected in docs at CI time, not discovery time. Structured SARIF-compatible findings. | HIGH | `DocAgent.Analyzers`. Catches the "ship it and forget docs" failure mode that every team hits. Dependency: stable snapshot + V1 ingestion pipeline. |
| Doc coverage policy enforcement as a build gate | Teams with large public APIs need coverage metrics enforced, not suggested. A CI step that fails on coverage drop is a forcing function no IDE plugin offers. | MEDIUM | Configurable threshold per project/namespace. Emits structured violations. Dependency: analyzers. |
| Snapshot catalog and artifact storage | Build-identified snapshots let agents ask "what did this codebase look like at commit X?" This is historical reasoning that hosted tools monetize behind enterprise plans. | MEDIUM | `artifacts/snapshots/<id>.json`. Trivial with file-based storage in V1. Dependency: deterministic serialization. |
| Embeddings index behind a clean interface | Agents that want semantic search (not just keyword) get it without rewiring the tool surface. The abstraction means the embedding provider is swappable. | HIGH | `IVectorIndex`. Only interface in V1; defer implementation. Value is the interface contract, not the impl. |
| Package reference graph (`PackageRefGraph`) | Agents can ask "what packages does this project depend on, at what version, and what changed?" NuGet graph reasoning is unavailable in LSP or Sourcegraph without custom indexing. | MEDIUM | Parses `*.csproj`, `Directory.Packages.props`, `packages.lock.json`. Dependency: V1 ingestion. |
| Self-hosted, offline-capable, stdio-only | Many enterprise codebases cannot use hosted services (data residency, IP concerns). Stdio MCP means zero network surface, works in air-gapped CI. | LOW | Already the V1 design. Value is in deliberate scope constraint. |
| Aspire hosting and telemetry | Teams that operate the server as a service (not just a local tool) get observability wiring out of the box. | MEDIUM | `DocAgent.AppHost`. Not unique but removes integration work. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem valuable but should be deliberately deferred or avoided at this stage.

| Anti-Feature | Why Requested | Why Problematic | Alternative |
|--------------|---------------|-----------------|-------------|
| Embeddings / vector search in V1 | Semantic search over docs and symbols sounds powerful. | Embeddings require provider choice (OpenAI, local, Azure), add non-determinism, complicate testing, and increase cost. The BM25 index solves 80% of agent navigation needs cheaply and deterministically. Embeddings before the symbol graph is solid is premature. | `IVectorIndex` interface only in V1. Implement when BM25 proves insufficient for real workloads. |
| HTTP/SSE MCP transport in V1 | Multi-client server use. | HTTP transport adds auth complexity (OAuth, API keys, rate limiting), network surface, and CORS/TLS concerns. These are solvable but distracting for V1 correctness. Stdio is simpler, more secure by default, and sufficient for local agent and CI use. | Stdio only for V1+V2. HTTP transport in V2/V3 with explicit auth model. |
| Real-time file-watch / incremental re-indexing | "Always up to date" sounds essential. | Incremental indexing is a significant engineering problem (change detection, partial graph invalidation, consistency windows). For most agent workflows, a rebuild-on-demand cycle is fast enough and far simpler to test. | Explicit re-index command. Snapshot catalog provides "latest indexed" timestamp agents can check. |
| Full query DSL (CodeQL-like) | Powerful ad-hoc queries over the symbol graph. | A query language is a product in itself: parser, optimizer, security sandbox, error messages. V1 teams will not have query workloads defined yet. Premature DSL design locks in schema before it's stable. | Narrow MCP tools cover 95% of agent navigation. Add DSL in Tier 5 when concrete query patterns are known. |
| Structural code rewrite / auto-fix | "Fix the docs for me" is a natural agent request. | Auto-rewrite with commit = trust problem. An agent that writes and commits is a much higher-stakes capability requiring verification gates, branch policies, and human approval flows. Conflating the read-path with the write-path multiplies attack surface. | Produce patch files + diff. Require explicit human approval before applying. Never auto-commit. |
| IDE plugin distribution in V1 | Discoverability / adoption. | Plugin distribution (VS, VS Code, Rider) requires marketplace approval, separate release pipelines, version compatibility matrices, and UX polish. None of this validates the core indexing and tool value. | Prove value via agent/CI use first. Plugin distribution is a Tier 7 concern. |
| Polyglot support (Tree-sitter, Python, TypeScript) in V1 | Broader audience. | Polyglot dilutes the compiler-accuracy thesis. A C# framework that "kind of works" for Python is worse than one that works perfectly for C#. Polyglot adapters also require a generic `ISymbolGraphBuilder` contract that is not yet stable. | Lock V1+V2 to C#/Roslyn. Abstract the builder interface with generic param when adding the second language (Tier 3). |
| Multi-tenant authz and tenant isolation | Enterprise SaaS readiness. | Multi-tenancy requires a data model, isolation guarantees, and a non-stdio transport. V1 is a single-tenant local tool. Adding tenancy before the data model is stable causes expensive rewrites. | Allowlist + audit log satisfy V1 security. Multi-tenant authz is a Tier 6 enterprise feature. |

---

## Feature Dependencies

```
[Stable SymbolId]
    └──requires──> [Roslyn symbol graph walker]

[Symbol graph walker]
    └──requires──> [Roslyn semantic model ingestion]

[SymbolGraphSnapshot]
    └──requires──> [Symbol graph walker]
    └──requires──> [XML doc parser + symbol binding]
    └──requires──> [Deterministic serialization]

[BM25 search index]
    └──requires──> [SymbolGraphSnapshot]

[MCP tools: search_symbols, get_symbol, get_references]
    └──requires──> [BM25 search index]
    └──requires──> [SymbolGraphSnapshot]

[MCP tool: diff_snapshots]
    └──requires──> [SymbolGraphSnapshot]
    └──requires──> [Stable SymbolId]

[Semantic diff engine]
    └──requires──> [diff_snapshots (basic)]
    └──requires──> [SymbolGraphSnapshot]

[Unusual Change Review skill / review_changes tool]
    └──requires──> [Semantic diff engine]
    └──requires──> [Roslyn analyzers]

[Roslyn analyzers (doc parity, coverage policy)]
    └──requires──> [SymbolGraphSnapshot]
    └──requires──> [XML doc parser]

[PackageRefGraph]
    └──requires──> [Project source ingestion (LocalFileSystemSource / GitRepoSource)]

[IVectorIndex (interface only)]
    └──requires──> [SymbolGraphSnapshot] (as input contract)
    └──independent of BM25 implementation

[Aspire app host]
    └──requires──> [MCP server (stub or real)]
    └──enhances──> [Telemetry + configuration]

[Snapshot catalog]
    └──requires──> [Deterministic serialization]
    └──enhances──> [diff_snapshots]
    └──enhances──> [Unusual Change Review]

[HTTP/SSE MCP transport] ──deferred──> depends on [Auth model (V2/V3)]
[Embeddings / IVectorIndex impl] ──deferred──> depends on [BM25 proving insufficient]
[Polyglot (Tree-sitter)] ──deferred──> depends on [Generic ISymbolGraphBuilder contract (V3)]
[Query DSL] ──deferred──> depends on [Stable snapshot schema + known query patterns (Tier 5)]
```

### Dependency Notes

- **Stable SymbolId requires Roslyn symbol graph walker:** The ID scheme must be derived from compiler-authoritative symbols, not file paths or text parsing. This is the highest-leverage decision in the entire system — getting it wrong causes a forced rebuild of diffs, indexes, and caches downstream.
- **Semantic diff engine requires diff_snapshots (basic) and SymbolGraphSnapshot:** The basic diff tool is the scaffold; semantic scoring (risk, grouping, nullability deltas) is layered on top. Do not attempt to build the full semantic diff without first validating the basic structural diff.
- **Unusual Change Review requires both semantic diff AND Roslyn analyzers:** The review skill synthesizes static analysis findings (analyzers) with change signals (diff). Neither alone is sufficient. This is a V2 capability — attempting it in V1 would require both subsystems to be stable simultaneously.
- **PackageRefGraph is independent of Roslyn symbol graph:** The two pipelines can be built in parallel worktrees. They share the `IProjectSource` abstraction but do not depend on each other's outputs.
- **IVectorIndex (interface only) is safe to define in V1 without an implementation:** The interface contract prevents future callers from coupling to BM25 internals. Implementation is deferred without blocking any V1 tool surface.

---

## MVP Definition

### Launch With (v1)

Minimum viable product — what's needed for an agent to get real value from a .NET codebase.

- [ ] **Stable SymbolId + SymbolGraphSnapshot schema** — Without this nothing else is trustworthy. All downstream features depend on it.
- [ ] **Roslyn symbol walker** — Namespaces, types, members, file spans. This is the compiler truth engine.
- [ ] **XML doc parser + symbol binding** — Binds prose documentation to specific symbols. Without binding, docs are just strings with no addressable anchor.
- [ ] **Deterministic serialization** — Same input → identical output. Required for diffs and caching to be meaningful.
- [ ] **BM25 search index** — Cheap, fast, deterministic. Solves 80% of agent navigation ("find me the class that does X").
- [ ] **MCP tools: search_symbols, get_symbol, get_references, diff_snapshots, explain_project** — The tool surface agents call. Without these, no agent can consume the framework.
- [ ] **Path allowlist + audit logging** — Minimum security posture for CI use. No allowlist = unsafe by default.
- [ ] **Snapshot artifact storage** — Write `artifacts/snapshots/<id>.json`. Enables history and diffing across runs.

### Add After Validation (v1.x)

- [ ] **PackageRefGraph** — Add once core symbol navigation is validated. Unlocks dependency-aware questions ("what packages changed with this PR?"). Trigger: agents asking about dependencies.
- [ ] **Semantic diff engine (risk-scored findings)** — Add once basic `diff_snapshots` is exercised by real agent workflows. Trigger: agents needing structured "what changed and should I care?" answers.
- [ ] **Roslyn analyzers (doc parity + coverage policy)** — Add once snapshot format is stable. Trigger: teams wanting CI gates on doc quality.

### Future Consideration (v2+)

- [ ] **Unusual Change Review skill / review_changes MCP tool** — Depends on semantic diff + analyzers both being stable. High value but complex synthesis. (V2)
- [ ] **IVectorIndex implementation** — Defer until BM25 proves insufficient for real agent workloads. (V2 or V3)
- [ ] **HTTP/SSE MCP transport** — Defer until stdio is proven and an auth model is designed. (V2/V3)
- [ ] **Polyglot via Tree-sitter** — Defer until generic `ISymbolGraphBuilder` contract is driven by a real second language need. (Tier 3)
- [ ] **Query DSL** — Defer until concrete query patterns are identified from actual agent usage. (Tier 5)

---

## Feature Prioritization Matrix

| Feature | Agent Value | Implementation Cost | Priority |
|---------|-------------|---------------------|----------|
| Stable SymbolId | HIGH | MEDIUM | P1 |
| Roslyn symbol walker | HIGH | HIGH | P1 |
| XML doc parser + symbol binding | HIGH | MEDIUM | P1 |
| Deterministic serialization | HIGH | LOW | P1 |
| BM25 search index | HIGH | MEDIUM | P1 |
| MCP tool surface (5 tools) | HIGH | MEDIUM | P1 |
| Path allowlist + audit log | HIGH | LOW | P1 |
| Snapshot artifact storage | MEDIUM | LOW | P1 |
| PackageRefGraph | MEDIUM | MEDIUM | P2 |
| Semantic diff engine | HIGH | HIGH | P2 |
| Roslyn analyzers (doc parity) | MEDIUM | HIGH | P2 |
| Doc coverage policy gate | MEDIUM | MEDIUM | P2 |
| review_changes MCP tool | HIGH | HIGH | P2 |
| IVectorIndex (interface) | LOW | LOW | P2 |
| Snapshot catalog | MEDIUM | LOW | P2 |
| Aspire app host | MEDIUM | MEDIUM | P2 |
| IVectorIndex (implementation) | MEDIUM | HIGH | P3 |
| HTTP/SSE MCP transport | MEDIUM | HIGH | P3 |
| Polyglot (Tree-sitter) | MEDIUM | HIGH | P3 |
| Query DSL | HIGH | VERY HIGH | P3 |
| Structural rewrite engine | HIGH | VERY HIGH | P3 |
| IDE plugin distribution | LOW | HIGH | P3 |

**Priority key:**
- P1: Must have for launch (V1)
- P2: Should have, add when core is validated (V1.x / V2)
- P3: Future consideration, defer until product-market fit (V3+)

---

## Competitor Feature Analysis

| Feature | Sourcegraph / Amp | CodeQL | LSP (raw) | DocAgentFramework |
|---------|-------------------|--------|-----------|-------------------|
| Symbol search | Yes (code search, semantic index) | Yes (via QL queries) | Yes (workspace/symbol) | Yes — BM25, MCP-exposed |
| References query | Yes | Yes | Yes | Yes — `get_references` |
| Cross-repo / monorepo | Yes (hosted) | Yes | Partial | Future (V3 workspace merge) |
| Persistent/versioned memory | Partial (indexes but opaque) | No | No | Yes — snapshot catalog, deterministic artifacts |
| Symbol-level semantic diff | No | Partial (QL scripts) | No | Yes — differentiator |
| Doc parity enforcement | No | No | No | Yes (Roslyn analyzers) — differentiator |
| Offline / self-hosted / stdio | Enterprise SKU required | Yes (CI runner) | Yes (in-process) | Yes by default — V1 design |
| Agent-consumable MCP surface | Yes (Sourcegraph MCP server) | Via CodeQL LSP MCP wrapper | Via LSP-MCP adapter | Yes — native MCP, narrow surface |
| Compiler-accurate semantics (.NET) | No (text + heuristics) | No (.NET is weak) | Partial (OmniSharp/roslyn-ls) | Yes — Roslyn semantic model |
| Package dependency graph | Partial (repo metadata) | No | No | Yes — `PackageRefGraph` |
| Unusual change review workflow | No | No | No | Yes (V2) — differentiator |

**Key competitive insight:** Sourcegraph provides broad code search and a hosted MCP server, but its symbol memory is opaque, not diffable, and requires a hosted service. CodeQL is a query engine, not a memory substrate. LSP is live-only — no persistence, no historical diff. DocAgentFramework occupies the gap: compiler-accurate, versioned, diffable, offline, agent-native memory for .NET. (MEDIUM confidence — based on public docs and WebSearch; Sourcegraph's internal index schema is not public.)

---

## Sources

- Project plans: `C:/Development/CSharpDocAgentFrameworkMCP/docs/Plan.md`, `EXTENDED_PLANS/EXTENDED_PLANS.md`, `EXTENDED_PLANS/LIVE_INTERROGATION_INTERFACE.md`, `EXTENDED_PLANS/TOOLING_MATRIX.md`, `.planning/PROJECT.md`
- [Sourcegraph Amp Agent — code intelligence for LLMs](https://www.amplifilabs.com/post/sourcegraph-amp-agent-accelerating-code-intelligence-for-ai-driven-development) (MEDIUM confidence — WebSearch)
- [Teaching AI to Navigate Your Codebase: Agent Skills + Sourcegraph MCP (Jan 2026)](https://medium.com/@ajaynz/teaching-ai-to-navigate-your-codebase-agent-skills-sourcegraph-mcp-710b75ab2943) (MEDIUM confidence — WebSearch)
- [Evaluating the impact of LSP-based code intelligence on coding agents](https://www.nuanced.dev/blog/evaluating-lsp) (MEDIUM confidence — WebSearch; contains LSP vs text-search latency data: ~50ms vs 45s for find-references)
- [CodeQL LSP MCP Server](https://lobehub.com/mcp/neuralprogram-codeql-lsp-mcp) (LOW confidence — third-party listing)
- [MCP servers for documentation sites (Dec 2025)](https://buildwithfern.com/post/mcp-servers-documentation-sites) (MEDIUM confidence — WebSearch)
- [MCP Enterprise Readiness — 2025-11-25 spec](https://subramanya.ai/2025/12/01/mcp-enterprise-readiness-how-the-2025-11-25-spec-closes-the-production-gap/) (MEDIUM confidence — WebSearch)
- [Build a Model Context Protocol server in C#](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/) (HIGH confidence — official Microsoft DevBlog)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (HIGH confidence — official SDK repo)
- [LSP specification 3.17](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/) (HIGH confidence — official spec)
- [CodeQL overview](https://codeql.github.com/) (HIGH confidence — official site)

---

*Feature research for: agent-native .NET code documentation / code memory framework (MCP server)*
*Researched: 2026-02-26*
