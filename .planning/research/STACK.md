# Stack Research: NuGet Package Mapping (v2.5)

**Project:** DocAgentFramework — NuGet Package Mapping additions
**Researched:** 2026-03-26
**Scope:** NEW additions only. Existing stack validated and unchanged: Roslyn 4.14.0, Lucene.Net 4.8-beta, MessagePack 3.1.4, MCP SDK 1.0.0, Aspire 13.1.2, OpenTelemetry 1.15.0, BenchmarkDotNet 0.15.8, YamlDotNet 16.3.0, System.IO.Hashing 9.0.0, Node.js sidecar.
**Confidence:** HIGH

---

## What Needs a Stack Decision

The v2.5 milestone requires four new technical capabilities:

1. **Parse `packages.lock.json`** — extract direct + transitive dependency trees per TFM
2. **Resolve NuGet global cache paths** — find DLL locations cross-platform, respecting env/config overrides
3. **Extract public API from DLLs** — load assemblies without MSBuild, walk public types and members
4. **Model the PackageGraph** — new domain type for dependency metadata

None of these require a completely new subsystem. Three need two new NuGet SDK packages; one reuses Roslyn already in the project.

---

## Recommended Stack Additions

### New NuGet Packages Required

| Package | Version | Purpose | Why This One |
|---------|---------|---------|-------------|
| `NuGet.ProjectModel` | 7.3.0 | Parse `packages.lock.json` via `PackagesLockFileFormat.Read()` | The first-party NuGet package that owns the `packages.lock.json` format. `PackagesLockFileFormat.Read(filePath)` returns a `PackagesLockFile` with `Targets` (one per TFM) containing `Libraries` with `PackageDependencyType` (Direct/Transitive/CentralTransitive), resolved version, and `ContentHash`. The alternative — manual `System.Text.Json` parsing — must be maintained as the format evolves; `PackagesLockFileFormat` is the canonical parser owned by the same team that writes the file. |
| `NuGet.Configuration` | 7.3.0 | Cross-platform global cache path resolution via `SettingsUtility.GetGlobalPackagesFolder()` | The only correct way to find the NuGet cache. The default path (`~/.nuget/packages` on Windows/macOS, `/home/user/.nuget/packages` on Linux) can be overridden by the `NUGET_PACKAGES` environment variable or `globalPackagesFolder` in `nuget.config`. `Settings.LoadDefaultSettings(null)` + `SettingsUtility.GetGlobalPackagesFolder(settings)` traverses all override sources in the correct precedence order. Hardcoding the default path breaks CI environments and custom configurations. |

**Version rationale:** 7.3.0 is the latest stable as of 2026-02-10, aligned with Visual Studio 2022 17.x/.NET SDK 9.x. Both packages share the same versioning cadence — always use matching versions. The project targets .NET 10; these packages ship a `net8.0` TFM which is binary-compatible with .NET 10.

**Confidence:** HIGH — versions verified on NuGet.org (2026-02-10 release date confirmed).

### No New Package: DLL Public API Extraction Uses Existing Roslyn

DLL reflection for public API extraction requires zero new packages. The approach:

```csharp
// Already available in DocAgent.Ingestion via Microsoft.CodeAnalysis.CSharp 4.14.0
var reference = MetadataReference.CreateFromFile(dllPath);
var compilation = CSharpCompilation.Create(
    assemblyName: "nuget-reflection",
    references: new[] { reference });
var assemblySymbol = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
// Walk assemblySymbol.GlobalNamespace recursively via GetTypeMembers()
// Filter: typeSymbol.DeclaredAccessibility == Accessibility.Public
```

`MetadataReference.CreateFromFile` reads the PE file into native memory via Roslyn's metadata reader — no `System.Reflection` loading, no `AssemblyLoadContext` isolation needed, no runtime type conflicts. `IAssemblySymbol` and `GetTypeMembers()` are the same API surface already used for C# source symbol extraction. The extraction pipeline for DLL-sourced types is structurally identical to the existing Roslyn walker.

**What NOT to add:**
- `System.Reflection.MetadataLoadContext` — for runtime reflection isolation, not needed here; Roslyn's metadata reader is already available and produces compiler-typed symbols
- `Mono.Cecil` — another PE reader; redundant with Roslyn already in the project
- `dnlib` — same problem as Mono.Cecil

**Confidence:** HIGH — `MetadataReference.CreateFromFile` + `IAssemblySymbol` is the established Roslyn pattern, stable since Roslyn 1.x.

---

## Dependency Chain Warning: NuGet.ProjectModel Pulls NuGet.Protocol

`NuGet.ProjectModel 7.3.0` depends on `NuGet.DependencyResolver.Core 7.3.0`, which depends on `NuGet.Protocol 7.3.0`. NuGet.Protocol is a heavy package (~3MB) that brings HTTP feed interaction APIs. These are not used, but they will appear in the dependency graph.

