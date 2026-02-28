# Phase 11: Change Intelligence & Review - Research

**Researched:** 2026-02-28
**Domain:** MCP tool layer over existing SymbolGraphDiffer — C#/.NET, DocAgent.McpServer patterns
**Confidence:** HIGH (primary source is the existing codebase; no external library uncertainty)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Change Severity & Classification**
- Breaking changes defined by public API surface only (public/protected symbols). Internal changes are non-breaking.
- Three-tier severity system: Breaking / Warning / Info
- "Potentially Breaking" label for risky-but-not-technically-breaking changes (accessibility widening, adding optional params). Maps to Warning tier.
- Trivial changes (doc-only, whitespace, member reordering) filtered out by default; include only when verbose mode requested.

**Review Findings Format**
- `review_changes` groups findings by severity, then by symbol — breaking changes surface first for priority-driven reading.
- Each finding includes actionable remediation suggestions (e.g., "Add overload with old signature for backward compat").
- Output includes a summary section (counts per severity, overall risk assessment) followed by detailed findings.
- `find_breaking_changes` is a separate dedicated tool, not a filter on `review_changes`. Cleaner API for CI/automation use cases.

**Explanation Style**
- Target audience: developer reviewing a PR — assumes C# knowledge, uses code snippets, concise but precise.
- Always show before/after code snippets for every change (old signature → new signature).
- Include impact scope: list dependent symbols affected by the change ("This change affects 3 callers: X, Y, Z").
- Include "why this matters" natural language context (e.g., "This parameter type change will cause compile errors in any caller passing a string").

**Unusual Change Detection**
- Flag all four patterns proactively:
  - Accessibility widening (private → public, internal → public)
  - Nullability regression (non-nullable → nullable on return types/parameters)
  - Mass signature changes (many methods in one type changing simultaneously)
  - Constraint removal (generic constraints being removed)
- Unusual patterns reported proactively in review_changes output as a dedicated section — not a separate tool.
- Remediation is text-based suggestions (describe what to fix), not code patches or worktree creation.
- Thresholds use sensible defaults (e.g., >5 signature changes in one type = mass change). Not configurable via parameters.

### Claude's Discretion
- Exact threshold values for "mass change" detection
- Internal data structures for change classification
- How to format before/after code snippets (inline vs block)
- Error handling when snapshots are incompatible

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| R-CHANGE-TOOLS | MCP tools: `review_changes`, `find_breaking_changes`, `explain_change` | Three new `[McpServerTool]` methods in a new `ChangeTools` class following the `DocTools`/`IngestionTools` pattern; call `SymbolGraphDiffer.Diff()` directly via `SnapshotStore` |
| R-REVIEW-SKILL | Unusual change review — detect suspicious patterns and return actionable remediation suggestions | A pure `ChangeReviewer` service that inspects a `SymbolDiff` for four anomaly patterns; invoked by `review_changes` |
</phase_requirements>

---

## Summary

Phase 11 is a **pure wrapper phase**: all diff intelligence lives in `SymbolGraphDiffer` (Phase 9). The task is to plumb that engine into three MCP tools and a `ChangeReviewer` service that adds pattern detection on top of raw diffs.

The existing codebase follows a single, well-established pattern for MCP tools: a `[McpServerToolType]` class with `[McpServerTool]` methods, a `FormatResponse(json/markdown/tron)` triple-factory, `ErrorResponse()` helper, OpenTelemetry activity wrapping, and a prompt-injection scan on any user-derived text. Phase 11 must replicate this pattern for the three new tools.

**The key integration gap**: `IKnowledgeQueryService.DiffAsync()` returns the old shallow `GraphDiff` type (a pre-Phase-9 interface). Phase 11 tools must call `SymbolGraphDiffer.Diff()` directly with snapshots loaded from `SnapshotStore` — they do NOT go through `IKnowledgeQueryService.DiffAsync()`. A new service or direct injection is needed: either a `IChangeReviewService` (clean) or direct constructor injection of `SnapshotStore` into the new tool class.

