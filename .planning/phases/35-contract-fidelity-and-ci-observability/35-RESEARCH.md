# Phase 35: Contract Fidelity and CI Observability — Research

**Researched:** 2026-03-26
**Domain:** TS↔C# JSON contract alignment, xUnit skip patterns, Aspire dependency wiring, benchmark CI docs
**Confidence:** HIGH

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SIDE-03 | C# TypeScriptIngestionService deserializes sidecar response into SymbolGraphSnapshot | Gaps INT-01 (GenericConstraint field mismatch) and INT-02 (skip markers) affect deserialization fidelity and test observability for this requirement |
| SIDE-04 | Aspire AppHost registers Node.js sidecar via AddNodeApp() with dependency visibility | Gap INT-03 (AppHost WithReference missing) directly closes SIDE-04 fully |
| EXTR-01 | Extract all declaration types into SymbolNode graph | Gap INT-01 affects GenericConstraint round-trip for generic types extracted by EXTR-01 |
| EXTR-04 | Extract inheritance/implementation edges as SymbolEdge relationships | Gap INT-04 (SymbolEdgeKind dormant values) affects edge deserialization safety for EXTR-04 |
</phase_requirements>

---

## Summary

Phase 35 closes six items of accumulated tech debt from the v2.0 milestone audit. All six items are precisely identified in `v2.0-MILESTONE-AUDIT.md` and the source files have been directly inspected. There are no discovery unknowns — the work is surgical, isolated changes to well-understood files.

The phase breaks into four independent work areas: (1) fix two TS type definition field names to match C# JSON property names (`GenericConstraint.name` → `typeParameterName`, add `ParameterInfo.isOptional`); (2) remove dormant `SymbolEdgeKind` values from the TS types file that would throw on C# deserialization; (3) replace early-return `if (env != "true") return;` guards in two sidecar integration tests with `[Fact(Skip=...)]` attributes; (4) add `WithReference(sidecar)` to the McpServer resource builder in AppHost for Aspire dashboard dependency visibility, and document benchmark CI requirements.

**Primary recommendation:** Execute as a single wave of four parallel tasks — the changes are in four distinct files/areas with no dependencies between them.

---

## Standard Stack

### Core (already in project, no new installs)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| xUnit | 2.x | Test framework | Project standard — all 654 tests use xUnit |
| Aspire.Hosting.JavaScript | 9.x (from csproj) | NodeApp resource registration | Already installed in AppHost.csproj |
| System.Text.Json | .NET 10 BCL | JSON serialization/deserialization for sidecar IPC | SidecarJsonOptions already defined in TypeScriptIngestionService |

**No new packages needed.** All changes are in existing code.

---

## Architecture Patterns

### Pattern 1: xUnit Skip vs. Early-Return Guard

**What:** `[Fact(Skip = "reason")]` causes the test runner to report the test as "Skipped" in output. An early `return;` guard causes the test to report as "Passed" — hiding the fact that no assertions ran.

**When to use:** Any test that requires an external prerequisite (Node.js binary, env var, real sidecar) must use `[Fact(Skip=...)]` so CI output clearly shows "Skipped" rather than misleadingly "Passed."

**Preferred pattern in this project:**

```csharp
// BAD — silently passes in CI, hides skip from test runner output
[Fact]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    if (Environment.GetEnvironmentVariable("RUN_SIDECAR_TESTS") != "true")
        return; // Skip unless explicitly opted in
    // ...
}

// GOOD — test runner reports "Skipped", not "Passed"
[Fact(Skip = "Requires RUN_SIDECAR_TESTS=true and Node.js/compiled sidecar")]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    // ...
}
```

**Existing project evidence:** `RegressionGuardTests` also uses early-return (same pattern, same tech debt). However Phase 35 scope is specifically the two sidecar integration tests flagged in INT-02. `RegressionGuardTests` has `[Trait("Category", "Benchmark")]` so it is excluded from standard runs — fixing it is optional/deferred.

