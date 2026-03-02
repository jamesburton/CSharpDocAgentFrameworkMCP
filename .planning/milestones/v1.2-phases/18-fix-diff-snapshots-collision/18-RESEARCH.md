# Phase 18: Fix diff_snapshots Tool Name Collision - Research

**Researched:** 2026-03-02
**Domain:** C# MCP tool attribute rename, unit test string updates
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Rename SolutionTools tool from `diff_snapshots` to `diff_solution_snapshots`
- Follows existing naming pattern (verb_scope_noun)
- DocTools `diff_snapshots` name is unchanged
- Update SolutionTools `diff_solution_snapshots` description to clearly state it's solution-level: per-project diffs, cross-project edge changes
- Do NOT change DocTools `diff_snapshots` description — minimize blast radius

### Claude's Discretion
- Exact wording of the updated tool description
- Whether test assertion strings need updating for the new tool name
- Any documentation references to update

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TOOLS-04 | `diff_snapshots` works at solution level (diff two SolutionSnapshots) | The SolutionTools.DiffSnapshots method is already implemented; this phase fixes the tool name collision so it is discoverable under its own unique MCP name |
</phase_requirements>

## Summary

Phase 18 is a surgical one-file attribute rename. The collision is in `SolutionTools.cs` at line 164, where `[McpServerTool(Name = "diff_snapshots")]` duplicates the same tool name already registered by `DocTools.cs` at line 375. The MCP SDK discovers all `[McpServerToolType]` classes via `WithToolsFromAssembly()` in `Program.cs`, and duplicate tool names cause registration failures or last-writer-wins silent collisions at server startup.

The fix requires changing the `Name` string on the SolutionTools method attribute from `"diff_snapshots"` to `"diff_solution_snapshots"`, and updating its `[Description(...)]` to make the solution-level scope explicit. The C# method name `DiffSnapshots` can remain unchanged — the MCP tool name (the wire name) is entirely controlled by the `Name` property in `[McpServerTool]`. DocTools is untouched.

Test impact is limited: `SolutionToolTests.cs` calls `tools.DiffSnapshots(...)` directly on the C# method — this is unaffected by the attribute rename. No test calls the wire name `"diff_snapshots"` as a string assertion. Therefore no test source changes are needed beyond verifying a clean `dotnet test` run.

**Primary recommendation:** Change one attribute string in `SolutionTools.cs`. Update the Description. Verify with `dotnet test`. Done.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `ModelContextProtocol.Server` | project reference | Provides `[McpServerTool(Name = ...)]` attribute that sets the wire name | This is the only mechanism — no alternatives exist in this codebase |

No new packages required. This is a pure attribute rename within the existing codebase.

## Architecture Patterns

### McpServerTool Attribute Controls the Wire Name

The `[McpServerTool(Name = "...")]` attribute on a method in a `[McpServerToolType]` class sets the name exposed to MCP clients. The C# method name is irrelevant to the wire protocol. Changing `Name` in the attribute is all that is needed to rename the tool from the MCP perspective.

**Current state (collision):**

```csharp
// DocTools.cs line 375 — UNCHANGED (Phase 5, per-symbol diff)
[McpServerTool(Name = "diff_snapshots"), Description("Diff two snapshot versions showing added/removed/modified symbols.")]
public async Task<string> DiffSnapshots(string versionA, string versionB, ...)

// SolutionTools.cs line 164 — DUPLICATE NAME (Phase 16, solution-level diff)
[McpServerTool(Name = "diff_snapshots")]
[Description("Diff two solution snapshots showing per-project changes, projects added/removed, and cross-project edge changes.")]
public async Task<string> DiffSnapshots(string before, string after, ...)
```

**Fixed state:**

```csharp
// SolutionTools.cs — rename the Name property only
[McpServerTool(Name = "diff_solution_snapshots")]
[Description("Diff two solution snapshots showing per-project changes: symbols added/removed/modified per project, projects added or removed from the solution, and cross-project edge changes.")]
public async Task<string> DiffSnapshots(string before, string after, ...)
```

### Registration Discovery

`WithToolsFromAssembly()` (Program.cs line 70) scans the assembly for all classes decorated with `[McpServerToolType]` and registers their `[McpServerTool]`-decorated methods. Both `DocTools` and `SolutionTools` are discovered this way. After the rename, the two tools will have distinct wire names: `diff_snapshots` (DocTools) and `diff_solution_snapshots` (SolutionTools).

### Anti-Patterns to Avoid