**Mitigation:** This is unavoidable with NuGet.ProjectModel. The transitive dependencies are passive — no initialization, no network calls, no startup cost unless explicitly invoked. Add `NuGet.ProjectModel` only to `DocAgent.Ingestion.csproj` (where lock file parsing belongs), not to Core or McpServer, to limit the blast radius.

**What NOT to add:**
- `NuGet.Protocol` directly — it's a transitive dependency; direct reference adds nothing
- `NuGet.Commands` — high-level CLI command wrappers, not needed
- `NuGet.PackageManagement` — Visual Studio package management, not needed

---

## Assembly Location Convention

Once the global cache root is resolved, DLL paths follow a deterministic convention:

```
{globalPackagesFolder}/{packageId.ToLower()}/{version}/lib/{tfm}/{assemblyName}.dll
```

Example on Windows:
```
C:\Users\james\.nuget\packages\newtonsoft.json\13.0.3\lib\net6.0\Newtonsoft.Json.dll
```

TFM selection for .NET 10 projects follows NuGet compatibility rules (prefer `net10.0` > `net9.0` > `net8.0` > `net6.0` > `netstandard2.1` > `netstandard2.0`). The `packages.lock.json` resolved target already records which TFM was selected during restore; no TFM resolution logic is needed beyond reading the lock file's target key.

**Edge cases to handle:**
- Package has no `lib/` folder (tools-only packages, content-only packages) — skip DLL extraction gracefully
- `ref/` folder vs `lib/` folder — prefer `lib/` for runtime assemblies; `ref/` contains compile-time reference assemblies which are suitable but less complete
- DLL not present locally (package not yet restored) — emit warning, create stub node, do not fail ingestion

---

## PackageGraph Domain Type (No New Dependencies)

The `PackageGraph` structure is a new domain type in `DocAgent.Core`. It requires no additional packages:

```csharp
// DocAgent.Core — pure domain, no IO dependencies
public sealed record PackageIdentity(string Id, string Version);

public enum DependencyKind { Direct, Transitive, CentralTransitive }

public sealed record PackageDependency(
    PackageIdentity Package,
    DependencyKind Kind,
    string TargetFramework,
    string? ContentHash);

public sealed class PackageGraph
{
    public string ProjectPath { get; init; } = "";
    public IReadOnlyList<PackageDependency> Dependencies { get; init; } = [];
    // Edges: which package depends on which (derived from lock file)
    public IReadOnlyDictionary<PackageIdentity, IReadOnlyList<PackageIdentity>> DependencyEdges { get; init; }
        = new Dictionary<PackageIdentity, IReadOnlyList<PackageIdentity>>();
}
```

`PackageGraph` follows the same immutable record pattern as `SymbolGraphSnapshot`. It serializes cleanly with MessagePack's ContractlessStandardResolver (same pattern as existing diff types). No new MessagePack attributes needed.

---

## Target Project Placement

| Component | Target Project | Rationale |
|-----------|---------------|-----------|
| `NuGet.ProjectModel` package reference | `DocAgent.Ingestion` | Lock file parsing is an ingestion concern, not a core domain concern |
| `NuGet.Configuration` package reference | `DocAgent.Ingestion` | Cache path resolution is part of ingestion, not serving |
| `PackageGraph`, `PackageDependency`, `DependencyKind` | `DocAgent.Core` | Domain types belong in Core; no IO, no NuGet SDK deps in Core |
| `IPackageGraphBuilder` interface | `DocAgent.Core` | Follows existing interface pattern (`ISymbolGraphBuilder`, `ISearchIndex`) |
| `PackageLockFileParser` | `DocAgent.Ingestion` | Implementation detail; uses NuGet.ProjectModel |
| `NuGetCacheResolver` | `DocAgent.Ingestion` | Platform-specific path logic; uses NuGet.Configuration |
| `DllPublicApiExtractor` | `DocAgent.Ingestion` | Uses `MetadataReference.CreateFromFile`; same layer as `RoslynSymbolWalker` |
| New MCP tools (`get_dependencies`, `find_package_usages`) | `DocAgent.McpServer` | All tools live in McpServer; no SDK deps leak into Core |

---

## Installation

Add to root `Directory.Packages.props`:

```xml
<!-- NuGet lock file parsing and cache path resolution -->
<PackageVersion Include="NuGet.ProjectModel" Version="7.3.0" />
<PackageVersion Include="NuGet.Configuration" Version="7.3.0" />
```

Add to `DocAgent.Ingestion/DocAgent.Ingestion.csproj`:

```xml
<PackageReference Include="NuGet.ProjectModel" />
<PackageReference Include="NuGet.Configuration" />
```