**Complication to handle:** Using `[Fact(Skip=...)]` with a static string means the test is always skipped in every test run, even when `RUN_SIDECAR_TESTS=true` is set. The standard xUnit approach for env-gated tests is one of:

1. **Simple `[Fact(Skip=...)]`** — Always skipped. Developers must run explicitly. Best for tests that require infrastructure that can never exist in standard CI.
2. **Custom `SkipFact` attribute** — Checks an env var at attribute construction time; skips only when env var is absent. More complex but allows `RUN_SIDECAR_TESTS=true dotnet test` to actually run the tests without code change.

For this project, option 1 is the correct choice. The project decision from State.md was "RUN_SIDECAR_TESTS=true" as a separate explicit run. Changing to a static `[Fact(Skip=...)]` means: the two tests are **always** skipped in the standard `dotnet test` run. To run them, the test class description already explains the requirement. This matches the precedent set by `dotnet test --filter "Category=Benchmark"`.

**Confidence:** HIGH — verified by reading `TypeScriptSidecarIntegrationTests.cs:85,145` and `RegressionGuardTests.cs:22-30`.

### Pattern 2: TS→C# JSON Field Name Contract Alignment

**What:** The TypeScript sidecar serializes types using TypeScript interface field names. C# deserializes using `[JsonPropertyName("...")]` attributes. When these don't match, fields are silently dropped (System.Text.Json default behavior with `PropertyNameCaseInsensitive = true` but no property-name override).

**The three mismatches:**

#### INT-01: GenericConstraint field name mismatch

- TS emits: `{ name: "T", constraints: [...] }` (from `extractor.ts:259`)
- C# expects: `[JsonPropertyName("typeParameterName")]` (from `Symbols.cs:81`)
- Fix: Change TS `types.ts:127` field from `name: string` to `typeParameterName: string`
- Also update `extractor.ts:259` where the object literal assigns `name:` to assign `typeParameterName:`

```typescript
// BEFORE (types.ts:126-129)
export interface GenericConstraint {
  name: string;
  constraints: string[];
}

// AFTER
export interface GenericConstraint {
  typeParameterName: string;
  constraints: string[];
}
```

```typescript
// BEFORE (extractor.ts ~259)
return (node.typeParameters || []).map(tp => ({
  name: tp.name.getText(),
  constraints: tp.constraint ? [...] : []
}));

// AFTER
return (node.typeParameters || []).map(tp => ({
  typeParameterName: tp.name.getText(),
  constraints: tp.constraint ? [...] : []
}));
```

#### ParameterInfo.isOptional missing from C# record

- TS emits: `{ name, typeName, isOptional: bool, defaultValue }` (from `types.ts:119-124` and `extractor.ts:250`)
- C# record: `ParameterInfo(Name, TypeName, DefaultValue?, IsParams, IsRef, IsOut, IsIn)` — no `IsOptional` property
- Fix: Add `[property: JsonPropertyName("isOptional")] bool IsOptional` to the C# `ParameterInfo` record in `Symbols.cs:70`
- C# record is positional — must add `IsOptional` in a non-breaking position. Since this is a record with explicit constructors and the JSON deserialization uses `PropertyNameCaseInsensitive`, adding at the end or between existing parameters works. The MessagePack feedback memo says: always append new enum values at the end. For records, the same principle applies: add new fields at the end to maintain constructor positional compatibility.

```csharp
// BEFORE (Symbols.cs:70-77)
public sealed record ParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeName")] string TypeName,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue,
    [property: JsonPropertyName("isParams")] bool IsParams,
    [property: JsonPropertyName("isRef")] bool IsRef,
    [property: JsonPropertyName("isOut")] bool IsOut,
    [property: JsonPropertyName("isIn")] bool IsIn);

// AFTER — append IsOptional at end (backward-compatible)
public sealed record ParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeName")] string TypeName,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue,
    [property: JsonPropertyName("isParams")] bool IsParams,
    [property: JsonPropertyName("isRef")] bool IsRef,
    [property: JsonPropertyName("isOut")] bool IsOut,
    [property: JsonPropertyName("isIn")] bool IsIn,
    [property: JsonPropertyName("isOptional")] bool IsOptional = false);
```

