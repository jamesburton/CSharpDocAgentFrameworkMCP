# Phase 12: ChangeTools Security Gate - Research

**Researched:** 2026-03-01
**Domain:** C# MCP server security — PathAllowlist enforcement pattern
**Confidence:** HIGH

## Summary

Phase 12 closes a security gap identified in the v1.1 milestone audit: `ChangeTools` receives a `PathAllowlist` via constructor injection but never calls `IsAllowed()` in any of its three tool methods (`ReviewChanges`, `FindBreakingChanges`, `ExplainChange`). All peer tool classes (`DocTools`, `IngestionTools`) enforce this gate. The fix is surgical — three call sites need a guard block inserted before `_snapshotStore.LoadAsync()`, plus new unit tests that verify denial behavior.

The gap is well-understood because: (a) the audit document pinpoints exact file, line pattern, and expected fix, (b) two working reference implementations exist in the same codebase (`IngestionTools` for hard-blocking, `DocTools` for conditional redaction), and (c) the existing test harness for `ChangeTools` already constructs a permissive allowlist (`AllowedPaths = ["**"]`) that can be swapped for a restrictive one in new tests.

**Primary recommendation:** Add `_allowlist.IsAllowed(_snapshotStore.ArtifactsDir)` check immediately before each `_snapshotStore.LoadAsync()` call, returning `ErrorResponse(QueryErrorKind.NotFound, "Access denied.")` on denial. This reuses the existing `ErrorResponse` helper (already in `ChangeTools`) and produces `"error": "not_found"` — matching the opaque denial pattern already in `DocTools` (which maps `NotFound` to `"Access denied"` in the opaque message path).

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| R-CHANGE-TOOLS | All 3 ChangeTools MCP methods enforce PathAllowlist before loading snapshots; denied access returns structured ErrorResponse; unit tests verify enforcement | Existing `_allowlist` field + `ErrorResponse` helper already present in `ChangeTools`; `IngestionTools` provides exact blocking pattern; existing `ChangeToolTests` test harness provides test infrastructure |
</phase_requirements>

## Standard Stack

### Core
| Library / Type | Location | Purpose | Why Standard |
|----------------|----------|---------|--------------|
| `PathAllowlist` | `src/DocAgent.McpServer/Security/PathAllowlist.cs` | Glob-based allow/deny path enforcement | Already injected into ChangeTools constructor; used by all peer tool classes |
| `SnapshotStore` | `src/DocAgent.Ingestion/SnapshotStore.cs` | Loads snapshots from `ArtifactsDir/{hash}.msgpack` | Already injected; `ArtifactsDir` property is the path to check |
| `QueryErrorKind` | `src/DocAgent.Core/QueryTypes.cs` | Typed error code enum for MCP error responses | Used by existing `ErrorResponse()` helper in ChangeTools |
| `ChangeTools.ErrorResponse()` | `src/DocAgent.McpServer/Tools/ChangeTools.cs` | Serializes structured error JSON | Already in ChangeTools — no new helpers needed |

### Supporting
| Library / Type | Location | Purpose | When to Use |
|----------------|----------|---------|-------------|
| `NullLogger<T>` | Microsoft.Extensions.Logging.Abstractions | No-op logger for tests | Already used in `ChangeToolTests.CreateTools()` |
| `Options.Create<T>` | Microsoft.Extensions.Options | Wraps options for unit test construction | Already used in existing tests |
| xUnit + FluentAssertions | `tests/DocAgent.Tests` | Test framework | All existing tests use this stack |

## Architecture Patterns

### Recommended Project Structure

No new files required. All changes are confined to:

```
src/DocAgent.McpServer/Tools/ChangeTools.cs        # add 3 guard blocks
tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs  # add denial tests
```

### Pattern 1: IngestionTools Hard-Block (primary reference)

`IngestionTools.IngestProject` checks the allowlist before any file I/O and returns immediately on denial:

