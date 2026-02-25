# EXTENDED_PLANS.md
## CSharpDocAgentFrameworkMCP — Extended plan (ordered by decreasing RoI)

### Positioning
Build a **compiler-grade, agent-native knowledge substrate** for codebases:
- Inputs: build outputs + source + dependency metadata
- Normalization: versioned, diffable symbol graph
- Serving: narrow MCP tools surface (secure-by-default)
- Orchestration: Microsoft Agent Framework workflows/skills

Primary objective: agents should reason over **compiler truth**, not approximations.

---

## Tier 0 — Foundations (Highest RoI)
### 0.1 Stable Symbol Identity (multi-build, multi-language)
**Deliver**
- A stable `SymbolId` spec that survives refactors and supports language adapters.
- An explicit schema version (`v1`, `v2`…) with migrations.
- Deterministic serialization and hashing (“source fingerprint”).

**Why RoI is highest**
Everything else (diffs, indexing, trust, polyglot) depends on stable identity.

**Notes**
- For C#, start with Roslyn `ISymbol` identity + assembly-qualified context.
- Store `SourceSpan` anchors and commit/build refs for traceability.

---

### 0.2 Snapshot pipeline (ingest → normalize → index → catalog)
**Deliver**
- Snapshot catalog (build id / commit / timestamp → snapshot files)
- Deterministic artifacts (`artifacts/snapshots/<id>.json`, `artifacts/index/<id>/…`)
- Rebuild-only-what-changed capability (incremental later)

---

### 0.3 Narrow MCP tool surface (read-only by default)
**Deliver tools**
- `search_symbols(query, filters)`
- `get_symbol(symbolId)`
- `get_references(symbolId)`
- `diff_snapshots(a, b)`
- `explain_project(projectId)`

**Security**
- Stdio transport first; explicit allowlists and audit logs

**Primary references**
- “Build a Model Context Protocol (MCP) server in C#”
  https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/
- MCP C# SDK
  https://github.com/modelcontextprotocol/csharp-sdk
- MCP in Microsoft Agent Framework
  https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools

---

## Tier 1 — Trust + Diff Intelligence (Very high RoI)
### 1.1 Semantic diff engine (symbol-level, not text-level)
**Diff dimensions**
- API surface (public/protected) changes
- Signature, generic constraints, nullability
- Inheritance/implementation changes
- Dependency changes (packages, versions)
- Doc/test divergence signals

**Output**
- Risk-scored findings
- Grouped “what changed” and “why it matters”

---

### 1.2 Unusual Change Review skill (branch/worktree flow)
**Deliver**
- A skill that:
  - compares snapshots
  - flags suspicious edits
  - proposes safe remediations
- Git worktree conventions for parallel review/fixes

---

## Tier 2 — Programmable Live Interrogation (High RoI)
### 2.1 “Ask the compiler” interface (Roslyn-backed)
Expose structured questions that route to analyzers / semantic model:

Examples:
- “Why is this null possible?”
- “What allocations happen on this path?”
- “Which callers reach this branch?”
- “Show me the minimal repro for this diagnostic.”

Implementation options
- Roslyn analyzer endpoints (in-proc)
- Roslyn scripting session (sandboxed)
- Dedicated “analysis service” process

---

### 2.2 LSP bridge (read-only workspace intelligence)
Use Language Server Protocol to query:
- hover docs
- go-to-definition
- references
- diagnostics
- code actions metadata (read-only)

References
- LSP overview: https://microsoft.github.io/language-server-protocol/
- LSP spec 3.17: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/

---

## Tier 3 — Polyglot expansion (Medium RoI, high leverage)
### 3.1 Tree-sitter AST ingestion (syntax-level, fast, uniform)
Use Tree-sitter to produce:
- syntax symbol stubs (functions/types/modules)
- docstring/comment capture
- local references (best-effort)
- file spans

References
- Tree-sitter repo: https://github.com/tree-sitter/tree-sitter
- Tree-sitter docs: https://tree-sitter.github.io/

**Why this is the right polyglot “on-ramp”**
- wide language coverage
- incremental parsing
- avoids re-implementing compilers

---

### 3.2 Per-language “semantic” adapters (later)
Once you have RoI, add deeper semantics for select languages:
- TypeScript: TypeScript compiler API
- Python: pylance/pyright + AST + import graph
- Go: `go/packages` + `gopls`
- Rust: rust-analyzer

Principle: syntax-first, semantics-where-worth-it.

---

## Tier 4 — Documentation & Knowledge Products (Medium RoI)
### 4.1 Documentation tool integration (ingest, don’t render)
Ingest from ecosystem docs tools where useful:
- .NET: DocFX (consumes XML docs; can output models/markdown)
  https://dotnet.github.io/docfx/
- TypeScript: TypeDoc (HTML or JSON model)
  https://typedoc.org/
- Python: Sphinx (reST/Markdown → HTML/PDF)
  https://www.sphinx-doc.org/
- Rust: rustdoc (built-in docs tool)
  https://doc.rust-lang.org/rustdoc/
- Go: godoc (docs from comments)
  https://go.dev/blog/godoc
- C++: Doxygen (multi-language)
  https://www.doxygen.nl/
- Swift: DocC (documentation compiler)
  https://www.swift.org/documentation/docc/

Use them as *inputs* to memory, not the memory itself.

---

## Tier 5 — Agent-visible “Code Native” features (Speculative, long-term)
### 5.1 Query language over the symbol graph (CodeQL-like feel)
Provide a constrained query DSL to enable:
- “find all callers of X that pass null”
- “list all public APIs without docs”
- “find patterns matching a policy”

Inspiration references
- CodeQL: https://codeql.github.com/
- CodeQL scanning docs: https://docs.github.com/code-security/code-scanning/introduction-to-code-scanning/about-code-scanning-with-codeql

---

### 5.2 Structural code rewrite engine (rule-based + verified)
Optionally integrate AST-based refactors:
- Semgrep autofix inspiration
  https://semgrep.dev/blog/2022/autofixing-code-with-semgrep
- ast-grep comparison (migration-oriented)
  https://ast-grep.github.io/advanced/tool-comparison.html

Safety constraints:
- never auto-commit
- always produce patch + tests
- run analysis gates and diff again

---

## Tier 6 — CI policy + enterprise hardening (Lower RoI early)
- SARIF output (security tooling consumption)
- provenance and build attestation hooks
- content-addressed storage for artifacts
- multi-tenant authz for non-stdio MCP transports

---

## Tier 7 — UX & Distribution (Lower RoI early)
- Aspire-hosted deployment for teams (optional)
- plugin packs for IDEs/agents
- “portable reviewer” mode: file-based .NET 10 apps that generate artifacts

Reference for file-based C# apps (.NET 10)
- https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps
- https://devblogs.microsoft.com/dotnet/announcing-dotnet-run-app/

---

## Recommended “permutation bundles” (feature sets)
### Bundle A (Best default): C# deep + MCP + diffs
- Roslyn snapshot + index
- MCP tools
- semantic diff + unusual-change review

### Bundle B (Polyglot starter): Tree-sitter + MCP
- Tree-sitter AST snapshot for many languages
- limited but useful search + navigation

### Bundle C (Live intelligence): LSP bridge
- agents can interrogate “what the IDE knows” in real time

### Bundle D (Secure enterprise): audit + allowlists + policy gates
- required once you move beyond stdio

---

## Definition of done (all tiers)
- Passing tests for all new behavior
- Deterministic snapshot outputs
- Docs updated with local links + references
- Explicit blockers listed and worktree-friendly task slicing
