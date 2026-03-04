# Phase 23: Dependency Foundation - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Upgrade all Microsoft.CodeAnalysis packages to 4.14.0, remove the VersionOverride workaround in DocAgent.Tests.csproj, enable NuGetAudit in Directory.Build.props, and audit all NuGet dependencies for vulnerabilities. This is a foundational correctness phase — no new features, no new code beyond build configuration changes.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion

User deferred all implementation choices to Claude. The following decisions should be made by the researcher/planner based on technical evaluation:

**NuGet audit scope:**
- Whether to use `NuGetAudit=true` with `NuGetAuditMode=direct` (suppress transitive) or full transitive audit
- Where to place the NuGetAudit configuration (Directory.Build.props vs Directory.Packages.props)
- Whether to record an audit baseline snapshot file or rely on clean restore as the baseline
- Whether to remove existing per-project `NuGetAuditMode=direct` overrides (Ingestion, Indexing, McpServer, Tests)

**Analyzer testing fallback:**
- Evaluate compatibility of `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2` with Roslyn 4.14.0 first
- If incompatible: pick least-disruptive path (VersionOverride on test packages only, upgrade testing packages, or skip analyzer tests temporarily)
- Determine minimum set of Microsoft.CodeAnalysis packages that need to move to 4.14.0

**Validation order:**
- Claude decides whether to validate incrementally (upgrade-then-test, then audit-then-test) or batch all changes
- If Verify.Xunit golden files change due to Roslyn bump, Claude evaluates whether diffs are cosmetic or semantic and decides per-file

**Package update scope:**
- Strict Roslyn + audit only — do NOT bump non-Roslyn packages unless the vulnerability audit flags them
- Include Benchmarks project and Analyzers project in restore/audit scope (success criteria requires zero NU1107 across ALL projects)

**Commit structure:**
- Claude decides whether to use one or two commits (Roslyn bump vs audit config) based on clean git history

**Vulnerability tolerance:**
- Claude evaluates each vulnerability's relevance to the project's threat model
- Transitive vulnerabilities in MSBuild workspace chain are acceptable to suppress with documented rationale
- Decide whether suppressions go in Directory.Build.props (centralized) or per-project
- Remove NU1107/NU1608 NoWarn entries from DocAgent.Tests.csproj if the upgrade resolves them
- No additional CI audit script needed — NuGetAudit + TreatWarningsAsErrors is sufficient

</decisions>

<specifics>
## Specific Ideas

No specific requirements — user deferred all implementation choices to Claude's discretion. The success criteria from ROADMAP.md are the binding constraints:

1. `dotnet restore` completes with zero NU1107 warnings across all projects including Benchmarks and Analyzers
2. The VersionOverride workaround in DocAgent.Tests.csproj is removed
3. `NuGetAudit` is enabled in Directory.Build.props and `dotnet restore` reports no known vulnerabilities
4. All five Microsoft.CodeAnalysis.* packages are at 4.14.0 and the package audit baseline is recorded

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Directory.Packages.props` (root): Central package management — all version pins live here
- `src/Directory.Packages.props`: CPM enablement file that imports root props
- `src/Directory.Build.props`: Shared build properties (TFM, LangVersion, nullable, TreatWarningsAsErrors)

### Established Patterns
- Central Package Management (CPM) via `ManagePackageVersionsCentrally` — all version changes go through root `Directory.Packages.props`
- `VersionOverride` used as escape hatch for version conflicts (currently in DocAgent.Tests.csproj line 35)
- Per-project `NuGetAuditMode=direct` on 4 projects to suppress transitive vulnerability warnings
- BenchmarkDotNet in separate project with relaxed `TreatWarningsAsErrors` to isolate transitive Roslyn conflicts

### Integration Points
- `Directory.Packages.props` lines 13-15: Three Roslyn packages at 4.12.0 (CSharp, CSharp.Workspaces, Workspaces.MSBuild)
- `Directory.Packages.props` line 42: `Microsoft.CodeAnalysis.Common` at 4.14.0 (VersionOverride support entry)
- `DocAgent.Tests.csproj` line 35: `VersionOverride="4.14.0"` on Microsoft.CodeAnalysis.Common
- `DocAgent.Tests.csproj`: NU1107/NU1608 NoWarn suppressions
- 4 csproj files with `NuGetAuditMode=direct`: Ingestion, Indexing, McpServer, Tests

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 23-dependency-foundation*
*Context gathered: 2026-03-04*
