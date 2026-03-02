# Phase 19: Incremental Solution Re-ingestion - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Solution re-ingestion skips unchanged projects, producing a byte-identical result to full re-ingestion for unchanged input. Includes per-project manifest comparison, dependency cascade for dirty-marking, and stub correctness guarantees.

</domain>

<decisions>
## Implementation Decisions

### Manifest storage & keys
- Manifests stored alongside snapshot output (co-located with the data they describe)
- Keys use solution-relative paths (e.g., `src/MyProject/Foo.cs`) to prevent collision per INGEST-03
- Hash covers source files (.cs) AND project-to-project references + target framework
- Manifests reset on snapshot version bump — new version = full re-ingest

### Dependency cascade
- Full transitive closure: if A→B→C and A changes, both B and C re-ingest
- Re-ingest in topological (dependency) order — leaves first, matching MSBuild build order
- Structural graph changes (project reference added/removed) trigger full solution re-ingestion
- Circular project references are treated as an error with clear diagnostic

### Observability & diagnostics
- Per-project log line for each skip/re-ingest decision (e.g., "Skipped MyProject (unchanged)")
- Structured result metadata returned alongside the snapshot: which projects skipped, which re-ingested, and why
- Telemetry counters (projects_skipped, projects_reingested) via existing OpenTelemetry/Aspire infra
- Force-full-reingest boolean parameter as escape hatch to bypass incremental logic

### Stub lifecycle
- Skipped projects preserve and reuse their existing stubs in the graph
- Projects removed from the solution have their stubs and nodes cleaned from the graph
- Projects that move directories treated as remove + add (old path removed, new path fully ingested)
- Byte-identical integrity test: run both full and incremental ingestion, assert identical snapshots (validates INGEST-05)

### Claude's Discretion
- SHA-256 vs other hash algorithm choice
- Manifest file format (JSON, binary, etc.)
- Exact telemetry meter/counter naming
- Internal caching strategy during topological traversal

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. The key constraint is determinism: incremental must produce byte-identical output to full ingestion when nothing changed.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 19-incremental-solution-re-ingestion*
*Context gathered: 2026-03-02*
