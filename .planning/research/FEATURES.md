# Feature Research

**Domain:** .NET code intelligence framework — housekeeping milestone
**Researched:** 2026-03-02
**Confidence:** HIGH

## Feature Landscape

### Table Stakes (Must Do for Housekeeping)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| INGEST-05: Per-project incremental solution re-ingestion | Deferred from v1.2, explicitly promised | MEDIUM | IncrementalIngestionEngine exists at project level; needs integration into SolutionIngestionService |
| Stale comment cleanup | Tech debt accumulates credibility cost | LOW | 2 known stale TODOs, plus v1.2 audit artifacts |
| Documentation refresh | docs/ files reference v1.0 plan, missing v1.1/v1.2 reality | LOW-MEDIUM | Architecture.md, Plan.md, Testing.md need alignment |

### Differentiators (Add Value Beyond Cleanup)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| MSBuild memory/latency benchmarks | Quantified performance baseline; regression detection | MEDIUM | No current benchmarks exist; flagged risk in STATE.md |
| Benchmark regression guard in tests | Prevents silent perf degradation | LOW | Simple test asserting ingestion stays under threshold |

### Anti-Features (Do Not Build)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Full incremental solution re-ingestion with project-level diffing | Only re-ingest changed projects | MSBuildWorkspace must open full solution for cross-project edges; skipping projects loses edge accuracy | Per-project SHA-256 skip of unchanged files within full solution open |
| Parallel project ingestion within solution | Performance | MSBuildWorkspace is not thread-safe | Sequential with incremental skip |

## Feature Dependencies

```
[INGEST-05] ──requires──> [Existing IncrementalIngestionEngine]
[INGEST-05] ──requires──> [Existing SolutionIngestionService]
[Benchmarks] ──independent──> [INGEST-05]
[Docs refresh] ──independent──> [Everything else]
[Code cleanup] ──independent──> [Everything else]
```

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| INGEST-05 | HIGH | MEDIUM | P1 |
| MSBuild benchmarks | MEDIUM | MEDIUM | P1 |
| Docs refresh | MEDIUM | LOW | P2 |
| Stale code cleanup | LOW | LOW | P2 |

---
*Feature research for: DocAgentFramework v1.3 Housekeeping*
*Researched: 2026-03-02*
