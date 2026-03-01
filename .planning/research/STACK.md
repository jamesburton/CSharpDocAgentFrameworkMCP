# Stack Research

**Domain:** .NET solution-level symbol graph ingestion (v1.2 additions only)
**Researched:** 2026-03-01
**Confidence:** HIGH

---

## Context: What Already Exists (Do Not Re-Research)

The following are **already pinned** in `Directory.Packages.props` and must NOT be re-added or changed:

| Package | Pinned Version | Role |
|---------|---------------|------|
| `Microsoft.CodeAnalysis.CSharp` | 4.12.0 | Roslyn compiler APIs |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 4.12.0 | Roslyn workspace model |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 4.12.0 | MSBuildWorkspace — already present, already versioned |
| `MessagePack` | 3.1.4 | Snapshot serialization |
| `Lucene.Net` + `Lucene.Net.Analysis.Common` | 4.8.0-beta00017 | BM25 search index |
| `ModelContextProtocol` | 1.0.0 | MCP server SDK |
| `System.IO.Hashing` | 9.0.0 | SHA-256 file hashing |
| `OpenTelemetry.*` | 1.15.0 | Telemetry |
| `xunit`, `FluentAssertions`, `Verify.Xunit` | 2.9.3 / 6.12.1 / 31.12.5 | Testing |

**This file covers only what v1.2 adds.**

---

## New Additions for v1.2

### Core — Solution Loading

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| `Microsoft.Build.Locator` | 1.11.2 | Locate and register MSBuild assemblies at runtime before `MSBuildWorkspace.Create()` | Required companion to `MSBuildWorkspace`. Without it, workspace construction throws a MEF composition error because the process cannot find `Microsoft.Build.*` assemblies. `MSBuildLocator.RegisterDefaults()` must be called before any MSBuild/Roslyn workspace type is loaded. Latest stable as of Nov 2025. |

**Why `MSBuildWorkspace` for solution loading:** `MSBuildWorkspace.OpenSolutionAsync()` is the only Roslyn-sanctioned API that turns a `.sln` file into a typed multi-project `Solution` object with compiler-grade `Compilation` chains and fully resolved `ProjectReference` cross-project edges. Alternatives produce inferior results — they give file lists but not semantic models. Since `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 is already in CPM, `Microsoft.Build.Locator` is the **only net-new dependency** for solution loading.

**Important: the v1.1 STACK.md said `Microsoft.Build.Locator` was already transitively available and not needed as an explicit reference. That was incorrect for server/host processes.** In `DocAgent.McpServer` and `DocAgent.AppHost`, which are standalone executables, the locator must be explicitly referenced to guarantee it is present at startup before the registration call. Add it explicitly.

### Supporting — NuGet Stub Nodes

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `NuGet.Packaging` | 6.12.0 | Read `.nuspec` metadata (package ID, version, exported type surface) from packages in the global NuGet cache | Building stub `SymbolNode`s for NuGet package types. Provides `PackageArchiveReader` and `NuspecReader` to extract package identity from local `.nupkg` files without any network calls. |
| `NuGet.Configuration` | 6.12.0 | Resolve the global NuGet packages folder path via `SettingsUtility.GetGlobalPackagesFolder()` | Locating the package cache without hardcoding paths. Reads `NuGet.Config` from standard locations. Must match `NuGet.Packaging` major.minor. |

**Why 6.12.0 not 7.x:** NuGet.Packaging 7.x is prerelease targeting internal NuGet tooling. The 6.x stable line is the public Client SDK per the official Microsoft Learn NuGet Client SDK documentation. The latest stable is 6.12.0 (confirmed on NuGet Gallery).

**Why not `NuGet.Protocol`:** `NuGet.Protocol` is for querying a NuGet feed over the network. v1.2 requires stub nodes from the local package cache only — no network calls in tests. `NuGet.Packaging` reads already-restored `.nupkg` files on disk, satisfying the no-network-in-tests constraint.

---

## Installation (CPM Additions to Directory.Packages.props)

```xml
<!-- Solution loading — required companion to MSBuildWorkspace for standalone host processes -->
<PackageVersion Include="Microsoft.Build.Locator" Version="1.11.2" />