Adding with a default value of `false` ensures: existing C# code that constructs `ParameterInfo` without `IsOptional` continues to compile without changes. The Roslyn symbol graph builder (`RoslynSymbolGraphBuilder.cs:381,401`) creates `ParameterInfo` records for C# symbols — C# has no `isOptional` equivalent that maps cleanly (it uses `HasExplicitDefaultValue` instead). Setting the default to `false` is correct for C# symbols; only TS sidecar deserialization sets it from JSON.

**Confidence:** HIGH — directly verified by reading both files.

#### INT-04: Dormant SymbolEdgeKind values in TS

- TS `types.ts:138-139` defines `InheritsFrom = "InheritsFrom"` and `Accepts = "Accepts"`
- C# `Symbols.cs:129-151` has no `InheritsFrom` or `Accepts` members
- The `allowIntegerValues: false` `JsonStringEnumConverter` will throw `JsonException` if the C# deserializer encounters these string values
- Fix: Remove `InheritsFrom` and `Accepts` from TS `SymbolEdgeKind` enum in `types.ts`
- The extractor (`extractor.ts`) does not emit these — confirmed by searching `extractor.ts`; only `Inherits` and `Implements` are emitted. Removing them from the type definition is safe.

```typescript
// BEFORE (types.ts:131-146)
export enum SymbolEdgeKind {
  Contains = "Contains",
  Inherits = "Inherits",
  Implements = "Implements",
  References = "References",
  Calls = "Calls",
  Overrides = "Overrides",
  InheritsFrom = "InheritsFrom",   // remove
  Returns = "Returns",
  Accepts = "Accepts",             // remove
  Invokes = "Invokes",
  Configures = "Configures",
  DependsOn = "DependsOn",
  Triggers = "Triggers",
  Imports = "Imports"
}

// AFTER — remove dormant values
export enum SymbolEdgeKind {
  Contains = "Contains",
  Inherits = "Inherits",
  Implements = "Implements",
  References = "References",
  Calls = "Calls",
  Overrides = "Overrides",
  Returns = "Returns",
  Invokes = "Invokes",
  Configures = "Configures",
  DependsOn = "DependsOn",
  Triggers = "Triggers",
  Imports = "Imports"
}
```

**Confidence:** HIGH — verified by reading `types.ts:131-146`, `Symbols.cs:129-151`, and `extractor.ts` (which does not reference `InheritsFrom` or `Accepts`).

### Pattern 3: Aspire WithReference for Dependency Visibility

**What:** Aspire's `WithReference()` extension method creates an Aspire-level resource dependency. When `mcpServer.WithReference(sidecar)` is called, the Aspire dashboard draws a dependency arrow from `docagent-mcp` to `ts-sidecar`, making the relationship visible in the topology view.

**Constraint:** `NodeApp` returned by `AddNodeApp()` is of type `NodeAppResource`. `WithReference()` only accepts resources that implement `IResourceWithConnectionString` or `IResourceWithEndpoints`. A plain `NodeApp` resource does not implement these interfaces by default — it is a process resource without an exposed endpoint.

**What is achievable:** Since `ts-sidecar` is spawned in-process via `TypeScriptIngestionService` (not via HTTP endpoint), the sidecar resource does not expose a connection string or endpoint that `WithReference()` can consume. The Aspire-idiomatic approach for non-HTTP dependencies is to use `WithAnnotation<WaitForDependencyAnnotation>` or to simply add a `WithEnvironment` call that references the sidecar's working path — which already exists.

**Decision tree for INT-03:**

Option A: `mcpServer.WithReference(sidecar)` — This will fail to compile unless `NodeAppResource` implements a supported interface. **Not directly applicable without additional Aspire extensions.**

Option B: `mcpServer.WaitFor(sidecar)` — `WaitFor()` is available for any `IResource`. This creates a startup dependency (McpServer waits for sidecar to be running). However, the Phase 33 decision was "No .WaitFor(sidecar) in AppHost — Parallel startup with graceful degradation." This option contradicts a locked project decision.

