# Stack Research

**Domain:** .NET semantic diff engine, incremental Roslyn ingestion, change review MCP tooling (v1.1 additions)
**Researched:** 2026-02-28
**Confidence:** HIGH (core additions verified via NuGet and official docs; no new external dependencies required)

---

## Context: What Already Exists (Do Not Re-Research)

The v1.0 stack is fully validated and operational:

| Package | Version Pinned | Status |
|---------|---------------|--------|
| `Microsoft.CodeAnalysis.CSharp` | 4.12.0 | In use — upgrade candidate below |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 4.12.0 | In use |
| `ModelContextProtocol` | 1.0.0 | Stable |
| `Lucene.Net` + `Lucene.Net.Analysis.Common` | 4.8.0-beta00017 | In use |
| `MessagePack` | 3.1.4 | In use |
| `System.IO.Hashing` | 9.0.0 | In use |
| `OpenTelemetry.*` | 1.15.0 | In use |
| `xunit` | 2.9.3 | In use |
| `FluentAssertions` | 6.12.1 | In use |
| `Verify.Xunit` | 31.12.5 | In use |

**This file covers only what v1.1 adds or changes.**

---

## Recommended Stack Additions for v1.1

### Semantic Diff Engine

**Verdict: No new packages. Implement entirely using Roslyn APIs already present.**

The existing `Microsoft.CodeAnalysis.CSharp` 4.12.0 (or upgraded to 5.0.0 — see below) provides all primitives needed to build a symbol-level semantic diff:

| Roslyn API | What It Enables |
|------------|----------------|
| `ISymbol.ToDisplayString(SymbolDisplayFormat)` | Canonical string representation of signatures for equality comparison |
| `IMethodSymbol.Parameters`, `.TypeParameters`, `.ReturnType` | Parameter list, constraints, return type diffing |
| `INamedTypeSymbol.Interfaces`, `.BaseType` | Inheritance and interface implementation changes |
| `ISymbol.DeclaredAccessibility` | Accessibility (public → internal) change detection |
| `ISymbol.IsAbstract`, `.IsSealed`, `.IsOverride`, `.IsVirtual` | Modifier change detection |
| `NullableAnnotation` on `IParameterSymbol`, `IPropertySymbol`, `IFieldSymbol` | Nullability annotation change detection |
| `IMethodSymbol.IsGenericMethod`, `.TypeParameters[i].ConstraintTypes` | Generic constraint change detection |
| `ISymbol.GetDocumentationCommentId()` | Stable cross-snapshot symbol identity key |

**Pattern:** Diff two `SymbolGraphSnapshot`s by keying nodes on `DocumentationCommentId`, comparing `ISymbol`-derived fields. No graph diffing library needed — the `SymbolNode` model already carries all comparable fields. Implement as a pure in-memory comparator in `DocAgent.Core` or `DocAgent.Ingestion`.

**Confidence: HIGH** — This is the standard approach used by tooling such as `git-semantic-diff` (which explicitly uses Roslyn APIs for semantic analysis) and `ApiCompat`.

---

### Incremental Ingestion (File-Change Detection + Partial Re-Walk)

**Verdict: No new packages. Use `System.IO.Hashing` (already present) + `System.IO.FileSystemWatcher` (inbox BCL).**

| Mechanism | Package | Approach |
|-----------|---------|---------|
| Content-hash change detection | `System.IO.Hashing` 9.0.0 (already pinned) | `XxHash64.HashToUInt64(fileBytes)` per `.cs` file; store hash in a `FileHashManifest` alongside the snapshot. On next ingest, compare hashes to identify changed/added/removed files only. |
| File-system event watching (optional) | `System.IO.FileSystemWatcher` (BCL inbox) | Wire `Changed`/`Created`/`Deleted` events to trigger incremental re-ingest of affected files. Debounce with `System.Threading.Channels.Channel<T>` (inbox) to coalesce rapid edits. |
| Partial Roslyn re-walk | `Microsoft.CodeAnalysis.CSharp` (already pinned) | Replace only the changed `SyntaxTree`s in the existing `CSharpCompilation` via `compilation.ReplaceSyntaxTree(oldTree, newTree)`. This preserves the existing semantic model for unchanged files — Roslyn internally caches unchanged trees. |

**Key pattern for incremental Roslyn re-walk:**
```csharp
// Fast path: only parse changed files
var updatedCompilation = existingCompilation;
foreach (var changedFile in changedFilePaths)
{
    var newSource = await File.ReadAllTextAsync(changedFile);
    var newTree = CSharpSyntaxTree.ParseText(newSource, path: changedFile);
    var oldTree = existingCompilation.SyntaxTrees
        .FirstOrDefault(t => t.FilePath == changedFile);
    updatedCompilation = oldTree is null
        ? updatedCompilation.AddSyntaxTrees(newTree)
        : updatedCompilation.ReplaceSyntaxTree(oldTree, newTree);
}
// Re-walk only changed symbols
```

