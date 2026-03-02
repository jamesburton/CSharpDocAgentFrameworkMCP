# Project Research Summary

**Project:** DocAgentFramework
**Domain:** .NET code intelligence framework — housekeeping milestone
**Researched:** 2026-03-02
**Confidence:** HIGH

## Executive Summary

v1.3 is a consolidation milestone clearing backlog accumulated across v1.0-v1.2. The primary deliverable is INGEST-05 (per-project incremental solution re-ingestion), which extends the proven SHA-256 file-hashing pattern from project-level to solution-level ingestion. Secondary deliverables are MSBuild performance benchmarking, stale code cleanup, and documentation refresh.

No new core technologies are needed. The only potential addition is BenchmarkDotNet for the performance spike, and even that is optional if a simpler Stopwatch approach suffices. The architecture changes are localized to SolutionIngestionService — no new projects or contracts needed.

The key risk is cross-project edge staleness when skipping unchanged projects whose dependencies changed. This must be addressed in the INGEST-05 design with dependency-cascade dirtiness tracking.

## Key Findings

### Recommended Stack

No new core stack. Optional BenchmarkDotNet 0.14.0 for performance spike.

**Existing stack is sufficient:**
- .NET 10, Roslyn 4.12.0, MessagePack 3.1.4
- SHA-256 incremental pattern already proven in IncrementalIngestionEngine

### Expected Features

**Must have (table stakes):**
- INGEST-05: Per-project incremental solution re-ingestion
- Stale TODO/comment removal (2 known items)
- Documentation alignment to v1.0-v1.2 reality

**Should have:**
- MSBuild memory/latency benchmarks with regression guards
- v1.2 audit artifact cleanup

### Architecture Approach

Modify SolutionIngestionService to track per-project file-hash manifests and skip unchanged projects during solution ingestion. Must still open full MSBuildWorkspace (cross-project edges need full compilation). Key addition: dependency-cascade logic that marks downstream projects dirty when their dependencies change.

### Critical Pitfalls

1. **Cross-project edge staleness** — skip unchanged project but its dependencies changed leading to stale edges. Prevent with dependency cascade.
2. **Manifest path collision** — same-named projects in different dirs. Use path-based keys.
3. **Benchmark instability** — MSBuild perf varies. Use generous thresholds.
4. **Docs drift** — refresh docs last, after all code changes.

## Implications for Roadmap

### Phase 19: Incremental Solution Re-ingestion (INGEST-05)
**Rationale:** Primary deferred feature, highest value, most complex
**Delivers:** Per-project skip in SolutionIngestionService, correctness tests
**Avoids:** Edge staleness pitfall via dependency cascade

### Phase 20: MSBuild Performance Benchmarks
**Rationale:** Addresses flagged risk; should run after INGEST-05 to measure improvement
**Delivers:** Benchmark suite, baseline numbers, optional regression guard

### Phase 21: Code and Audit Cleanup
**Rationale:** Low-risk, independent work
**Delivers:** Removed stale TODOs, cleaned audit artifacts

### Phase 22: Documentation Refresh
**Rationale:** Must be last — reflects all changes made in prior phases
**Delivers:** Updated Architecture.md, Plan.md, Testing.md

### Phase Ordering Rationale

- INGEST-05 first: highest value, benchmarks benefit from measuring it
- Benchmarks second: measure before/after INGEST-05
- Code cleanup third: independent, low-risk
- Docs last: captures final state accurately

### Research Flags

Phases with standard patterns (skip research-phase):
- **All phases:** Well-understood existing codebase, no external APIs or new domains

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | No new tech needed |
| Features | HIGH | All items from known backlog |
| Architecture | HIGH | Extends proven patterns |
| Pitfalls | HIGH | Based on v1.1 incremental experience |

**Overall confidence:** HIGH

### Gaps to Address

- MSBuildWorkspace memory behavior at scale (>20 projects) — benchmark phase will quantify
- Optimal dependency-cascade algorithm — design during INGEST-05 planning

---
*Research completed: 2026-03-02*
*Ready for roadmap: yes*
