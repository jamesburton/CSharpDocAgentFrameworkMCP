# Phase 21: Code and Audit Cleanup - Research

**Researched:** 2026-03-03
**Domain:** C# codebase hygiene, audit artifact cleanup, OTel wiring, BenchmarkDotNet
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- This is codebase hygiene — no new features, no behavioral changes beyond fixing broken wiring.
- Scan entire codebase for TODO/FIXME/stub comments, not just the 2 specified. Triage each: remove stale ones, keep legitimate ones.

### Claude's Discretion

- Audit artifact cleanup scope and file organization
- Benchmark fix approach (wrap vs restructure vs new benchmark)
- Whether to capture real baseline values
- Which tech debt items to include based on effort/impact
- Staleness determination for each TODO/stub found
- Whether to archive audit files to milestones/ or keep in .planning/

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope

</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| QUAL-01 | Stale "TODO: replace with BM25" comment removed from `InMemorySearchIndex.cs` | Comment confirmed at line 27; BM25 was implemented in Phase 3; comment is definitively stale |
| QUAL-02 | Stale "stub" comment removed from `KnowledgeQueryService.cs:215` | Comment confirmed at line 215; `GetReferencesAsync` is fully implemented in Phase 5/6; label is stale |
| QUAL-03 | v1.2 audit artifact issues resolved (stale frontmatter, documentation gaps) | v1.2-MILESTONE-AUDIT.md identified 4 tech debt items; only the 16-01-SUMMARY.md missing `requirements_completed` is a documentation gap (the rest are code issues covered by QUAL-01/02) |

</phase_requirements>

---

## Summary

Phase 21 is a focused hygiene pass with five concrete, bounded tasks. All work items are directly traceable to specific files and line numbers identified during the v1.2 and v1.3 milestone audits. There is no ambiguity about what to change — the research below documents each fix precisely.

