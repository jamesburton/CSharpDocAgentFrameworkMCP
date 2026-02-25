# Implementation plan (V1 → V3)

Date: 2026-02-25

## Guiding principles

- Everything is **testable** (unit + component + end-to-end).
- Treat doc ingestion as a **compiler-like pipeline**:
  1) discover → 2) parse → 3) normalize → 4) index → 5) serve
- Prefer **interfaces + generics + extensions** to keep sources/pluggable.
- Enable **parallel delivery** with clear blockers and worktree separation.

---

## V1 — “Doc memory server” (deliver value fast)

### V1 goals
- Ingest:
  - XML docs (`GenerateDocumentationFile` output)
  - Roslyn symbols (namespaces/types/members)
  - Repo metadata (solution/projects, packages, nuspec, lock files)
- Normalize into a stable **SymbolGraph**.
- Build a **searchable index**:
  - BM25 / keyword index (fast, cheap)
  - optional embeddings index (behind an interface)
- Expose everything via a **securable MCP server** (stdio first).
- Add “unusual change review” skill that:
  - compares two graph snapshots
  - flags suspicious diffs
  - offers branch/worktree-based remediation flows

### V1 deliverables
1. **Core domain contracts**
   - `SymbolId`, `SymbolKind`, `DocComment`, `SourceSpan`, `SymbolNode`, `SymbolEdge`
   - `SymbolGraphSnapshot` (versioned schema)
   - `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `IIndexer`, `IQueryService`

2. **Ingestion pipeline**
   - Local repo source (`LocalFileSystemSource`)
   - Git repo source (`GitRepoSource`):
     - clone/fetch to a cache dir (read-only)
     - support branch/tag/commit pinning
   - XML doc parser (`XmlDocParser`) with robust symbol binding
   - Roslyn walker (`RoslynSymbolCollector`) for symbol discovery + file spans

3. **Package mapping**
   - Parse `*.csproj`, `Directory.Packages.props`, `packages.lock.json`, `*.nuspec`
   - Produce `PackageRefGraph` (project → package → version)
   - Provide a “reference resolver” that yields canonical IDs and metadata

4. **Indexes**
   - `ISearchIndex` (BM25 or simple inverted index)
   - `IVectorIndex` (optional, behind interface)
   - `IndexWriter` writes deterministic artifacts to `artifacts/index/…`

5. **MCP server**
   - `DocAgent.McpServer` exposing tools:
     - `search_symbols(query)`
     - `get_symbol(symbolId)`
     - `get_references(symbolId)`
     - `diff_snapshots(a,b)`
     - `explain_project(projectId)`
   - AuthN/AuthZ stubs for non-stdio transports (V1 can ship stdio only)

6. **Aspire host**
   - `DocAgent.AppHost` to run MCP server + optional storage (SQLite)
   - Wired via environment variables and configuration

7. **Testing**
   - Golden-test fixture repo under `tests/Fixtures/RepoA`
   - Unit tests for parsing and normalization
   - Component tests for MCP tools (run server in-proc)
   - End-to-end test: ingest fixture → index → query tools

8. **Docs**
   - README, architecture, security, references, agent guidance

### V1 acceptance criteria (definition of done)
- `dotnet test` is green on clean machine
- Snapshot determinism: same input → identical `SymbolGraphSnapshot` output
- MCP tool contracts are versioned and documented
- Security: path allowlist, audit logging, and transport constraints documented

### V1 blockers
- Choosing the embeddings provider (can be deferred; keep interface)
- Finalizing MCP transport choice beyond stdio (defer to V2)

### Parallelization (recommended worktrees)
- ingestion-xml
- roslyn-symbolgraph
- package-mapping
- mcp-server
- aspire-host
- tests-infra
- docs

---

## V2 — “Deep understanding” (Roslyn analyzers + better diffs)

### V2 goals
- Add Roslyn analyzers:
  - detect public API changes not reflected in docs
  - detect suspicious edits (e.g., semantic changes without doc updates)
  - enforce doc coverage policy for public symbols
- Upgrade diffing:
  - symbol-level semantic diffs (signature, nullability, constraints, accessibility)
  - dependency graph diffs (package changes)

### V2 deliverables
- `DocAgent.Analyzers` package + tests
- CI pipeline step that fails on policy violations
- `review_changes` MCP tool that returns structured findings

### V2 blockers
- Requires stable symbol binding and snapshot format from V1

---

## V3 — “Assistive generation” (source generators + more sources)

### V3 goals
- Source generators to:
  - emit symbol graph hints
  - generate reference stubs for missing docs
  - generate MCP tool manifest from attributes
- Extend sources:
  - remote git monorepos, submodules
  - NuGet package source mapping (metadata + minimal docs ingestion)
- “Project-to-project” understanding:
  - merge multiple snapshots into a knowledge space

### V3 deliverables
- `DocAgent.Generators`
- `NuGetMetadataSource`
- Cross-repo “workspace” query tools

---

## CI & quality gates (all versions)

- `dotnet test` must pass
- Coverage threshold enforced (set your target; recommended 80%+ for core)
- Linting: dotnet format / analyzers (V2+)
- Security checks: no accidental secret capture in artifacts
