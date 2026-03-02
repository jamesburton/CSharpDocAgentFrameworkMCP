# Architecture Research

**Domain:** .NET code intelligence framework — housekeeping milestone
**Researched:** 2026-03-02
**Confidence:** HIGH

## Integration Points for INGEST-05

### Current Flow (Full Re-ingestion)

```
ingest_solution MCP tool
    |
SolutionIngestionService.IngestAsync()
    |
MSBuildWorkspace.OpenSolutionAsync(slnPath)
    |
foreach project in solution.Projects (sequential)
    |
    GetCompilationAsync() -> Walk all symbols -> Build nodes/edges
    |
Merge all projects -> SymbolGraphSnapshot + SolutionSnapshot
    |
Store + Index
```

### Target Flow (Incremental)

```
ingest_solution MCP tool
    |
SolutionIngestionService.IngestAsync()
    |
MSBuildWorkspace.OpenSolutionAsync(slnPath)  <-- still needed for cross-project edges
    |
Load previous SolutionSnapshot + per-project file-hash manifests
    |
foreach project in solution.Projects (sequential)
    |
    Compare file hashes -> skip unchanged projects entirely
    |
    Changed: GetCompilationAsync() -> Walk symbols -> Build nodes/edges
    Unchanged: Reuse nodes/edges from previous snapshot
    |
Merge all projects -> new SymbolGraphSnapshot + SolutionSnapshot
    |
Store + Index + persist per-project manifests
```

### Modified Components

| Component | Change Type | Details |
|-----------|-------------|---------|
| SolutionIngestionService | **Modified** | Add per-project hash manifest tracking, skip unchanged projects |
| IncrementalIngestionEngine | **Reused** (reference pattern) | Existing SHA-256 manifest logic to adapt for solution context |
| SnapshotStore | **No change** | Already stores snapshots by path |
| ISearchIndex | **No change** | Receives final merged snapshot as before |

### New Components

| Component | Location | Purpose |
|-----------|----------|---------|
| Per-project manifest storage | SolutionIngestionService (internal) | Track SHA-256 file hashes per project within solution artifacts dir |

### Build Order

1. **INGEST-05** — modify SolutionIngestionService with per-project incremental logic
2. **Benchmarks** — measure before/after INGEST-05
3. **Code cleanup** — independent, any order
4. **Docs last** — captures final state accurately

## Anti-Patterns

### Anti-Pattern: Skipping MSBuildWorkspace Open

**What people do:** Try to avoid opening the full solution for unchanged projects
**Why it is wrong:** Cross-project edges require full compilation context; partial opens lose type resolution
**Do this instead:** Always open full solution, but skip per-project symbol walking when file hashes match

### Anti-Pattern: Parallel Project Compilation

**What people do:** Try to parallelize GetCompilationAsync() across projects
**Why it is wrong:** MSBuildWorkspace is not thread-safe
**Do this instead:** Sequential with incremental skip

---
*Architecture research for: DocAgentFramework v1.3 Housekeeping*
*Researched: 2026-03-02*