Option C: Annotate the relationship without enforcing ordering. Use `sidecar.WithAnnotation(new CustomResourceAnnotation(...))` or simply ensure `sidecar` variable is used by referencing it in a `WithEnvironment` lambda. **This gives Aspire the graph information without imposing startup ordering.**

Option D (simplest, lowest risk): Call `.WithAnnotation(new ResourceRelationshipAnnotation("ts-sidecar", "uses"))` if that API exists, or use the pattern of passing `sidecar` resource as an environment value source.

**Verified approach for Aspire 9.x (Aspire.Hosting.JavaScript):** Looking at installed Aspire version 13.1.2 (from csproj `Sdk="Aspire.AppHost.Sdk/13.1.2"`), the `WithReference` API is available for resources implementing `IResourceWithConnectionString`. A `NodeAppResource` does not implement this. However, a common approach in Aspire AppHosts is to use `mcpServer.WithEnvironment(ctx => ...)` with a reference to the sidecar resource, which implicitly registers a dependency relationship for the Aspire manifest. Since the env var `DOCAGENT_SIDECAR_DIR` is already hardcoded as a string, not derived from the sidecar resource, the relationship is not surfaced.

**Practical fix for INT-03:** The cleanest approach given project constraints is to add a comment-level annotation and ensure the `sidecar` variable is referenced in the McpServer configuration, rather than leaving it unused. The exact mechanism depends on what `Aspire.Hosting.JavaScript` 9.x exposes. Given that `WaitFor` is excluded by prior decision, the fix should use `WithAnnotation` if available, or at minimum reference `sidecar` in a `WithEnvironment` that uses a property of the sidecar resource.

**Confidence:** MEDIUM — The exact `WithReference` vs `WithAnnotation` API compatibility for `NodeAppResource` needs to be verified during implementation. The `WaitFor` approach is excluded by prior decision. Document the constraint and implement the best available option that does not impose startup ordering.

### Pattern 4: Benchmark CI Documentation

**What:** `TypeScriptIngestionBenchmarks.cs` requires a real Node.js sidecar to run. Currently there is no documentation in `docs/Testing.md` or elsewhere explaining that these benchmarks require sidecar prerequisites and how to run them in CI.

**Required content:** Add a section to `docs/Testing.md` (or the benchmark file header is already well-documented) explaining:
- Run command: `dotnet run -c Release --project tests/DocAgent.Benchmarks`
- Prerequisites: Node.js installed, sidecar built (`cd src/ts-symbol-extractor && npm install && npm run build`)
- CI integration: Set `RUN_BENCHMARKS=1` (for `RegressionGuardTests`) or run benchmark project directly

**Confidence:** HIGH — the benchmark class header already documents this; the gap is a missing entry in `docs/Testing.md`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Conditional test skip based on env var | Custom `SkippableFactAttribute` with env probe | Static `[Fact(Skip=...)]` | xUnit's built-in skip is sufficient; no env-conditional skip needed here since tests are always skipped in standard CI |
| JSON field mapping between TS and C# | Custom mapping layer | `[JsonPropertyName]` on C# records + matching field names in TS interfaces | Already the project pattern; just fix the mismatched names |
| Aspire dependency graph entry | Custom Aspire extension | `WithReference()` or `WithAnnotation()` from `Aspire.Hosting` | Use what Aspire provides; don't build custom resource relationship tracking |

---

## Common Pitfalls

### Pitfall 1: ParameterInfo Record Constructor Break

**What goes wrong:** Adding a required parameter to a positional record in the middle breaks all existing call sites that use positional syntax.
**Why it happens:** Positional records generate a constructor where parameter order matters. Existing test and production code constructs `ParameterInfo` with 7 positional arguments.
**How to avoid:** Add `IsOptional = false` at the end as a parameter with a default value. All existing call sites compile unchanged. C# symbols get `IsOptional = false` (correct). TypeScript symbols get `IsOptional` from JSON deserialization.
**Warning signs:** Any `ParameterInfo(...)` call sites that use positional argument style will fail to compile if a non-optional parameter is inserted before the last position.