**Primary recommendation:** Add a new `ChangeTools` class in `DocAgent.McpServer/Tools/` that injects `SnapshotStore` directly (the same way `KnowledgeQueryService` already does), plus a new `ChangeReviewer` pure-logic class in `DocAgent.McpServer/` (or ideally `DocAgent.Core/` if it has no MCP dependencies).

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `ModelContextProtocol` (McpServer) | Already in csproj | `[McpServerToolType]`, `[McpServerTool]` attributes | All MCP tools use it — Phase 5/8 established pattern |
| `DocAgent.Core` | Project ref | `SymbolDiff`, `SymbolGraphDiffer`, `SnapshotStore` accessor types | Phase 9 engine lives here |
| `DocAgent.Ingestion` | Project ref | `SnapshotStore` (MessagePack persistence) | Already injected in `KnowledgeQueryService` |
| `System.Text.Json` | .NET 10 BCL | JSON serialization for tool output | Used by all existing tools |
| xUnit + FluentAssertions | Already in `DocAgent.Tests.csproj` | Unit tests for `ChangeReviewer` logic | Project standard |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `OpenTelemetry` / `DocAgentTelemetry.Source` | Already wired | Activity tracing per tool call | Required for every `[McpServerTool]` method |
| `PromptInjectionScanner` | Project class | Sanitize doc content in `explain_change` output | Any output derived from doc comments |
| `TronSerializer` | Project class | Token-efficient output format | Add a new TRON shape for diff review results |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Direct `SnapshotStore` injection in `ChangeTools` | Extending `IKnowledgeQueryService` with a `SymbolDiffAsync()` overload | Extending the interface is cleaner long-term but requires touching `KnowledgeQueryService`, `IKnowledgeQueryService`, and all mock stubs in tests. Direct injection is simpler and follows Phase 8's `IngestionTools` precedent. |
| `ChangeReviewer` as a plain static class | DI-registered service | Static is fine if it has no dependencies; preferred for pure logic |

**Installation:** No new packages needed. All required types already in the project.

---

## Architecture Patterns

### Recommended Project Structure
```
src/DocAgent.McpServer/Tools/
├── DocTools.cs              # existing — search_symbols, get_symbol, get_references, diff_snapshots, explain_project
├── IngestionTools.cs        # existing — ingest_project
└── ChangeTools.cs           # NEW — review_changes, find_breaking_changes, explain_change

src/DocAgent.McpServer/Review/   (or inline in Tools/)
└── ChangeReviewer.cs        # NEW — pure logic for unusual change detection + remediation text

tests/DocAgent.Tests/ChangeReview/
├── ChangeReviewerTests.cs   # unit tests for pattern detection
└── ChangeToolTests.cs       # unit tests for MCP tool output shape
```

### Pattern 1: MCP Tool Class

All tool classes follow this shape exactly:

```csharp
// Source: src/DocAgent.McpServer/Tools/DocTools.cs (existing)
[McpServerToolType]
public sealed class ChangeTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SnapshotStore _snapshotStore;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<ChangeTools> _logger;
    private readonly DocAgentServerOptions _options;

    public ChangeTools(
        SnapshotStore snapshotStore,
        PathAllowlist allowlist,
        ILogger<ChangeTools> logger,
        IOptions<DocAgentServerOptions> options)
    { ... }

    [McpServerTool(Name = "review_changes"), Description("...")]
    public async Task<string> ReviewChanges(
        [Description("...")] string versionA,
        [Description("...")] string versionB,
        [Description("Include doc-only/trivial changes (default: false)")] bool verbose = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity("tool.review_changes", ActivityKind.Internal);
        activity?.SetTag("tool.name", "review_changes");
        try
        {
            // 1. Load snapshots from SnapshotStore
            // 2. Call SymbolGraphDiffer.Diff()
            // 3. Apply ChangeReviewer for unusual patterns
            // 4. Format and return
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

### Pattern 2: Snapshot Loading

`SnapshotStore.LoadAsync(id, ct)` is the correct call. `SnapshotRef.Id` is a content hash string. The existing `DiffAsync` in `KnowledgeQueryService` (lines 139-145) shows the exact pattern:

```csharp
// Source: src/DocAgent.Indexing/KnowledgeQueryService.cs lines 139-145
var snapshotA = await _snapshotStore.LoadAsync(a.Id, ct).ConfigureAwait(false);
if (snapshotA is null)
    return QueryResult<...>.Fail(QueryErrorKind.SnapshotMissing, $"Snapshot '{a.Id}' not found.");