<!-- NuGet package metadata for stub nodes (local cache reads, no network) -->
<PackageVersion Include="NuGet.Packaging" Version="6.12.0" />
<PackageVersion Include="NuGet.Configuration" Version="6.12.0" />
```

Reference in project files:

```xml
<!-- DocAgent.McpServer and DocAgent.AppHost — both need the locator for startup registration -->
<PackageReference Include="Microsoft.Build.Locator" />

<!-- DocAgent.Ingestion — the layer that does discovery and NuGet stub node construction -->
<PackageReference Include="NuGet.Packaging" />
<PackageReference Include="NuGet.Configuration" />
```

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| `MSBuildWorkspace.OpenSolutionAsync()` | Parse `.sln` text manually (regex the `Project(...)` blocks) | Produces a file-path list only. Cannot resolve `ProjectReference` edges between projects or produce `Compilation` objects with inter-project type resolution. Breaks on SDK-style projects that expand globs. |
| `MSBuildWorkspace.OpenSolutionAsync()` | `MSBuildProjectGraph` (Microsoft.Build package directly) | Gives build-ordering topology but not a Roslyn `Solution` or `Compilation`. No access to the Roslyn semantic model, symbol tables, or cross-project type resolution needed to build `SymbolNode` / `SymbolEdge` graphs. |
| `NuGet.Packaging` (local cache reads) | `NuGet.Protocol` (feed queries) | Requires HTTP network access; violates the no-network-in-tests constraint. All restored packages already exist in the local cache; no feed query is needed for stub node metadata. |
| `NuGet.Packaging` 6.12.0 | `NuGet.Packaging` 7.x | 7.x is prerelease/internal. 6.x is the stable public Client SDK line per Microsoft Learn documentation. |
| `NuGet.Packaging` for stub type metadata | `MetadataReference.CreateFromFile()` on assembly DLLs in the package cache | Loading full assembly metadata via Roslyn is the right approach for resolving cross-project types semantically, but for stub nodes (which intentionally have no source span and minimal data), reading the nuspec for package identity is sufficient and much cheaper. Full metadata loading is the correct approach IF deep type resolution for NuGet packages is added in a later milestone. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `Microsoft.Build` (direct reference) | Creates assembly version conflicts with the copy loaded by `MSBuildLocator` at runtime. The locator's purpose is to load the correct `Microsoft.Build.*` from the SDK install. Referencing it directly as a `PackageReference` causes binding failures. If it appears as a transitive dependency, set `ExcludeAssets=runtime`. | `Microsoft.Build.Locator` only |
| `NuGet.Protocol` | Network I/O; violates no-network-in-tests constraint; overkill for reading already-restored packages from local cache | `NuGet.Packaging` for local `.nupkg` file reads |
| `Buildalyzer` | Third-party wrapper around MSBuildWorkspace; adds abstraction without benefit since the project already has direct Roslyn workspace packages. Buildalyzer was useful before MSBuildLocator existed; it is no longer necessary. | `MSBuildWorkspace` directly |
| Any new serialization library | MessagePack 3.1.4 already handles snapshot serialization. Stub node types must use the same contractless resolver pattern. | Extend existing MessagePack contracts |
| `Microsoft.Build.Framework` / `Microsoft.Build.Tasks.Core` | These are internal MSBuild implementation packages. Referencing them alongside MSBuildLocator causes binding redirect conflicts. | MSBuildLocator manages these at runtime |

---

## Integration Notes

### MSBuildLocator Call Site

`MSBuildLocator.RegisterDefaults()` must be called **once, at process startup**, before any `Microsoft.CodeAnalysis.MSBuild` or `Microsoft.Build.*` type is referenced — including before `MSBuildWorkspace.Create()` is called.

In `DocAgent.McpServer` or `DocAgent.AppHost` Program.cs:

```csharp
using Microsoft.Build.Locator;

// Must be first — before any type from Microsoft.CodeAnalysis.MSBuild is loaded
MSBuildLocator.RegisterDefaults();