The two stale comments (QUAL-01, QUAL-02) are simple single-line deletions. The audit artifact issue (QUAL-03) is a missing frontmatter field in one SUMMARY file. The two integration wiring fixes (from the v1.3 audit's integration gaps) are small code changes: updating a benchmark to use the decorator service, and adding one `AddMeter` call to `Program.cs`.

The broader stale-comment scan (per CONTEXT.md discretion) requires reading all `.cs` files in the `src/` tree. The research below categorizes what was found and provides staleness guidance.

**Primary recommendation:** Implement in a single plan (21-01) covering all five specific success criteria, with a discretionary second task (21-02) for the broader TODO/FIXME scan if the scope warrants it.

---

## Standard Stack

### Core

| Tool | Version | Purpose | Notes |
|------|---------|---------|-------|
| .NET 10 / C# | net10.0 | Target runtime | LangVersion=preview, TreatWarningsAsErrors=true |
| xUnit | 2.x | Test framework | Already in project |
| OpenTelemetry | Current | OTel metrics registration | `metrics.AddMeter()` call pattern |
| BenchmarkDotNet | 0.15.8 | Benchmark framework | Already used in DocAgent.Benchmarks |

No new packages required. All fixes use existing dependencies.

---

## Architecture Patterns

### The Five Specific Fixes

**Fix 1 — QUAL-01: Remove stale BM25 TODO**

File: `src/DocAgent.Indexing/InMemorySearchIndex.cs`, line 27

```csharp
// BEFORE (stale — BM25 was implemented in Phase 3):
// TODO: replace with BM25/inverted index

// AFTER: delete the comment line entirely
```

Staleness evidence: The `#pragma disable CS1998` block and linear scan implementation have been present since Phase 3 (v1.0) when BM25 was implemented in a separate class. The comment was written speculatively and was never removed. `InMemorySearchIndex` is intentionally kept as a lightweight fallback; the TODO no longer applies.

---

**Fix 2 — QUAL-02: Remove stale stub label**

File: `src/DocAgent.Indexing/KnowledgeQueryService.cs`, line 215

```csharp
// BEFORE (stale — GetReferencesAsync is fully implemented):
// GetReferencesAsync (stub — MCPS-03, Phase 5/6 concern)

// AFTER: replace with a clean section header, e.g.:
// -------------------------------------------------------------------------
// GetReferencesAsync
// -------------------------------------------------------------------------
```

Staleness evidence: `GetReferencesAsync` returns real edges from the snapshot. The "stub" label was written in Phase 5/6 planning era and never updated. The method is substantive code, not a stub.

---

**Fix 3 — QUAL-03: Repair 16-01-SUMMARY.md frontmatter**

File: `.planning/milestones/v1.2-phases/16-solution-mcp-tools/16-01-SUMMARY.md`

The v1.2 audit identified: "16-01 SUMMARY has empty requirements_completed frontmatter (TOOLS-05 verified in VERIFICATION.md)". The `requirements_completed` field is absent from the frontmatter entirely. Fix: add the field with the correct requirement IDs.

```yaml
# Add to frontmatter:
requirements_completed: [TOOLS-05]
```

TOOLS-05 is `explain_solution` tool, which is what 16-01-PLAN.md implemented. The VERIFICATION.md confirms it satisfied.

No other v1.2 audit artifact issues are documentation gaps — the remaining tech debt items (TODO in InMemorySearchIndex, stale comment in KnowledgeQueryService, 3 human verification items) are addressed by QUAL-01/02 or are permanently deferred human-verification items.

---

**Fix 4 — Integration Gap: Benchmark uses wrong service type**

File: `tests/DocAgent.Benchmarks/SolutionIngestionBenchmarks.cs`

The v1.3 audit identified: `IncrementalNoChange` benchmark instantiates `SolutionIngestionService` directly, so the skip path is never exercised.

Current GlobalSetup (lines 35-38):
```csharp
var store = new SnapshotStore(_tempDir);
var index = new InMemorySearchIndex();
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SolutionIngestionService>();
_service = new SolutionIngestionService(store, index, logger);
```

`IncrementalSolutionIngestionService` constructor signature (from source):
```csharp
public IncrementalSolutionIngestionService(
    SnapshotStore store,
    SolutionIngestionService fullIngestionService,
    ILogger<IncrementalSolutionIngestionService> logger)
```

Recommended fix approach: Change `_service` field type to `ISolutionIngestionService`, create both services in GlobalSetup, wrap the concrete service in the decorator. The `IncrementalNoChange` benchmark method already calls `_service.IngestAsync(...)` twice correctly — no change needed there.

```csharp
// Updated GlobalSetup:
var store = new SnapshotStore(_tempDir);
var index = new InMemorySearchIndex();
var fullLogger = LoggerFactory.Create(b => b.AddConsole())
    .CreateLogger<SolutionIngestionService>();
var fullService = new SolutionIngestionService(store, index, fullLogger);
var incrLogger = LoggerFactory.Create(b => b.AddConsole())
    .CreateLogger<IncrementalSolutionIngestionService>();
_service = new IncrementalSolutionIngestionService(store, fullService, incrLogger);
```

The `FullSolutionIngestion` benchmark also calls `_service.IngestAsync(..., forceFullReingest: true)` — `IncrementalSolutionIngestionService.IngestAsync` passes `forceFullReingest` through to the full service, so this benchmark still exercises the full path correctly.

Note: `_service` field is declared as `SolutionIngestionService _service` at line 20 — this declaration must change to `ISolutionIngestionService _service`.

---

**Fix 5 — Integration Gap: AddMeter missing in Program.cs**

File: `src/DocAgent.McpServer/Program.cs`

The v1.3 audit identified: `metrics.AddMeter("DocAgent.Ingestion")` missing in the `WithMetrics` lambda. OTel counters from `IncrementalSolutionIngestionService` fire but are silently dropped because the meter is not registered with the OTel pipeline.

Current `.WithMetrics` block (lines 46-50):
```csharp
.WithMetrics(metrics =>
{
    metrics.AddRuntimeInstrumentation();
    metrics.AddOtlpExporter();
});
```

Fix: add `metrics.AddMeter("DocAgent.Ingestion")`:
```csharp
.WithMetrics(metrics =>
{
    metrics.AddRuntimeInstrumentation();
    metrics.AddMeter("DocAgent.Ingestion");
    metrics.AddOtlpExporter();
});
```

The meter name `"DocAgent.Ingestion"` matches `IncrementalSolutionIngestionService`'s `new Meter("DocAgent.Ingestion")` declaration at line 18 of that file.

---

### Broader TODO/FIXME Scan Findings

A grep of all `.cs` files in `src/` for `TODO|FIXME|HACK|stub` produced the following entries beyond the two required removals:

| File | Pattern | Text | Disposition |
|------|---------|------|-------------|
| `InMemorySearchIndex.cs:27` | TODO | "TODO: replace with BM25/inverted index" | **Remove** (QUAL-01) |
| `KnowledgeQueryService.cs:215` | stub | "stub — MCPS-03, Phase 5/6 concern" | **Remove** (QUAL-02) |
| `SolutionTools.cs:15,48,143` | stub | Various "stub node" references in doc/code | **Keep** — "stub node" is the domain term for external symbol nodes; not stale |
| `SolutionIngestionService.cs:20,33,106,265,321,517,532,652` | stub | "stub nodes" throughout | **Keep** — domain terminology |
| `IncrementalSolutionIngestionService.cs:13` | stub | "stub node lifecycle" in XML doc | **Keep** — domain terminology |
| `Symbols.cs:60,65,76` | stub | `SymbolOrigin.Stub` enum and related docs | **Keep** — core domain type |

No additional stale TODOs or FIXMEs found. The only stale items are the two explicitly required by QUAL-01/QUAL-02.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| OTel meter registration | Custom instrumentation bridge | `metrics.AddMeter("DocAgent.Ingestion")` — single call |
| Service composition in benchmark | Factory or DI container | Construct manually in GlobalSetup (existing pattern in this file) |

---

## Common Pitfalls

### Pitfall 1: Changing `_service` field type breaks FullSolutionIngestion benchmark

**What goes wrong:** If `_service` is changed to `IncrementalSolutionIngestionService` but `FullSolutionIngestion` benchmark passes `forceFullReingest: true`, this works correctly because `IncrementalSolutionIngestionService.IngestAsync` delegates to `SolutionIngestionService` when `forceFullReingest` is true.

**Verification:** Confirm `IncrementalSolutionIngestionService.IngestAsync` checks `forceFullReingest` before attempting skip logic (it does — the service returns early to the full service when forced).

### Pitfall 2: AddMeter name must match exactly

**What goes wrong:** If meter name differs between registration and the `new Meter(...)` call, counters remain dropped silently.

**Prevention:** The meter name is `"DocAgent.Ingestion"` in both `IncrementalSolutionIngestionService.cs:18` and the fix. Use the string constant, not a derived value.

### Pitfall 3: Removing "stub" comment triggers TreatWarningsAsErrors

**What goes wrong:** Accidentally removing an XML doc comment could trigger CS1591 (missing XML comment for publicly visible type).

**Prevention:** Only remove the inline `// stub — ...` comment inside the method body, not the XML doc comment above the method.

### Pitfall 4: 16-01-SUMMARY.md frontmatter format

**What goes wrong:** Wrong YAML indentation or wrong requirement ID breaks frontmatter parsing.

**Prevention:** Match the YAML key naming used by other SUMMARY files (`requirements_completed:` as a list). Check adjacent SUMMARY files for the expected format.

---

## Validation Architecture

Test framework: xUnit + FluentAssertions. Quick run: `dotnet test`. No new tests are required for these changes — they are comment removals, frontmatter repairs, and wiring fixes. The existing test suite (303+ tests) serves as the regression guard.

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Validation |
|--------|----------|-----------|------------|
| QUAL-01 | Comment absent from file | Manual grep / code review | `grep "TODO.*BM25" src/DocAgent.Indexing/InMemorySearchIndex.cs` returns nothing |
| QUAL-02 | Comment absent from file | Manual grep / code review | `grep "stub.*MCPS-03" src/DocAgent.Indexing/KnowledgeQueryService.cs` returns nothing |
| QUAL-03 | 16-01-SUMMARY.md has requirements_completed | Manual frontmatter check | `grep "requirements_completed" .planning/milestones/v1.2-phases/16-solution-mcp-tools/16-01-SUMMARY.md` returns value |
| SC-4 | IncrementalNoChange uses decorator service | Build + inspection | `dotnet build` passes; field type is `ISolutionIngestionService` |
| SC-5 | AddMeter registered | Build + inspection | `dotnet build` passes; grep confirms `AddMeter("DocAgent.Ingestion")` in Program.cs |

**Regression gate:** `dotnet test` must pass (all 303+ tests) after all changes.

---

## Recommended Plan Structure

### Option A: Single plan (recommended)

21-01-PLAN.md covers all five success criteria. They are all small, sequential, non-conflicting edits. Estimated work: 30-45 minutes.

**Task order within the plan:**
1. Remove QUAL-01 comment (`InMemorySearchIndex.cs:27`)
2. Remove QUAL-02 comment (`KnowledgeQueryService.cs:215`)
3. Fix 16-01-SUMMARY.md frontmatter (QUAL-03)
4. Fix benchmark service type (SC-4)
5. Add `AddMeter` to Program.cs (SC-5)
6. `dotnet test` to confirm no regressions

### Option B: Two plans

21-01: Code fixes (QUAL-01, QUAL-02, SC-4, SC-5)
21-02: Audit artifact cleanup (QUAL-03 + broader scan report)

Option B adds overhead for minimal gain. Option A is preferred.

---

## Open Questions

1. **Should the v1.2 audit document be updated to mark the 16-01 frontmatter issue as resolved?**
   - What we know: v1.2-MILESTONE-AUDIT.md lists it as a tech debt item
   - Recommendation: Update the audit doc's tech_debt section to reflect the fix as part of the same plan

2. **Should `baselines.json` placeholder values be updated with real benchmark measurements?**
   - What we know: The v1.3 audit flagged this as tech debt; CONTEXT.md says Claude decides whether to capture real values
   - Recommendation: Defer. Running real BenchmarkDotNet measurements takes 10-30 minutes and requires Release build. The regression guard already handles this case (silently passes when baselines are generous). Include as a discretionary task if time permits.

---

## Sources

### Primary (HIGH confidence)

- Direct codebase inspection via Read/Grep tools — all file paths and line numbers verified
- `src/DocAgent.Indexing/InMemorySearchIndex.cs` — QUAL-01 comment confirmed at line 27
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — QUAL-02 comment confirmed at line 215
- `tests/DocAgent.Benchmarks/SolutionIngestionBenchmarks.cs` — SC-4 issue confirmed; field type and GlobalSetup pattern inspected
- `src/DocAgent.McpServer/Program.cs` — SC-5 gap confirmed; `WithMetrics` block inspected at lines 46-50
- `src/DocAgent.McpServer/Ingestion/IncrementalSolutionIngestionService.cs` — meter name `"DocAgent.Ingestion"` confirmed at line 18
- `.planning/milestones/v1.2-MILESTONE-AUDIT.md` — QUAL-03 source of truth
- `.planning/milestones/v1.2-phases/16-solution-mcp-tools/16-01-SUMMARY.md` — missing `requirements_completed` confirmed
- `.planning/v1.3-MILESTONE-AUDIT.md` — integration gaps confirmed (SC-4, SC-5)

---

## Metadata

**Confidence breakdown:**
- What to change: HIGH — every fix is pinpointed to file + line
- How to change: HIGH — patterns are straightforward; no novel API required
- Pitfalls: HIGH — identified from direct code inspection, not speculation

**Research date:** 2026-03-03
**Valid until:** This research describes the exact current state of the codebase. Valid until any of the five target files are changed.