```

For Phase 11, this becomes:

```csharp
var snapshotA = await _snapshotStore.LoadAsync(versionA, cancellationToken).ConfigureAwait(false);
if (snapshotA is null)
    return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionA}' not found.");

var snapshotB = await _snapshotStore.LoadAsync(versionB, cancellationToken).ConfigureAwait(false);
if (snapshotB is null)
    return ErrorResponse(QueryErrorKind.SnapshotMissing, $"Snapshot '{versionB}' not found.");

// SymbolGraphDiffer throws ArgumentException for mismatched ProjectName
SymbolDiff diff;
try { diff = SymbolGraphDiffer.Diff(snapshotA, snapshotB); }
catch (ArgumentException ex) { return ErrorResponse(QueryErrorKind.InvalidInput, ex.Message); }
```

### Pattern 3: ChangeReviewer — Unusual Pattern Detection

This is a pure class (no DI dependencies) that operates on a `SymbolDiff`:

```csharp
public static class ChangeReviewer
{
    // Threshold for "mass change" detection (Claude's discretion — 5 is recommended)
    private const int MassChangeThreshold = 5;

    public static ChangeReviewReport Analyze(SymbolDiff diff)
    {
        var unusualFindings = new List<UnusualFinding>();

        // Pattern 1: Accessibility widening
        foreach (var change in diff.Changes.Where(c =>
            c.Category == ChangeCategory.Accessibility &&
            c.AccessibilityDetail is not null))
        {
            var d = change.AccessibilityDetail!;
            if (IsWidening(d.OldAccessibility, d.NewAccessibility))
                unusualFindings.Add(new UnusualFinding(
                    Kind: UnusualKind.AccessibilityWidening,
                    SymbolId: change.SymbolId,
                    Description: $"{change.Description} — previously {d.OldAccessibility}, now {d.NewAccessibility}",
                    Remediation: "Verify intent: widening accessibility increases public API surface. If intentional, update XML docs. If unintentional, revert."));
        }

        // Pattern 2: Nullability regression
        // Pattern 3: Mass signature changes (group by ParentSymbolId, count > threshold)
        // Pattern 4: Constraint removal

        return new ChangeReviewReport(diff, unusualFindings);
    }
}
```

### Pattern 4: Three-Tier Severity Mapping

The `SymbolDiff` already uses `ChangeSeverity { Breaking, NonBreaking, Informational }`. Phase 11 maps this to the user's three-tier output system:

| Phase 9 `ChangeSeverity` | Phase 11 Output Tier |
|--------------------------|----------------------|
| `Breaking` | `"breaking"` |
| `NonBreaking` | `"warning"` (when involves unusual pattern) or `"info"` |
| `Informational` | `"info"` |

Accessibility widening changes have `Severity = NonBreaking` in the Phase 9 engine, but Phase 11's `ChangeReviewer` should escalate these to the `"warning"` tier.

### Pattern 5: `explain_change` — Impact Scope via SnapshotStore

To list dependent symbols ("3 callers: X, Y, Z"), `explain_change` must look up the `after` snapshot's edges where `To == changedSymbolId`. This is already available in `SymbolGraphSnapshot.Edges`. No new service needed:

```csharp
var callers = snapshotB.Edges
    .Where(e => e.To == symbolId && e.Kind == SymbolEdgeKind.Calls)
    .Select(e => e.From)
    .ToList();