`CSharpCompilation.ReplaceSyntaxTree()` is documented Roslyn API — it preserves all unchanged semantic models. This is the same mechanism used by Roslyn's incremental source generators internally.

**Confidence: HIGH** — `ReplaceSyntaxTree` is a stable, documented public API. The hash-based manifest pattern is standard for incremental build systems (used by MSBuild's input/output tracking).

---

### Change Review Skill (`review_changes` MCP Tool + `find_breaking_changes` / `explain_change`)

**Verdict: No new packages. New MCP tools are pure logic built on the diff engine output.**

| Component | Implementation |
|-----------|---------------|
| `review_changes` tool | Calls diff engine on two named snapshots; serializes `SymbolDiffResult[]` to MCP response using existing `System.Text.Json` (inbox) |
| `find_breaking_changes` tool | Filters diff results by `DiffClassification.Breaking` enum; a breaking change is any signature removal, accessibility narrowing, parameter type change, non-nullable→nullable annotation change, or constraint loosening |
| `explain_change` tool | Given a `DocumentationCommentId`, returns the before/after `SymbolNode` diff with human-readable field labels — formatted as structured text, no LLM call needed |
| Snapshot selection | Load two snapshots by version label from existing `SnapshotStore` (artifacts directory + manifest.json — already implemented) |

No new MCP SDK features are required. Tool registration follows the existing `[McpServerTool]` attribute pattern. `ModelContextProtocol` 1.0.0 is already stable and sufficient.

---

### Optional Upgrade: Roslyn 5.0.0

**Recommendation: Upgrade `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Workspaces` from 4.12.0 to 5.0.0.**

| Reason | Detail |
|--------|--------|
| C# 14 language feature APIs | 5.0.0 ships with VS 2026 / .NET 10 final. The project targets `LangVersion=preview` — Roslyn must be >= the language version to parse C# 14 constructs in analyzed projects without degraded semantic models. |
| `NullableAnnotation` API improvements | 5.0.0 adds more complete nullability flow in `ITypeSymbol.NullableAnnotation` — directly relevant to the nullability diffing requirement. |
| No breaking changes in public API | Roslyn maintains strict API compatibility across minor versions. Upgrading 4.12.0 → 5.0.0 requires no application code changes. |

**Version on NuGet as of 2026-02-28:** `Microsoft.CodeAnalysis.CSharp` **5.0.0** (stable, not preview).

**Confidence: MEDIUM** — Version confirmed on NuGet. No specific C# 14 nullability API changes verified against changelog; assertion is based on Roslyn's track record of backward compatibility.

---

### No New Testing Packages Needed

The existing `Verify.Xunit` 31.12.5 (already pinned) is the right tool for golden-file testing of diff engine output — snapshot the `SymbolDiffResult[]` JSON and verify determinism across runs. No new test library is required.

If the team upgrades to `xunit.v3` (3.2.2) and `FluentAssertions` 8.8.0 (recommended in the v1.0 STACK.md but deferred), v1.1 is a natural time to do it — the test count is about to grow significantly and the v3 parallelism model helps.

---

## Installation (v1.1 Changes Only)

```bash
# In src/Directory.Packages.props — update these two version pins:
# Microsoft.CodeAnalysis.CSharp: 4.12.0 → 5.0.0
# Microsoft.CodeAnalysis.CSharp.Workspaces: 4.12.0 → 5.0.0

# No new NuGet packages required for v1.1 core features.

# If doing the xunit v3 + FA upgrade at the same time:
# xunit: 2.9.3 → xunit.v3 3.2.2
# FluentAssertions: 6.12.1 → 8.8.0
```

Update in `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.0.0" />
```

No other package changes are needed for the v1.1 feature set.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| Custom diff engine on `ISymbol` APIs | `Microsoft.DotNet.ApiCompat` CLI tool | ApiCompat is an MSBuild task / CLI tool, not a library — cannot be embedded in a running MCP server or called programmatically in-process. It compares compiled assemblies (not source), which means it operates post-build, not on the live Roslyn symbol graph. Use ApiCompat in CI for safety, but it cannot power the `review_changes` MCP tool. |
| Custom diff engine on `ISymbol` APIs | `DotNetAnalyzers.PublicApiAnalyzer` | A Roslyn analyzer that enforces API surface declaration in text files — useful for CI enforcement but not for runtime diff queries. Not embeddable as a library for on-demand diffing. |
| `System.IO.Hashing` (already present) for file hashing | `MD5` / `SHA256` from `System.Security.Cryptography` | `XxHash64` is 3-5x faster than SHA256 for non-cryptographic content hashing of source files. Already a dependency. Collision probability for file change detection is negligible. |
| `CSharpCompilation.ReplaceSyntaxTree()` for partial re-walk | Full re-parse of all files | Full re-parse is O(n) in file count; `ReplaceSyntaxTree` is O(changed files). For large projects with hundreds of files, partial re-walk is the only practical path. |
| `System.Threading.Channels` (BCL) for debounce | `System.Reactive` (Rx.NET) | Rx adds a significant dependency for a pattern (`Channel<T>` + `Task.Delay` debounce) that is 20 LOC without it. Do not add Rx. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Any third-party "semantic diff" NuGet package | No maintained, embeddable .NET library exists for C# symbol-level semantic diff. The two candidates (`semdiff`, `git-semantic-diff`) are CLI tools or abandoned. Roslyn already provides all the primitives; a custom comparator is ~300 LOC. | Custom `ISymbolDiffEngine` built on Roslyn `ISymbol` APIs |
| `Microsoft.DotNet.ApiCompat` as a library | Not designed for embedding. Assembly-based (not source-based). Cannot operate on an in-memory `SymbolGraphSnapshot`. | Custom diff engine |
| `System.Reactive` (Rx.NET) | Heavyweight dependency for a simple debounce pattern. Introduces `IObservable<T>` abstractions that conflict with the project's `IAsyncEnumerable<T>` + `Channel<T>` conventions. | `System.Threading.Channels.Channel<T>` (BCL inbox) |
| `Microsoft.Build.Locator` (new reference) | Already implicitly used by `MSBuildWorkspace`. Do not add as an explicit dependency — it is transitively available via `Microsoft.CodeAnalysis.Workspaces.MSBuild`. | Existing transitive reference |
| Embedding provider / vector index | Explicitly deferred to v2+. The diff engine results are structured data, not semantic embeddings. | Implement `IVectorIndex` in v2+ |

---

## Stack Patterns for v1.1

**Building the semantic diff engine:**
- Implement `ISymbolDiffEngine` in `DocAgent.Core` (no IO dependencies — pure symbol comparison)
- Implement `SymbolDiffEngine : ISymbolDiffEngine` in `DocAgent.Ingestion` (has access to Roslyn APIs and snapshot loading)
- Key `SymbolNode` records by `DocumentationCommentId` across two snapshots
- Classify changes: `Added`, `Removed`, `SignatureChanged`, `NullabilityChanged`, `AccessibilityChanged`, `ConstraintChanged`, `DocChanged`
- Breaking = `Removed` | `AccessibilityNarrowed` | `SignatureChanged` | `NullabilityChanged` (non-nullable added to param)

**Building incremental ingestion:**
- Add `FileHashManifest` record alongside `SymbolGraphSnapshot` in the artifact store
- Hash each `.cs` file with `XxHash64` before and after; compute added/changed/removed sets
- Use `CSharpCompilation.ReplaceSyntaxTree()` for the changed files only
- Re-walk only the `ISymbol`s declared in changed files; merge into the existing snapshot
- Store new snapshot with incremented version label; keep previous version for diff

**Building `review_changes` MCP tool:**
- Accept two snapshot version labels (or "latest" + "previous") as parameters
- Load both snapshots from `SnapshotStore`
- Run `ISymbolDiffEngine.Diff(snapshotA, snapshotB)`
- Return `SymbolDiffResult[]` as structured MCP response (serialize with `System.Text.Json`)
- Annotate each result with `IsBreaking`, `ChangeType`, `Before`, `After` fields

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.0.0 | Must be the same version — always upgrade both together. |
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 | Testing package targets `netstandard2.0`; does not require a version bump when upgrading Roslyn core. |
| `System.IO.Hashing` 9.0.0 | .NET 10 | Already pinned. `XxHash64` is the right algorithm — non-cryptographic, fast, stable API. |
| `Verify.Xunit` 31.12.5 | `xunit` 2.9.3 and `xunit.v3` 3.x | Works with both. No version change needed if the xunit upgrade is deferred. |

---

## Sources

- [NuGet: Microsoft.CodeAnalysis.CSharp 5.0.0](https://www.nuget.org/packages/microsoft.codeanalysis.csharp/) — confirmed latest stable, HIGH confidence
- [Roslyn GitHub: CSharpCompilation.ReplaceSyntaxTree](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/CSharpCompilation.cs) — documented public API, HIGH confidence
- [Microsoft Learn: Microsoft.DotNet.ApiCompat.Tool](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/global-tool) — CLI/MSBuild only, not embeddable library, HIGH confidence
- [NuGet: DotNetAnalyzers.PublicApiAnalyzer](https://www.nuget.org/packages/DotNetAnalyzers.PublicApiAnalyzer) — analyzer-only, not runtime diff library, HIGH confidence
- [GitHub: git-semantic-diff](https://github.com/gboya/git-semantic-diff) — uses Roslyn APIs for semantic analysis; confirms Roslyn is sufficient primitive, MEDIUM confidence
- [Microsoft Learn: FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-io-filesystemwatcher) — BCL inbox, no package needed, HIGH confidence
- [Roslyn incremental generators docs](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) — confirms ReplaceSyntaxTree caching model, HIGH confidence

---

*Stack research for: v1.1 semantic diff engine, incremental ingestion, change review tooling*
*Researched: 2026-02-28*