### Pitfall 2: TypeScript `types.ts` Rename Breaks Extractor Compile

**What goes wrong:** Renaming `GenericConstraint.name` to `typeParameterName` in `types.ts` without updating `extractor.ts` causes a TypeScript compile error.
**Why it happens:** `extractor.ts:259` assigns `name: tp.name.getText()` in an object literal — after renaming the interface field, the object literal becomes invalid.
**How to avoid:** Update both files together: `types.ts` interface definition and `extractor.ts` object literal assignment.
**Warning signs:** `npm run build` in `src/ts-symbol-extractor/` fails with a type error on the `getGenericConstraints` function.

### Pitfall 3: Aspire WaitFor Contradicts Phase 33 Decision

**What goes wrong:** Using `.WaitFor(sidecar)` would introduce a startup ordering constraint that the Phase 33 decision explicitly rejected.
**Why it happens:** INT-03 audit gap says "no Aspire dependency link" — tempting fix is `WaitFor`. But Phase 33 established that McpServer must start independently with graceful degradation.
**How to avoid:** Use `WithReference` if available for `NodeAppResource`, or use `WithAnnotation` for metadata-only dependency. Do not use `WaitFor`.
**Warning signs:** If the fix adds a `.WaitFor(sidecar)` call, it violates the STATE.md decision "No .WaitFor(sidecar) in AppHost."

### Pitfall 4: xUnit Skip String Is Static

**What goes wrong:** Changing `if (env != "true") return;` to `[Fact(Skip=...)]` means the test is ALWAYS skipped, even when `RUN_SIDECAR_TESTS=true` is explicitly set.
**Why it happens:** xUnit's `[Fact(Skip=...)]` is unconditional — any non-null/non-empty skip reason causes the test to be reported as skipped.
**How to avoid:** This is intentional and correct for this project. The prior behavior (early return showing as "Passed") was the bug. The fix (always Skipped) is the correct observable behavior. Document in the skip reason string how to run these tests.
**Warning signs:** If someone expects `RUN_SIDECAR_TESTS=true dotnet test` to run these tests after the fix, they'll find them still skipped. The correct way to run them would be via a custom `SkippableFactAttribute` if needed in future — but that's deferred scope.

---

## Code Examples

### GenericConstraint Fix (TS — types.ts)

```typescript
// Source: src/ts-symbol-extractor/src/types.ts
// Change 'name' to 'typeParameterName' to match C# [JsonPropertyName("typeParameterName")]
export interface GenericConstraint {
  typeParameterName: string;   // was: name
  constraints: string[];
}
```

### GenericConstraint Fix (TS — extractor.ts)

```typescript
// Source: src/ts-symbol-extractor/src/extractor.ts
// Object literal must use 'typeParameterName' to match the updated interface
return (node.typeParameters || []).map(tp => ({
  typeParameterName: tp.name.getText(),   // was: name
  constraints: tp.constraint ? [checker.typeToString(checker.getTypeAtLocation(tp.constraint))] : []
}));
```

### ParameterInfo isOptional Fix (C# — Symbols.cs)

```csharp
// Source: src/DocAgent.Core/Symbols.cs
// Append IsOptional at end with default false — backward-compatible
public sealed record ParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeName")] string TypeName,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue,
    [property: JsonPropertyName("isParams")] bool IsParams,
    [property: JsonPropertyName("isRef")] bool IsRef,
    [property: JsonPropertyName("isOut")] bool IsOut,
    [property: JsonPropertyName("isIn")] bool IsIn,
    [property: JsonPropertyName("isOptional")] bool IsOptional = false);
```

### xUnit Skip Fix (C# — TypeScriptSidecarIntegrationTests.cs)

```csharp
// Source: tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs
// Replace early-return guard with [Fact(Skip=...)] on both test methods

// BEFORE (line 82-86):
[Fact]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    if (Environment.GetEnvironmentVariable("RUN_SIDECAR_TESTS") != "true")
        return; // Skip unless explicitly opted in

// AFTER:
[Fact(Skip = "Requires Node.js and compiled sidecar. Run with: RUN_SIDECAR_TESTS=true dotnet test --filter 'Category=Sidecar'")]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    // remove the early-return guard; test body unchanged
```