```csharp
// src/DocAgent.McpServer/Tools/IngestionTools.cs lines ~64-70
var absolutePath = Path.GetFullPath(path);
if (!_allowlist.IsAllowed(absolutePath))
{
    _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
    activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
    return ErrorJson("access_denied", "Path is not in the configured allow list.");
}
```

**IngestionTools uses a separate `ErrorJson()` helper** that emits `"error": "access_denied"` — this is appropriate because IngestionTools takes a user-supplied path parameter.

**ChangeTools is different:** the user supplies a content hash (opaque identifier), not a file path. The file path is internal to `SnapshotStore`. Therefore the check should be against `_snapshotStore.ArtifactsDir` (the directory where snapshots live), and the error code should follow the `DocTools`/`QueryErrorKind.NotFound` pattern that emits `"Access denied"` opaquely — preventing path disclosure.

### Pattern 2: DocTools Opaque Denial (secondary reference for error code choice)

`DocTools.GetSymbol` uses `QueryErrorKind.NotFound` → `"not_found"` which maps to the opaque message `"Access denied"` when `VerboseErrors` is false:

```csharp
// DocTools.ErrorResponse() — already mirrored verbatim in ChangeTools
var opaque = kind == QueryErrorKind.NotFound ? "Access denied" : (message ?? "Request failed");
return JsonSerializer.Serialize(new {
    error = code,
    message = _options.VerboseErrors ? message : opaque,
    detail,
}, s_jsonOptions);
```

`ChangeTools.ErrorResponse()` is an **identical copy** of this method, so using `QueryErrorKind.NotFound` with a denial message string will automatically produce the right opaque behavior.

### Pattern 3: What Path to Check

`SnapshotStore.LoadAsync()` resolves the file path internally as:
```csharp
var filePath = Path.Combine(_artifactsDir, $"{contentHash}.msgpack");
```

The user-visible parameter (`versionA`, `versionB`) is a content hash, not a path. The meaningful path to gate is `_snapshotStore.ArtifactsDir` — the directory containing all snapshots. Checking this once per tool method (not once per `LoadAsync` call) is correct: if the artifacts directory is allowed, both `versionA` and `versionB` loads are allowed.

### Recommended Implementation for Each Tool Method

Insert this block at the top of `ReviewChanges`, `FindBreakingChanges`, and `ExplainChange`, immediately inside the `try` block, before the first `_snapshotStore.LoadAsync()`:

```csharp
// PathAllowlist gate — deny access to snapshot store if directory is not allowed
if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
{
    _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
    activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
    return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
}
```

This check is:
- **Fast** — no I/O, just glob matching
- **Opaque** — uses `NotFound` which maps to `"Access denied"` in non-verbose mode
- **Consistent** — same `ErrorResponse()` helper used for all other errors in `ChangeTools`
- **Once per tool call** — not once per `LoadAsync` (both loads use the same directory)

### Anti-Patterns to Avoid

- **Checking the content hash string with `IsAllowed()`**: The hash is not a file path — `IsAllowed()` glob-matches against a file path string; passing a hash would always fail or produce wrong results.
- **Checking per LoadAsync call**: Both `LoadAsync` calls in each tool use the same `_artifactsDir`; checking once at the method entry is sufficient and avpler to reason about.
- **Returning an exception**: The audit explicitly requires structured `ErrorResponse`, not thrown exceptions.
- **Adding a new QueryErrorKind**: `QueryErrorKind` does not have a `PathDenied` value. Do not extend the enum — `NotFound` is correct per the existing opaque denial pattern.
- **Using a new `ErrorJson()` helper**: `ChangeTools` already has `ErrorResponse(QueryErrorKind, string?)` — use it, not a separate helper.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| Path glob matching | Custom string prefix checking | `PathAllowlist.IsAllowed()` — already wired |
| Error JSON serialization | New serialization code | `ChangeTools.ErrorResponse(QueryErrorKind, string?)` — already present |
| Test allowlist construction | New test helper | `new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }))` — existing pattern in `ChangeToolTests.CreateTools()` |