- **Renaming the C# method:** Unnecessary. The wire name is controlled solely by the `Name` parameter in `[McpServerTool]`. Renaming the C# method would require updating all call sites in tests, increasing blast radius for zero benefit.
- **Changing DocTools:** Locked decision — DocTools `diff_snapshots` stays unchanged to minimize blast radius.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tool name deduplication | Custom reflection scan | Existing `[McpServerTool(Name = "...")]` attribute | The MCP SDK handles discovery and registration; just fix the Name string |

## Common Pitfalls

### Pitfall 1: Thinking test method calls use the wire name

**What goes wrong:** Developer searches for `"diff_snapshots"` in tests expecting to find broken string assertions after the rename.

**Why it happens:** Confusion between the MCP wire name (attribute string) and the C# method name.

**How to avoid:** `SolutionToolTests.cs` calls `tools.DiffSnapshots(...)` — a direct C# method invocation. The attribute rename has no effect on these tests. The method signature is unchanged.

**Verification:** `grep -r "diff_solution_snapshots\|diff_snapshots" tests/` shows only C# method call sites, no string-based tool name assertions.

### Pitfall 2: Forgetting to update the Description

**What goes wrong:** Tool is renamed but description still says "Diff two solution snapshots..." without clearly distinguishing it from DocTools' `diff_snapshots`.

**How to avoid:** Update Description at the same time as the Name attribute. The description is the primary signal for AI agents choosing between the two tools.

### Pitfall 3: Editing DocTools instead of SolutionTools

**What goes wrong:** Wrong file edited; DocTools wire name changed, breaking Phase 5 compliance.

**How to avoid:** Locked decision is explicit: only `SolutionTools.cs` changes. `DocTools.cs` is untouched.

## Code Examples

### Exact Edit Location

File: `src/DocAgent.McpServer/Tools/SolutionTools.cs`

Lines 164-165 (current):
```csharp
[McpServerTool(Name = "diff_snapshots")]
[Description("Diff two solution snapshots showing per-project changes, projects added/removed, and cross-project edge changes.")]
```

After fix:
```csharp
[McpServerTool(Name = "diff_solution_snapshots")]
[Description("Diff two solution snapshots at the solution level: per-project symbol changes (added/removed/modified), projects added or removed from the solution, and cross-project edge changes between snapshots.")]
```

### Test File — No Changes Required

`tests/DocAgent.Tests/SolutionToolTests.cs` — all 6 diff tests call the C# method directly:
```csharp
var json = await tools.DiffSnapshots(savedBefore.ContentHash!, savedAfter.ContentHash!);
```

The C# method name `DiffSnapshots` is unchanged, so all these tests continue to compile and pass without modification.

`tests/DocAgent.Tests/McpToolTests.cs` — also calls DocTools' `DiffSnapshots` method directly; no wire name assertions found.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit + FluentAssertions |
| Config file | none (discovered by test runner) |
| Quick run command | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TOOLS-04 | `diff_solution_snapshots` tool registered without collision; solution-level diffs work | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | Yes (SolutionToolTests.cs tests 8-13) |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~SolutionToolTests"`
- **Phase gate:** `dotnet test` — full suite green

### Wave 0 Gaps
None — existing test infrastructure covers all phase requirements. The 6 existing `DiffSnapshots_*` tests (Tests 8-13 in SolutionToolTests.cs) provide complete coverage of the renamed tool's behaviour. No new tests are needed unless the verifier requests a smoke test asserting the attribute value directly.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Duplicate `[McpServerTool(Name = "diff_snapshots")]` in both DocTools and SolutionTools | Unique names: `diff_snapshots` (DocTools) + `diff_solution_snapshots` (SolutionTools) | Phase 18 | MCP server starts clean; agents can unambiguously select the right tool |

## Open Questions

None. The fix is fully specified. The only discretion area is exact wording of the updated Description, which is addressed in the Code Examples section above.

## Sources

### Primary (HIGH confidence)
- Direct code inspection of `src/DocAgent.McpServer/Tools/SolutionTools.cs` — confirmed duplicate `[McpServerTool(Name = "diff_snapshots")]` at line 164
- Direct code inspection of `src/DocAgent.McpServer/Tools/DocTools.cs` — confirmed original `[McpServerTool(Name = "diff_snapshots")]` at line 375
- Direct code inspection of `tests/DocAgent.Tests/SolutionToolTests.cs` — confirmed all diff tests call C# method directly; no wire name string assertions
- Direct code inspection of `src/DocAgent.McpServer/Program.cs` — confirmed `WithToolsFromAssembly()` is the discovery mechanism

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — direct code inspection, no external dependencies
- Architecture: HIGH — single attribute property change, well-understood MCP SDK pattern
- Pitfalls: HIGH — confirmed by reading test files directly

**Research date:** 2026-03-02
**Valid until:** Until SolutionTools.cs or DocTools.cs are substantially modified (stable)