```

### Pattern 6: MCP Tool Unit Test Pattern

Tests instantiate tool classes directly with stub dependencies — no MCP runtime:

```csharp
// Source: tests/DocAgent.Tests/McpToolTests.cs (existing pattern)
private static ChangeTools CreateTools(SnapshotStore? store = null, ...)
{
    // Use a real SnapshotStore pointing to a temp dir, or a stub
    store ??= new SnapshotStore(Path.GetTempPath());
    ...
    return new ChangeTools(store, allowlist, logger, opts);
}
```

For `ChangeReviewer`, tests operate directly on `SymbolDiff` built via `DiffTestHelpers` (from Phase 9's test helpers).

### Anti-Patterns to Avoid

- **Do not go through `IKnowledgeQueryService.DiffAsync()`**: That returns `GraphDiff` (old shallow type), not `SymbolDiff` (Phase 9 rich type). Phase 11 tools MUST call `SymbolGraphDiffer.Diff()` directly.
- **Do not add parameters to disable unusual change detection**: User locked "not configurable via parameters". Detection is always on; triviality filtering is the only toggle.
- **Do not create a new format beyond json/markdown/tron**: The existing triple-factory is the standard.
- **Do not add `verbose` parameter to `find_breaking_changes`**: It is a CI-focused tool. Its output should always be minimal (breaking changes only, no doc-only noise).
- **Do not include doc-only changes in `find_breaking_changes` output**: `ChangeSeverity.Informational` and `ChangeCategory.DocComment` changes are noise for CI.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting changed symbols between snapshots | Custom node-comparison loop | `SymbolGraphDiffer.Diff()` | Phase 9 already handles all 6 categories with deterministic ordering |
| Persisting/loading snapshots | File I/O code | `SnapshotStore.LoadAsync()` | Handles MessagePack deserialization, manifest lookup, ContentHash resolution |
| Before/after code snippet formatting | Custom display name builder | `SymbolNode.DisplayName`, `ParameterInfo` fields | Phase 9 populated these from Roslyn; `SignatureChangeDetail.OldReturnType` etc. are already strings |
| Counting callers / blast-radius | Graph traversal from scratch | `SymbolGraphSnapshot.Edges` filtered by `To == symbolId` | Edges are already indexed in the snapshot |
| Severity classification | New severity logic | `SymbolChange.Severity` from Phase 9 | `SymbolGraphDiffer` already classifies Breaking/NonBreaking/Informational correctly |

**Key insight:** Phase 11's job is orchestration and presentation, not new computation. Every hard question (what changed? how severe? which parameters changed?) is already answered by Phase 9 types.

---

## Common Pitfalls

### Pitfall 1: Snapshot Incompatibility (Different Projects)
**What goes wrong:** `SymbolGraphDiffer.Diff()` throws `ArgumentException` when `before.ProjectName != after.ProjectName`. If the tool propagates this as an unhandled exception, it becomes a 500-level MCP error.
**Why it happens:** User passes two snapshots from different ingest runs of different projects.
**How to avoid:** Wrap the `SymbolGraphDiffer.Diff()` call in a `try/catch (ArgumentException)` and return `ErrorResponse(QueryErrorKind.InvalidInput, ex.Message)`.
**Warning signs:** Test with two snapshots from different project names to confirm the catch.

### Pitfall 2: Treating `SymbolDiff.Changes` as Grouped
**What goes wrong:** `SymbolDiff.Changes` is a flat list sorted by SymbolId then Category. A single modified symbol can appear multiple times (once per change category). Iterating once and treating each entry as a distinct symbol will double-count.
**Why it happens:** Phase 9 design was explicit: flat list, consumers filter and group. See `DiffTypes.cs` line 110 comment: "Consumers (Phase 11 MCP tools) filter as needed."
**How to avoid:** Group by `SymbolId` before building review findings. Use `.GroupBy(c => c.SymbolId)`.

### Pitfall 3: `verbose=false` Swallowing All Changes
**What goes wrong:** If trivial-filtering logic is too aggressive, `review_changes` returns empty when all changes are doc-only. This is correct but surprising for users who expect something.
**Why it happens:** `ChangeSeverity.Informational` + `ChangeCategory.DocComment` = trivial. If verbose=false, ALL such changes are filtered.
**How to avoid:** Always include the summary section (counts) even when verbose=false. Users see "3 doc-only changes filtered" rather than "no results".

### Pitfall 4: Tron Schema Not Added
**What goes wrong:** `TronSerializer` has no method for review/diff results. If `format="tron"` is requested and the factory calls a non-existent method, it will fail at runtime.
**Why it happens:** New tools require new TRON schemas.
**How to avoid:** Add `TronSerializer.SerializeChangeReview(...)` and `TronSerializer.SerializeBreakingChanges(...)` in the same PR.

### Pitfall 5: Impact Scope Edge Direction
**What goes wrong:** Looking for callers of a changed method by filtering `Edges.Where(e => e.From == symbolId)` finds what the symbol calls, not what calls it. The direction is reversed.
**Why it happens:** `SymbolEdgeKind.Calls` edge is `From = caller, To = callee`.
**How to avoid:** Filter `Edges.Where(e => e.To == symbolId)` to find callers. Confirmed by `GetReferencesAsync` in `KnowledgeQueryService.cs` lines 229-233 which returns bidirectional edges.

---

## Code Examples

### review_changes JSON output shape (recommended)
```json
{
  "beforeVersion": "abc123",
  "afterVersion": "def456",
  "summary": {
    "breaking": 2,
    "warning": 1,
    "info": 3,
    "trivialFiltered": 5,
    "overallRisk": "high"
  },
  "findings": [
    {
      "symbolId": "MyAssembly::MyNs.MyClass.Process",
      "displayName": "Process",
      "severity": "breaking",
      "category": "Signature",
      "description": "Return type changed from 'string' to 'string?'",
      "before": "string Process(int id)",
      "after": "string? Process(int id)",
      "impactScope": ["MyNs.Consumer.Run", "MyNs.Worker.Execute"],
      "remediation": "Return type widened to nullable. Update all callers to handle null return."
    }
  ],
  "unusualFindings": [
    {
      "kind": "AccessibilityWidening",
      "symbolId": "...",
      "description": "...",
      "remediation": "..."
    }
  ]
}
```

### find_breaking_changes JSON output shape (CI-optimized, minimal)
```json
{
  "beforeVersion": "abc123",
  "afterVersion": "def456",
  "breakingCount": 2,
  "breakingChanges": [
    {
      "symbolId": "...",
      "displayName": "...",
      "description": "..."
    }
  ]
}
```

### explain_change JSON output shape
```json
{
  "symbolId": "...",
  "displayName": "Process",
  "changeType": "Modified",
  "changes": [
    {
      "category": "Signature",
      "severity": "breaking",
      "before": "string Process(int id)",
      "after": "string? Process(int id)",
      "whyItMatters": "Return type widened to nullable. Any caller that assigns the result without null check will now compile with a nullable warning or error (depending on project settings).",
      "impactScope": {
        "callerCount": 2,
        "callers": ["MyNs.Consumer.Run", "MyNs.Worker.Execute"]
      },
      "remediation": "Update callers to handle null return, or revert if unintentional."
    }
  ]
}
```

### Accessing `SymbolChange` detail fields
```csharp
// Source: src/DocAgent.Core/DiffTypes.cs (Phase 9)
// SymbolChange has exactly one non-null detail field matching its Category
var change = diff.Changes.First();
string before = "", after = "";