No changes to `DocAgent.Core.csproj`, `DocAgent.McpServer.csproj`, or `DocAgent.AppHost.csproj`.

---

## Alternatives Considered

| Recommended | Alternative | Why Not |
|-------------|-------------|---------|
| `NuGet.ProjectModel` for lock file parsing | Manual `System.Text.Json` parsing | `packages.lock.json` format is an internal NuGet implementation detail with three format versions (V1/V2/V3). `PackagesLockFileFormat` handles all versions. Manual parsing creates maintenance burden on every NuGet SDK update. |
| `NuGet.Configuration` for cache path | Hardcode `~/.nuget/packages` | Breaks CI environments with `NUGET_PACKAGES` set, custom corporate cache locations, and Azure DevOps agents with non-standard home directories. |
| Roslyn `MetadataReference.CreateFromFile` | `System.Reflection.MetadataLoadContext` | MetadataLoadContext requires an assembly resolver chain and loads into a live CLR context. Roslyn's metadata reader is purely structural, produces typed `ISymbol` objects directly, and doesn't risk type-loading conflicts. We already use `IAssemblySymbol` for C# sources — DLL extraction is the same API. |
| Roslyn `MetadataReference.CreateFromFile` | `Mono.Cecil` or `dnlib` | These PE readers are alternatives to Roslyn's, not complements. Adding either creates a second symbol model that must be translated into `SymbolNode`. Roslyn produces `IAssemblySymbol` which maps directly to the existing `SymbolNode` extraction pattern. |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `NuGet.Protocol` (direct reference) | Heavy HTTP feed client; 3MB+ package; zero usage in this feature. Already pulled transitively — don't add a direct reference. | Implicit transitive dependency only |
| `NuGet.Commands` | High-level restore/install commands; wraps MSBuild. We only read lock files, not run restore. | `NuGet.ProjectModel` directly |
| `NuGet.PackageManagement` | Visual Studio integration layer; not for programmatic use outside VS. | Not needed |
| `Mono.Cecil` / `dnlib` | Redundant PE readers when Roslyn is already present. | `MetadataReference.CreateFromFile` |
| `System.Reflection.MetadataLoadContext` | Loads assemblies into CLR; risk of type conflicts with .NET 10 BCL types; requires resolver chain. | Roslyn metadata reader (structural only) |
| `NuGet.Versioning` (direct reference) | Already a transitive dependency of `NuGet.ProjectModel`. `NuGetVersion.Parse()` is available without a direct reference. | Transitive; add direct reference only if `NuGetVersion` is used in `DocAgent.Core` |

---

## Version Compatibility

| Package | Version | Compatible With | Notes |
|---------|---------|-----------------|-------|
| `NuGet.ProjectModel` | 7.3.0 | .NET 10 (net8.0 TFM) | Ships net8.0 and net472 TFMs; net8.0 is used on .NET 10 |
| `NuGet.Configuration` | 7.3.0 | .NET 10 (net8.0 TFM) | Same versioning cadence as ProjectModel; must match |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.0 | `MetadataReference.CreateFromFile` stable since 1.x | Already in project; no change |
| `NuGet.ProjectModel` 7.3.0 | `NuGet.Configuration` 7.3.0 | Must use same major.minor; different versions cause binding conflicts | Always update together |

---

## Sources

- [NuGet.ProjectModel 7.3.0 on NuGet.org](https://www.nuget.org/packages/NuGet.ProjectModel/) — version and release date confirmed (HIGH confidence)
- [NuGet.Configuration 7.3.0 on NuGet.org](https://www.nuget.org/packages/NuGet.Configuration/) — version and release date confirmed (HIGH confidence)
- [NuGet Client SDK reference](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk) — package purpose descriptions (HIGH confidence)
- [PackagesLockFileFormat.cs in NuGet.Client](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.ProjectModel/ProjectLockFile/PackagesLockFileFormat.cs) — `Read()` overloads and parsing logic (HIGH confidence)
- [PackagesLockFile.cs in NuGet.Client](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.ProjectModel/ProjectLockFile/PackagesLockFile.cs) — `Targets`, `Version`, `Path` properties (HIGH confidence)
- [NuGetEnvironment.cs in NuGet.Client](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs) — cross-platform path resolution implementation (HIGH confidence)
- [Managing global packages and cache folders](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders) — NUGET_PACKAGES override, globalPackagesFolder config (HIGH confidence)
- [MetadataReference.CreateFromFile on MSDN](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.metadatareference.createfromfile) — method signature and memory behavior (HIGH confidence)
- Existing codebase: `Directory.Packages.props`, `DocAgent.Ingestion.csproj`, `DocAgent.Core.csproj` — direct file reads (HIGH confidence)

---
*Stack research for: DocAgentFramework v2.5 NuGet Package Mapping*
*Researched: 2026-03-26*