## Common Pitfalls

### Pitfall 1: Checking the Wrong Path
**What goes wrong:** Passing `versionA` or `versionB` (a content hash string like `"abc123def..."`) to `_allowlist.IsAllowed()`. This is not a file path; `PathAllowlist` does glob matching on file system paths.
**Why it happens:** The tool parameters are named "version" but are used as filenames. It's natural to reach for the method parameter.
**How to avoid:** Check `_snapshotStore.ArtifactsDir`, which is the actual directory path on disk.
**Warning signs:** `IsAllowed()` always returning false or throwing in tests.

### Pitfall 2: Forgetting the Activity Status Tag
**What goes wrong:** Returning the error response without calling `activity?.SetStatus(ActivityStatusCode.Error, ...)`.
**Why it happens:** The `ErrorResponse()` helper does not set activity status — the call site must do it.
**How to avoid:** Mirror the `IngestionTools` pattern: set status before returning.

### Pitfall 3: Placing the Check Outside the Try Block
**What goes wrong:** Placing the allowlist check before `using var activity = ...` or after the existing `try {`, causing the check to bypass telemetry.
**Why it happens:** The guard block looks like it should precede everything.
**How to avoid:** Place it inside the `try` block, before the first `_snapshotStore.LoadAsync()` call. The `activity` variable is available throughout the `try` block.

### Pitfall 4: Test Uses Permissive Allowlist for Denial Test
**What goes wrong:** New "access denied" tests accidentally use `CreateTools()` which sets `AllowedPaths = ["**"]` (permissive) — test always passes regardless of enforcement.
**Why it happens:** `CreateTools()` was designed for happy-path tests.
**How to avoid:** Create a separate factory overload or inline construction with a restrictive allowlist (empty `AllowedPaths`, empty `DeniedPaths`, i.e., default — which allows only CWD). The `_tempDir` used by `SnapshotStore` is under `Path.GetTempPath()`, which is outside CWD by default, so the default `PathAllowlist` will deny it.

## Code Examples

### Allowlist Guard Block (to insert in each tool method)
```csharp
// Source: IngestionTools.cs pattern + DocTools opaque error pattern
// Place inside try{}, before first _snapshotStore.LoadAsync() call
if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
{
    _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
    activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
    return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
}
```

### Test: Denial Returns Structured Error
```csharp
[Fact]
public async Task ReviewChanges_PathDenied_ReturnsAccessDenied()
{
    var (hashA, hashB) = await SaveBreakingPairAsync();

    // Restrictive allowlist: no AllowedPaths → only CWD allowed → _tempDir denied
    var allowlist = new PathAllowlist(Options.Create(new DocAgentServerOptions()));
    var tools = CreateTools(allowlist: allowlist);  // inject restrictive allowlist

    var json = await tools.ReviewChanges(hashA, hashB);
    var root = Parse(json);

    root.TryGetProperty("error", out var err).Should().BeTrue();
    // "not_found" because QueryErrorKind.NotFound → opaque "Access denied"
    err.GetString().Should().Be("not_found");
}
```

Note: `CreateTools()` in `ChangeToolTests` currently takes an optional `SnapshotStore? store` parameter. The plan should add an optional `PathAllowlist? allowlist` parameter to `CreateTools()` to support injecting a restrictive allowlist for denial tests.

### Existing CreateTools Helper (for reference)
```csharp
// Current signature in ChangeToolTests.cs
private ChangeTools CreateTools(SnapshotStore? store = null)
{
    store ??= _store;
    var opts = new DocAgentServerOptions { VerboseErrors = true };
    var allowlist = new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }));
    var logger = NullLogger<ChangeTools>.Instance;
    return new ChangeTools(store, allowlist, logger, Options.Create(opts));
}
```

