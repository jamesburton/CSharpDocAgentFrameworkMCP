# Phase 2: Ingestion Pipeline - Context

**Gathered:** 2026-02-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Walk Roslyn symbols from .NET projects, parse XML doc comments, and produce deterministic `SymbolGraphSnapshot` artifacts. A real .NET project can be ingested and produces byte-identical snapshots across runs. Project discovery, symbol walking, XML parsing, snapshot storage, and determinism verification are in scope. Search indexing, querying, and MCP serving are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Symbol Scope
- Ingest **public and protected** symbols only (API surface + inheritance-visible members)
- **Full depth** nesting — walk all nested types recursively
- **All semantic edges**: containment, inheritance, interface implementation, parameter/return type references
- **Generated code included but tagged** — ingest symbols with `[GeneratedCode]` attribute or from obj/ but mark them with a flag so downstream consumers can filter

### XML Doc Handling
- **Synthesize placeholder** for symbols with no XML doc comment — generate minimal text like "No documentation provided" so every node has searchable text
- **Inherit docs from base types** when a derived member has no docs — copy base docs and mark as inherited
- **Best-effort parse for malformed XML** — extract what's parseable, attach a warning flag to the DocComment, don't fail the whole ingestion
- **Full structured parse** of XML doc elements: summary, remarks, param, returns, example, exception, seealso, typeparam into typed fields on DocComment

### Project Discovery
- **Solution-first with csproj override** — default to .sln file discovery for all projects; allow overriding with explicit .csproj paths for filtering
- **Exclude test projects by convention** — skip projects matching *.Tests, *.Test, *.Specs patterns; user can override to include
- **Pick highest TFM** for multi-targeted projects — use the newest target framework (symbol surface is usually a superset)
- **Direct NuGet dependency public API ingestion** — ingest public symbols from direct NuGet package references (Roslyn compilation already resolves these)

### Snapshot Identity
- **Content hash + git metadata** — content hash (from Phase 1's ContentHash) is the identity; git commit SHA and ingestion timestamp are metadata annotations
- **MessagePack storage in artifacts/** — store as `artifacts/{content-hash}.msgpack`
- **Manifest file** — `manifest.json` listing all snapshots with metadata (project, git commit, timestamp, content hash) for querying without scanning files

### Claude's Discretion
- Snapshot retention policy (keep all vs bounded)
- Exact manifest.json schema
- Roslyn workspace configuration details
- Compilation error handling strategy (warn vs skip vs fail)

</decisions>

<specifics>
## Specific Ideas

- The ingestion model should be **extensible for multiple source tiers** (designed but not all implemented in Phase 2):
  1. Primary source — projects in the solution
  2. Secondary source locations — additional own-source directories beyond the solution
  3. Dependency source — git repo links for NuGet packages to ingest from source
  4. Dependency API — public API from direct NuGet dependencies (this tier is in scope for Phase 2)
- The `IProjectSource` interface from Phase 1 should be the extension point for these tiers

</specifics>

<deferred>
## Deferred Ideas

- **Secondary source locations** — ability to list additional own-source directories beyond the solution for ingestion (future phase or extension)
- **Git repo-linked dependency ingestion** — adding a git repo link for a NuGet package to ingest its source code directly (future phase)

</deferred>

---

*Phase: 02-ingestion-pipeline*
*Context gathered: 2026-02-26*