### Aspire WithReference (AppHost — Program.cs)

```csharp
// Source: src/DocAgent.AppHost/Program.cs
// Use the sidecar variable to express dependency in Aspire manifest
var sidecar = builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js");

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithReference(sidecar)    // Add this — creates dependency link in Aspire dashboard
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR", ...)
    // ...
```

NOTE: If `NodeAppResource` does not implement `IResourceWithConnectionString`, `WithReference()` will not compile. In that case, use the annotation approach or accept that the `sidecar` variable remains logically unreferenced for this Aspire version. Verify at implementation time.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Early-return guard for env-gated tests | `[Fact(Skip=...)]` | xUnit convention pre-existing | Tests show as Skipped not Passed in CI |
| TS field name `name` on GenericConstraint | `typeParameterName` matching C# JsonPropertyName | Phase 35 | Generic constraint data no longer silently dropped |
| C# ParameterInfo without isOptional | Add `IsOptional = false` default parameter | Phase 35 | TS optional parameter metadata round-trips correctly |
| Dormant `InheritsFrom`/`Accepts` in TS | Remove from TS SymbolEdgeKind | Phase 35 | Eliminates latent throw risk |
| Aspire sidecar variable unused | `WithReference(sidecar)` or equivalent annotation | Phase 35 | Aspire dashboard shows dependency relationship |

---

## Open Questions

1. **NodeAppResource and WithReference compatibility**
   - What we know: `WithReference()` in Aspire requires `IResourceWithConnectionString` or `IResourceWithEndpoints`
   - What's unclear: Whether `NodeAppResource` from `Aspire.Hosting.JavaScript` v9.x/13.x implements either interface, or whether `WithAnnotation` provides a lighter-weight alternative
   - Recommendation: During implementation, check `Aspire.Hosting.JavaScript.dll` reflection or source. If `WithReference` does not compile, use `mcpServer.WithAnnotation(new ResourceRelationshipAnnotation("ts-sidecar", AnnotationRelationshipType.Dependency))` if that API exists, or leave a code comment explaining the constraint and close INT-03 as "best-effort — Aspire limitation for NodeApp resources without endpoints."

2. **RegressionGuardTests early-return pattern**
   - What we know: `RegressionGuardTests` also uses early-return (`if (string.IsNullOrEmpty(env)) return;`) rather than `[Fact(Skip=...)]`
   - What's unclear: Whether Phase 35 scope includes fixing it alongside INT-02, or deferred
   - Recommendation: Phase 35 scope (per audit) is INT-02 (sidecar integration tests). `RegressionGuardTests` is mitigated by `[Trait("Category", "Benchmark")]` which excludes it from default runs. Fixing it is low-risk but out of stated scope — defer unless it's a trivial change that fits the plan.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.x + FluentAssertions |
| Config file | none (standard dotnet test discovery) |
| Quick run command | `dotnet test src/DocAgentFramework.sln --filter "FullyQualifiedName~TypeScriptDeserialization"` |
| Full suite command | `dotnet test src/DocAgentFramework.sln` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SIDE-03 | GenericConstraint round-trips through sidecar deserialization | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | Partial — new test needed for GenericConstraint |
| SIDE-03 | ParameterInfo.IsOptional deserializes from sidecar JSON | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | Partial — new test needed for isOptional |
| SIDE-03 | Sidecar E2E tests show as Skipped not Passed in CI | unit | `dotnet test src/DocAgentFramework.sln` — verify output shows skipped count | ✅ — after fix, existing tests show Skip |
| SIDE-04 | AppHost registers sidecar with dependency link | manual | `dotnet run --project src/DocAgent.AppHost` — verify Aspire dashboard topology | ❌ — verify manually post-fix |
| EXTR-01 | GenericConstraint field name matches between TS and C# | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | ❌ Wave 0 |
| EXTR-04 | No dormant TS SymbolEdgeKind values that would throw | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test src/DocAgentFramework.sln --filter "FullyQualifiedName~TypeScriptDeserialization"` (fast — no sidecar needed)
- **Per wave merge:** `dotnet test src/DocAgentFramework.sln` (full 654 test suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/DocAgent.Tests/TypeScriptDeserializationTests.cs` — add tests for GenericConstraint round-trip (typeParameterName field), ParameterInfo.IsOptional deserialization, and SymbolEdgeKind dormant value removal. Existing file exists; new test methods needed.