switch (change.Category)
{
    case ChangeCategory.Signature when change.SignatureDetail is { } sig:
        before = sig.OldReturnType ?? "void";
        after  = sig.NewReturnType ?? "void";
        // sig.ParameterChanges gives per-param before/after
        break;
    case ChangeCategory.Nullability when change.NullabilityDetail is { } nul:
        before = nul.OldAnnotation ?? "";
        after  = nul.NewAnnotation ?? "";
        break;
    case ChangeCategory.Accessibility when change.AccessibilityDetail is { } acc:
        before = acc.OldAccessibility.ToString();
        after  = acc.NewAccessibility.ToString();
        break;
    case ChangeCategory.Constraint when change.ConstraintDetail is { } con:
        before = string.Join(", ", con.RemovedConstraints);
        after  = string.Join(", ", con.AddedConstraints);
        break;
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `GraphDiff` / `DiffEntry` (shallow, string summaries) | `SymbolDiff` / `SymbolChange` (typed, categorized, severity-tagged) | Phase 9 (2026-02-28) | Phase 11 must use `SymbolDiff` path, not the `IKnowledgeQueryService.DiffAsync()` path |
| No production caller for `SymbolGraphDiffer.Diff()` | Phase 11 is the planned first consumer | — | Existing tech debt explicitly noted in v1.1-MILESTONE-AUDIT.md |

---

## Open Questions

1. **How should `SnapshotStore` be exposed in `ChangeTools`?**
   - What we know: `SnapshotStore` is already registered as a singleton in `DocAgentServiceCollectionExtensions`. `KnowledgeQueryService` injects it. `IngestionTools` uses `IIngestionService` instead.
   - What's unclear: Should `ChangeTools` inject `SnapshotStore` directly (concrete class, not interface) or should a new `IDiffService` interface wrap it?
   - Recommendation: Inject `SnapshotStore` directly in `ChangeTools` constructor (same as `KnowledgeQueryService`). No new interface needed — the interface doesn't buy testability here since `SnapshotStore` already supports temp-dir construction in tests.

2. **Where should `ChangeReviewer` live?**
   - What we know: It's pure logic with no I/O, no DI dependencies. The MCP serving layer (`DocAgent.McpServer`) is the primary consumer.
   - What's unclear: Core vs McpServer. If `DocAgent.Core` held it, future non-MCP consumers could use it. But `DocAgent.Core` has no current non-MCP consumers, and adding it there would be premature.
   - Recommendation: `DocAgent.McpServer/Review/ChangeReviewer.cs` as a static class. Can be moved to Core later if needed.

3. **What constitutes "before/after code snippet" for non-method symbols?**
   - What we know: `SymbolNode.DisplayName` is available. For methods, `SignatureChangeDetail` has old/new return types and `ParameterChange` list. For types, changes would be accessibility or constraint-level.
   - What's unclear: Should we reconstruct a C# signature string from parts, or use the raw display name + diff fields?
   - Recommendation: Reconstruct minimal signature strings from the typed fields. E.g., `{accessibility} {returnType} {displayName}({params})`. This is straightforward with the available data and avoids needing Roslyn at query time.

---

## Validation Architecture

> `workflow.nyquist_validation` is not set in `.planning/config.json` — this section is skipped.

---

## Sources

### Primary (HIGH confidence)
- `src/DocAgent.Core/DiffTypes.cs` — Complete type contracts for `SymbolDiff`, `SymbolChange`, all 6 detail records
- `src/DocAgent.Core/Abstractions.cs` — `IKnowledgeQueryService`, `SnapshotStore` interfaces
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — Snapshot loading pattern, `DiffAsync` implementation
- `src/DocAgent.McpServer/Tools/DocTools.cs` — Tool class pattern: `[McpServerToolType]`, `FormatResponse`, `ErrorResponse`, telemetry
- `src/DocAgent.McpServer/Tools/IngestionTools.cs` — Direct dependency injection pattern (not via `IKnowledgeQueryService`)
- `tests/DocAgent.Tests/McpToolTests.cs` — Tool unit test pattern: direct construction, stub dependencies
- `tests/DocAgent.Tests/SemanticDiff/DiffTestHelpers.cs` — Confirmed exists; provides `BuildSnapshot`/`BuildMethod` helpers reusable in Phase 11 tests
- `.planning/phases/09-semantic-diff-engine/09-VERIFICATION.md` — Confirmed all Phase 9 types and `SymbolGraphDiffer.Diff()` are production-quality (24/24 tests pass)
- `.planning/v1.1-MILESTONE-AUDIT.md` — Confirms `SymbolGraphDiffer.Diff()` has no production caller; Phase 11 is the planned consumer

### Secondary (MEDIUM confidence)
- `.planning/phases/11-change-intelligence-review/11-CONTEXT.md` — User decisions (authoritative for this phase)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all patterns established in phases 5, 8, 9
- Architecture: HIGH — codebase examined directly; `DiffTypes.cs` and `DocTools.cs` provide definitive patterns
- Pitfalls: HIGH — identified from direct code inspection (flat list grouping, edge direction, `ArgumentException` from `Diff()`)

**Research date:** 2026-02-28
**Valid until:** N/A — all sources are local codebase; no external API freshness concern
