# Pitfalls Research

**Domain:** .NET code intelligence framework — housekeeping milestone
**Researched:** 2026-03-02
**Confidence:** HIGH

## Critical Pitfalls

### Pitfall 1: Cross-Project Edge Staleness After Incremental Skip

**What goes wrong:**
Reusing nodes from a previous snapshot for an unchanged project, but the project dependencies changed. Cross-project edges become stale because the unchanged project type references now point to different symbols.

**Why it happens:**
File hashes within project A did not change, so it is skipped. But project B (which A depends on) changed its public API. The edges from A to B are now wrong.

**How to avoid:**
When a project direct dependencies have changed files, mark that project as dirty even if its own files are unchanged. Cascade dirtiness through the project dependency graph.

**Warning signs:**
Cross-project edge counts differ between incremental and full re-ingestion for the same input.

**Phase to address:**
INGEST-05 implementation phase — must be in the design, not retrofitted.

---

### Pitfall 2: Manifest File Location Collision

**What goes wrong:**
Per-project file-hash manifests collide if two projects share the same name in different directories.

**Why it happens:**
Using just project.Name as the manifest filename without considering the full path.

**How to avoid:**
Use a deterministic hash of the project file path as the manifest key, or use the project relative path within the solution.

**Warning signs:**
Manifest files getting overwritten unexpectedly; incremental correctness tests failing intermittently.

**Phase to address:**
INGEST-05 implementation phase.

---

### Pitfall 3: Benchmark Instability in CI

**What goes wrong:**
Benchmarks produce wildly different results between runs, making regression guards flaky.

**Why it happens:**
MSBuildWorkspace performance depends on disk I/O, .NET SDK warm-up, available memory, and background processes.

**How to avoid:**
Use generous thresholds (2-3x baseline) for regression guards. Keep benchmarks as informational spike tests, not hard CI gates. Use warm-up runs.

**Warning signs:**
Test failures in CI that pass locally; benchmark variance > 50% between runs.

**Phase to address:**
Benchmark phase — design thresholds conservatively.

---

### Pitfall 4: Docs Drift During Implementation

**What goes wrong:**
Refreshing docs before implementation phases, then the implementation changes things that invalidate the docs again.

**Why it happens:**
Natural ordering mistake — docs refresh feels like it should come first.

**How to avoid:**
Do docs refresh LAST, after all code changes are complete.

**Warning signs:**
Having to re-edit docs after code phases complete.

**Phase to address:**
Phase ordering — docs refresh should be the final phase.

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Loading full MSBuildWorkspace per MCP call | >30s latency, memory spikes | Workspace caching (already in place) | Solutions >20 projects |
| Computing SHA-256 for all files on every call | Latency proportional to total file count | Per-project manifests persisted to disk | Solutions with >1000 .cs files |

## Looks Done But Is Not Checklist

- [ ] **INGEST-05:** Verify incremental result is byte-identical to full re-ingestion
- [ ] **INGEST-05:** Verify cross-project edges are correct when only some projects changed
- [ ] **INGEST-05:** Verify dependency cascade marks downstream projects dirty
- [ ] **Benchmarks:** Verify measurements are stable across 3+ runs
- [ ] **Docs:** Verify all MCP tool names, counts, and descriptions match actual code
- [ ] **Cleanup:** Verify removed code has no remaining references

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Cross-project edge staleness | INGEST-05 | Incremental vs full comparison test |
| Manifest collision | INGEST-05 | Multi-project test with same-name projects |
| Benchmark instability | Benchmarks | Run 3+ times, check variance |
| Docs drift | Docs (last phase) | Ordering prevents it |

---
*Pitfalls research for: DocAgentFramework v1.3 Housekeeping*
*Researched: 2026-03-02*