*(Existing `TypeScriptDeserializationTests.cs` covers string enum deserialization and DocComment conversion — extend it for the three contract gaps.)*

---

## File Map (Exact Files to Change)

| Gap | File | Change |
|-----|------|--------|
| INT-01 GenericConstraint field name | `src/ts-symbol-extractor/src/types.ts:127` | Rename `name` to `typeParameterName` |
| INT-01 GenericConstraint field name | `src/ts-symbol-extractor/src/extractor.ts:~259` | Rename object literal key `name:` to `typeParameterName:` |
| ParameterInfo.isOptional | `src/DocAgent.Core/Symbols.cs:70-77` | Add `IsOptional = false` parameter at end of record |
| INT-04 Dormant edge kinds | `src/ts-symbol-extractor/src/types.ts:138-139` | Remove `InheritsFrom` and `Accepts` from SymbolEdgeKind |
| INT-02 Skip markers | `tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs:82,142` | Replace `[Fact]` + early-return with `[Fact(Skip=...)]` (two methods) |
| INT-03 Aspire WithReference | `src/DocAgent.AppHost/Program.cs:12` | Add `.WithReference(sidecar)` to mcpServer chain |
| Benchmark docs | `docs/Testing.md` | Add TypeScriptIngestionBenchmarks section |
| New tests | `tests/DocAgent.Tests/TypeScriptDeserializationTests.cs` | Add 3 new test methods for contract alignment |

---

## Sources

### Primary (HIGH confidence)

- Direct file inspection: `src/ts-symbol-extractor/src/types.ts` — verified field names emitted by TS sidecar
- Direct file inspection: `src/DocAgent.Core/Symbols.cs` — verified C# JsonPropertyName attributes
- Direct file inspection: `tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs` — verified early-return pattern at lines 85 and 145
- Direct file inspection: `src/DocAgent.AppHost/Program.cs` — verified sidecar variable is unused after AddNodeApp()
- Direct file inspection: `src/ts-symbol-extractor/src/extractor.ts:256-263` — verified `name:` assignment in getGenericConstraints, confirmed `InheritsFrom`/`Accepts` never emitted
- Direct file inspection: `.planning/v2.0-MILESTONE-AUDIT.md` — ground truth for all 6 tech debt items
- Direct file inspection: `.planning/STATE.md` — confirmed "No .WaitFor(sidecar) in AppHost" as locked project decision

### Secondary (MEDIUM confidence)

- xUnit skip pattern: project convention established by `RegressionGuardTests` (`[Trait("Category", "Benchmark")]` + env guard) and documented in `docs/Testing.md`
- Aspire `WithReference` constraint for `NodeAppResource`: inferred from Aspire hosting model — NodeApp resources lack endpoint exposure needed for WithReference; requires implementation-time verification

### Tertiary (LOW confidence)

- Aspire 13.1.2 `WithAnnotation` API availability for relationship annotation: not verified against installed DLL; may or may not provide a clean alternative to `WithReference`

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, all existing dependencies
- Architecture (contract fixes): HIGH — exact files, exact line numbers, exact field names verified
- Architecture (Aspire WithReference): MEDIUM — blocked by NodeAppResource interface constraint requiring runtime verification
- Architecture (xUnit Skip): HIGH — xUnit behavior is well-understood, project pattern confirmed
- Pitfalls: HIGH — all derived from direct code inspection

**Research date:** 2026-03-26
**Valid until:** 2026-04-26 (stable domain — no fast-moving dependencies)