Extend to:
```csharp
private ChangeTools CreateTools(SnapshotStore? store = null, PathAllowlist? allowlist = null)
{
    store ??= _store;
    var opts = new DocAgentServerOptions { VerboseErrors = true };
    allowlist ??= new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }));
    var logger = NullLogger<ChangeTools>.Instance;
    return new ChangeTools(store, allowlist, logger, Options.Create(opts));
}
```

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit + FluentAssertions |
| Config file | none (discovered by convention) |
| Quick run command | `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| R-CHANGE-TOOLS | ReviewChanges denies when allowlist blocks ArtifactsDir | unit | `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` | Wave 0 — new test |
| R-CHANGE-TOOLS | FindBreakingChanges denies when allowlist blocks ArtifactsDir | unit | `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` | Wave 0 — new test |
| R-CHANGE-TOOLS | ExplainChange denies when allowlist blocks ArtifactsDir | unit | `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` | Wave 0 — new test |
| R-CHANGE-TOOLS | All existing happy-path tests still pass after guard added | unit | `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` | ✅ exists (10 tests) |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~ChangeToolTests"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before verify

### Wave 0 Gaps
- [ ] New test methods in `tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs` — covers R-CHANGE-TOOLS denial path (3 tests: one per tool method)
- [ ] `CreateTools()` overload with optional `PathAllowlist? allowlist` parameter

## Open Questions

1. **Error code: `not_found` vs `access_denied`**
   - What we know: `IngestionTools` uses `"access_denied"` (custom string, no `QueryErrorKind` value). `DocTools` uses `QueryErrorKind.NotFound` → `"not_found"` with opaque message. `ChangeTools.ErrorResponse()` does not have an `access_denied` branch.
   - What's unclear: Should `ChangeTools` match `IngestionTools` exactly (`access_denied`) or match the existing `ErrorResponse()` pattern (`not_found`)?
   - Recommendation: Use `QueryErrorKind.NotFound` → `"not_found"` with message `"Access denied."`. Rationale: (a) avoids adding a new string branch to `ErrorResponse()`, (b) is more opaque (doesn't confirm access control exists), (c) is consistent with the existing code structure. If the team prefers `access_denied`, add an `ErrorJson()` static helper matching `IngestionTools`.

2. **Placement of check: once per method vs once per LoadAsync**
   - What we know: `_snapshotStore.ArtifactsDir` is a fixed path; both loads in a method use the same directory.
   - Recommendation: Check once per tool method (at method entry). This is simpler, faster, and equally correct.

## Sources

### Primary (HIGH confidence)
- Direct code reading: `src/DocAgent.McpServer/Tools/ChangeTools.cs` — current state with gap
- Direct code reading: `src/DocAgent.McpServer/Tools/IngestionTools.cs` — blocking pattern reference
- Direct code reading: `src/DocAgent.McpServer/Tools/DocTools.cs` — opaque denial pattern reference
- Direct code reading: `src/DocAgent.McpServer/Security/PathAllowlist.cs` — `IsAllowed()` signature and behavior
- Direct code reading: `src/DocAgent.Ingestion/SnapshotStore.cs` — `ArtifactsDir` property and `LoadAsync` path construction
- Direct code reading: `tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs` — existing test harness
- Direct code reading: `tests/DocAgent.Tests/IngestionToolTests.cs` — denial test pattern
- Audit document: `.planning/v1.1-MILESTONE-AUDIT.md` — gap specification and recommended fix

## Metadata

**Confidence breakdown:**
- Gap identification: HIGH — audit is precise; code confirms `_allowlist` field unused in all 3 methods
- Fix pattern: HIGH — two reference implementations in same codebase (`IngestionTools`, `DocTools`)
- Error code choice: MEDIUM — minor ambiguity between `access_denied` and `not_found`; recommendation documented in Open Questions
- Test pattern: HIGH — `ChangeToolTests` and `IngestionToolTests` provide clear template

**Research date:** 2026-03-01
**Valid until:** This research is based entirely on codebase state — valid until `ChangeTools.cs` or `PathAllowlist.cs` are modified.