// Then proceed with host builder, DI, etc.
var builder = Host.CreateApplicationBuilder(args);
// ...
```

Failure to call this before any MSBuild type is loaded results in a MEF composition exception with no useful diagnostic message. This is the single most common failure mode when adopting `MSBuildWorkspace`.

### Cross-Project Edge Representation

`MSBuildWorkspace.OpenSolutionAsync()` produces a Roslyn `Solution` where each `Project` has a typed `ProjectReferences` collection (edges to other `Project`s in the solution). These map directly to new `SymbolEdge` entries with kind `ProjectReference` in `SymbolGraphSnapshot`. No additional library is needed — the `ProjectReference` API is part of the already-pinned `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0.

### NuGet Stub Node Strategy

For each `PackageReference` in a project that is NOT a `ProjectReference` (i.e., resolves to a NuGet package, not a local project):

1. Resolve global NuGet cache path: `NuGet.Configuration.SettingsUtility.GetGlobalPackagesFolder(settings)`
2. Locate the `.nupkg` file for the package ID + version
3. Open with `NuGet.Packaging.PackageArchiveReader`
4. Read package identity and description via `NuspecReader.GetIdentity()`, `GetDescription()`
5. Create a lightweight `SymbolNode` with `IsStub = true`, no `SourceSpan`, doc from nuspec description
6. Add `SymbolEdge` from the referencing project root node to the stub node with kind `NuGetReference`

This stays entirely on the local filesystem — no network calls, no test isolation problems.

### ExcludeAssets Pattern for Microsoft.Build

If `Microsoft.Build` or `Microsoft.Build.Framework` appear as transitive dependencies (from NuGet packages), suppress their runtime assets to avoid conflict with the MSBuildLocator-managed copy:

```xml
<PackageReference Include="Microsoft.Build" ExcludeAssets="runtime" />
```

This is the documented pattern from the MSBuildWorkspace guidance by the Roslyn team.

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|----------------|-------|
| `Microsoft.Build.Locator` 1.11.2 | `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 | Locator version is independent of Roslyn version; 1.11.2 supports .NET 10 SDK discovery |
| `NuGet.Packaging` 6.12.0 | `NuGet.Configuration` 6.12.0 | Must match major.minor; version mismatches cause `NuGet.Frameworks` assembly binding failures |
| `NuGet.Packaging` 6.12.0 | .NET 10 (`net10.0`) | Targets `netstandard2.0`; fully compatible with `net10.0` TFM |
| `Microsoft.Build.Locator` 1.11.2 | .NET 10 | Confirmed: targets `net472` + `netstandard2.0`; loads .NET SDK MSBuild from `dotnet` install |

---

## Sources

- [NuGet Gallery: Microsoft.Build.Locator 1.11.2](https://www.nuget.org/packages/Microsoft.Build.Locator/) — version confirmed, latest stable Nov 2025, HIGH confidence
- [NuGet Gallery: Microsoft.CodeAnalysis.Workspaces.MSBuild](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/) — confirmed 4.12.0 already in CPM, HIGH confidence
- [NuGet Gallery: NuGet.Packaging 6.12.0](https://www.nuget.org/packages/NuGet.Packaging/6.12.0) — stable client SDK line, HIGH confidence
- [NuGet Client SDK — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk) — official API documentation for `NuGet.Packaging` + `NuGet.Configuration`, MEDIUM confidence
- [Using MSBuildWorkspace — Dustin Campbell (Roslyn team)](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3) — authoritative usage guide including `ExcludeAssets=runtime` pattern and MEF composition pitfall, HIGH confidence
- [MSBuildWorkspace cross-project reference issue #36072](https://github.com/dotnet/roslyn/issues/36072) — confirmed ProjectReference loading behavior, MEDIUM confidence
- WebSearch: MSBuildWorkspace + MSBuildLocator + solution loading patterns — verified against official Roslyn gist and NuGet Gallery, HIGH confidence overall

---

*Stack research for: DocAgentFramework v1.2 multi-project/solution-level additions*
*Researched: 2026-03-01*
