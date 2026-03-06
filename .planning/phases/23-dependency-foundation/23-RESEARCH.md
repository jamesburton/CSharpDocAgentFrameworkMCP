# Phase 23: Dependency Foundation - Research

**Researched:** 2026-03-06
**Domain:** NuGet package management, Roslyn version upgrades, NuGetAudit configuration
**Confidence:** HIGH

## Summary

Phase 23 is a build configuration phase with no new feature code. The work involves three coordinated changes: (1) upgrading all Microsoft.CodeAnalysis.* packages from 4.12.0 to 4.14.0, (2) removing the VersionOverride workaround in DocAgent.Tests.csproj, and (3) enabling NuGetAudit in Directory.Build.props. All three Roslyn 4.14.0 packages are confirmed available on NuGet (published May 15, 2025). The analyzer testing packages (Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2) declare a loose dependency on Microsoft.CodeAnalysis.CSharp.Workspaces >= 1.0.1, meaning they are forward-compatible with 4.14.0. No actual analyzer test code exists yet in the codebase, so the testing packages are unused dependencies -- this simplifies the upgrade significantly.

A critical discovery: the project currently has a pre-existing NU1507 warning (multiple NuGet package sources without source mapping under CPM) that causes restore failures on projects with TreatWarningsAsErrors=true. This is NOT in scope for Phase 23 (it is about NU1107, not NU1507), but the planner must be aware that `dotnet restore` currently fails for non-test projects due to this unrelated issue. The success criteria specify "zero NU1107 warnings" not "zero warnings overall."

**Primary recommendation:** Upgrade all three Roslyn packages to 4.14.0 in Directory.Packages.props, remove the VersionOverride and NoWarn entries from DocAgent.Tests.csproj, remove per-project NuGetAuditMode=direct overrides, enable NuGetAudit centrally in Directory.Build.props with `NuGetAuditMode=direct` and use `NuGetAuditSuppress` for known transitive advisories from the MSBuild workspace chain.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

No locked decisions -- user deferred all implementation choices to Claude.

### Claude's Discretion

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
- Strict Roslyn + audit only -- do NOT bump non-Roslyn packages unless the vulnerability audit flags them
- Include Benchmarks project and Analyzers project in restore/audit scope (success criteria requires zero NU1107 across ALL projects)

**Commit structure:**
- Claude decides whether to use one or two commits (Roslyn bump vs audit config) based on clean git history

**Vulnerability tolerance:**
- Claude evaluates each vulnerability's relevance to the project's threat model
- Transitive vulnerabilities in MSBuild workspace chain are acceptable to suppress with documented rationale
- Decide whether suppressions go in Directory.Build.props (centralized) or per-project
- Remove NU1107/NU1608 NoWarn entries from DocAgent.Tests.csproj if the upgrade resolves them
- No additional CI audit script needed -- NuGetAudit + TreatWarningsAsErrors is sufficient

### Deferred Ideas (OUT OF SCOPE)

None -- discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| PKG-01 | All five Microsoft.CodeAnalysis packages upgraded to 4.14.0, VersionOverride workaround removed | Roslyn 4.14.0 packages confirmed on NuGet (May 2025). Analyzer testing packages have loose dependency >= 1.0.1, forward-compatible. VersionOverride and NoWarn entries can be cleanly removed. |
| PKG-02 | All NuGet dependencies audited for vulnerabilities and outdated versions; NuGetAudit enabled in Directory.Build.props | NuGetAudit MSBuild properties documented. .NET 10 defaults NuGetAuditMode=all. Known transitive vulnerability from Microsoft.Build.Tasks.Core via Workspaces.MSBuild -- use NuGetAuditSuppress for specific advisories. |
</phase_requirements>

## Standard Stack

### Core (no new libraries -- configuration changes only)

| Library | Current | Target | Purpose | Change |
|---------|---------|--------|---------|--------|
| Microsoft.CodeAnalysis.CSharp | 4.12.0 | 4.14.0 | Roslyn C# compiler APIs | Version bump in Directory.Packages.props |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 4.12.0 | 4.14.0 | Roslyn workspace APIs | Version bump in Directory.Packages.props |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 4.12.0 | 4.14.0 | MSBuild project loading | Version bump in Directory.Packages.props |
| Microsoft.CodeAnalysis.Common | 4.14.0 | 4.14.0 | Roslyn common types (transitive) | Already at 4.14.0 -- remove VersionOverride, keep CPM entry |
| Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit | 1.1.2 | 1.1.2 | Analyzer test infrastructure | No change needed -- compatible with 4.14.0 |

### Key Discovery: "Five Packages" Identification

The success criteria references "all five Microsoft.CodeAnalysis.* packages." These are:

1. `Microsoft.CodeAnalysis.CSharp` (4.12.0 -> 4.14.0)
2. `Microsoft.CodeAnalysis.CSharp.Workspaces` (4.12.0 -> 4.14.0)
3. `Microsoft.CodeAnalysis.Workspaces.MSBuild` (4.12.0 -> 4.14.0)
4. `Microsoft.CodeAnalysis.Common` (already 4.14.0 via VersionOverride -- normalize to CPM)
5. `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` + `Microsoft.CodeAnalysis.Testing.Verifiers.XUnit` are the remaining CodeAnalysis packages but at 1.1.2 -- these are test infrastructure, NOT Roslyn compiler packages

**Clarification:** The "five" likely refers to the four Roslyn compiler packages (CSharp, CSharp.Workspaces, Workspaces.MSBuild, Common) plus one transitive (e.g., Workspaces.Common brought in by Workspaces.MSBuild). The testing packages stay at 1.1.2.

## Architecture Patterns

### Recommended Change Structure

```
Directory.Packages.props (root)    # Version pins: bump 3 packages to 4.14.0
src/Directory.Build.props          # Add NuGetAudit properties
tests/DocAgent.Tests.csproj        # Remove VersionOverride, NoWarn, TreatWarningsAsErrors=false
tests/DocAgent.Benchmarks.csproj   # Remove NoWarn if NU1608 resolved
src/DocAgent.Ingestion.csproj      # Remove NuGetAuditMode=direct
src/DocAgent.Indexing.csproj       # Remove NuGetAuditMode=direct
src/DocAgent.McpServer.csproj      # Remove NuGetAuditMode=direct
```

### Pattern: Centralized NuGetAudit Configuration

**What:** Place all NuGetAudit configuration in `src/Directory.Build.props` (already used for TFM, LangVersion, TreatWarningsAsErrors).
**Why:** Centralizes audit policy. Per-project `NuGetAuditMode=direct` overrides were workarounds for transitive vulnerabilities from Roslyn MSBuild deps. After upgrade, evaluate if they are still needed, then handle centrally.

**Recommended Directory.Build.props additions:**
```xml
<PropertyGroup>
  <!-- NuGet security audit on restore -->
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>direct</NuGetAuditMode>
</PropertyGroup>
```

**Why `direct` not `all`:** .NET 10 defaults NuGetAuditMode to `all`, which would surface transitive vulnerabilities from the Microsoft.Build.Tasks.Core chain brought in by Workspaces.MSBuild. These are not actionable (the Roslyn team pins this dependency). Using `direct` audits only packages the project directly references, which is what the current per-project overrides already do. Alternatively, use `all` with `NuGetAuditSuppress` items for specific advisories -- this is more transparent but requires identifying the exact advisory URLs.

### Pattern: NuGetAuditSuppress for Known Transitive Advisories

If using `NuGetAuditMode=all` (the .NET 10 default), suppress known non-actionable advisories centrally:

```xml
<ItemGroup>
  <!-- Microsoft.Build.Tasks.Core transitive via Workspaces.MSBuild - advisory applies
       only to MSBuild host process, not library consumers -->
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-XXXX" />
</ItemGroup>
```

**Recommendation:** Use `NuGetAuditMode=direct` for simplicity. This aligns with the existing project pattern and the success criteria ("dotnet restore reports no known vulnerabilities"). If the team later wants full transitive auditing, they can switch to `all` + suppress.

### Anti-Patterns to Avoid

- **Per-project NuGetAuditMode overrides:** The current pattern of `NuGetAuditMode=direct` in 4 separate csproj files is exactly what this phase should clean up. Centralize in Directory.Build.props.
- **NoWarn for NuGet version warnings:** `NoWarn>NU1608;NU1107;NU1605` in DocAgent.Tests.csproj masks real problems. After the Roslyn bump unifies versions, these suppressions should be removed.
- **TreatWarningsAsErrors=false on test project:** This was needed because the NoWarn approach was insufficient for NU1107. After cleanup, the test project should inherit the global `TreatWarningsAsErrors=true`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Vulnerability auditing | Custom scripts to check CVEs | NuGetAudit MSBuild properties | Built into .NET SDK, runs on every restore, integrates with GitHub Advisory Database |
| Version conflict resolution | Manual dependency graph analysis | `dotnet nuget why <package>` command | Shows transitive dependency chains causing conflicts |
| Advisory suppression | NoWarn on NU1903/NU1904 codes | `NuGetAuditSuppress` items with advisory URLs | Granular per-advisory suppression with traceability |
| Audit baseline recording | Snapshot file of package versions | Clean `dotnet restore` output + `dotnet list package --vulnerable` | Standard tooling, reproducible |

## Common Pitfalls

### Pitfall 1: NU1507 Conflated with Phase Scope

**What goes wrong:** The developer sees `dotnet restore` failing with NU1507 (multiple package sources without source mapping) and tries to fix it as part of this phase.
**Why it happens:** NU1507 is a pre-existing issue from the global NuGet configuration having both nuget.org and QHubPackages sources. CPM requires source mapping when multiple sources exist.
**How to avoid:** NU1507 is OUT OF SCOPE. The success criteria specify "zero NU1107 warnings" not "zero restore errors." The NU1507 issue exists on the main branch today and predates this phase. If it blocks testing, temporarily add `<NoWarn>$(NoWarn);NU1507</NoWarn>` in Directory.Build.props or create a repo-level nuget.config with package source mapping. But do not let this scope-creep the phase.
**Warning signs:** Restore failures mentioning "package source mapping" or "NU1507."

### Pitfall 2: Analyzer Project Targeting netstandard2.0

**What goes wrong:** The Analyzers project targets `netstandard2.0` (required for Roslyn analyzers). Roslyn 4.14.0 packages target netstandard2.0 as well, so this should work, but the dependency chain for System.Collections.Immutable 9.0.0 may cause issues on netstandard2.0.
**Why it happens:** Analyzers must run inside the compiler process, which mandates netstandard2.0.
**How to avoid:** After bumping versions, verify `dotnet build src/DocAgent.Analyzers/DocAgent.Analyzers.csproj` succeeds. Microsoft.CodeAnalysis.CSharp 4.14.0 has explicit netstandard2.0 support with all necessary shims.
**Warning signs:** Build errors about missing types from System.Collections.Immutable or version conflicts in the Analyzers project.

### Pitfall 3: BenchmarkDotNet Roslyn Version Alignment

**What goes wrong:** BenchmarkDotNet 0.15.8 transitively pulls Microsoft.CodeAnalysis.Common 4.14.0. This was the CAUSE of the original NU1107 conflict (BDN wanted 4.14.0, production projects pinned 4.12.0). After the upgrade, BDN and production align at 4.14.0, resolving the conflict.
**Why it happens:** BDN uses Roslyn for disassembly/code analysis features.
**How to avoid:** After upgrading to 4.14.0, the BDN conflict disappears. The Benchmarks project NoWarn for NU1608 should be removable. Verify by restoring DocAgent.Benchmarks.csproj without NoWarn entries.
**Warning signs:** If NU1608 persists after upgrade, BDN may have updated to require an even newer Roslyn version.

### Pitfall 4: Golden File Diffs from Verify.Xunit

**What goes wrong:** Roslyn version bumps can change the exact string representation of syntax nodes, diagnostics, or symbol metadata. Verify.Xunit golden files (.verified.txt) may need updating.
**Why it happens:** Minor Roslyn version changes sometimes alter diagnostic messages, formatting, or metadata properties.
**How to avoid:** After the upgrade, run `dotnet test` and check if any Verify tests fail. If failures are cosmetic (whitespace, version strings), approve the new snapshots. If semantic (different diagnostic IDs, missing information), investigate.
**Warning signs:** Test failures mentioning "Received file does not match Verified file."

### Pitfall 5: NuGetAudit with TreatWarningsAsErrors

**What goes wrong:** Enabling NuGetAudit with TreatWarningsAsErrors=true causes restore to fail if ANY vulnerability exists, even non-actionable transitive ones.
**Why it happens:** NuGetAudit warnings (NU1901-NU1904) become errors under TreatWarningsAsErrors.
**How to avoid:** Either use `NuGetAuditMode=direct` (skip transitive) or use `NuGetAuditSuppress` for specific advisories. Do NOT use `WarningsNotAsErrors` for NU190x codes -- that defeats the purpose of the audit.
**Warning signs:** Restore fails after enabling NuGetAudit with NU1903 errors about Microsoft.Build.Tasks.Core.

## Code Examples

### Example 1: Directory.Packages.props Version Bump

```xml
<!-- Before -->
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.12.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.12.0" />

<!-- After -->
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.14.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.14.0" />
```

### Example 2: DocAgent.Tests.csproj Cleanup

```xml
<!-- REMOVE these lines -->
<NoWarn>$(NoWarn);NU1608;NU1107;NU1605</NoWarn>
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>

<!-- REMOVE this PackageReference -->
<PackageReference Include="Microsoft.CodeAnalysis.Common" VersionOverride="4.14.0" />
```

### Example 3: NuGetAudit in Directory.Build.props

```xml
<!-- Source: https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages -->
<PropertyGroup>
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>direct</NuGetAuditMode>
</PropertyGroup>
```

### Example 4: Audit Baseline Recording

```bash
# Record audit baseline after all changes
dotnet list src/DocAgentFramework.sln package --vulnerable --include-transitive > audit-baseline.txt

# Verify clean restore across all projects
dotnet restore src/DocAgentFramework.sln
dotnet restore tests/DocAgent.Benchmarks/DocAgent.Benchmarks.csproj
dotnet restore tests/DocAgent.Tests/DocAgent.Tests.csproj
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-project NuGetAuditMode=direct | Centralized in Directory.Build.props | .NET 8+ | Single audit policy for all projects |
| NoWarn NU1608/NU1107 | Unified package versions | Always best practice | Clean restore without suppressions |
| VersionOverride for conflicts | Align all packages to same version | N/A | CPM works as designed |
| Framework-specific analyzer testing (.XUnit suffix) | Generic testing packages (1.1.3+) | 2025 | Decoupled from xUnit version; DefaultVerifier pattern |

**Deprecated/outdated:**
- `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`: Marked deprecated on NuGet. Successor is `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` (1.1.3). However, since no analyzer test code exists yet, this migration is not urgent for Phase 23.

## Open Questions

1. **NU1507 pre-existing restore failure**
   - What we know: Global NuGet config has multiple sources (nuget.org + QHubPackages). CPM requires source mapping or single source.
   - What's unclear: Whether this blocks the phase's success criteria validation (can't run `dotnet restore` to verify zero NU1107 if NU1507 fails first).
   - Recommendation: Add `<NoWarn>$(NoWarn);NU1507</NoWarn>` in Directory.Build.props as a separate concern, or create a repo-level `nuget.config` with `<packageSourceMapping>`. This should be handled as a prerequisite fix, not part of the core phase work.

2. **Exact transitive vulnerability advisories after upgrade**
   - What we know: Workspaces.MSBuild 4.14.0 depends on Microsoft.Build >= 17.7.2 (for net8.0/net9.0 targets) or >= 17.13.9 (for netfx). Known vulnerabilities exist in Microsoft.Build.Tasks.Core.
   - What's unclear: The exact advisory URLs that will surface after upgrading. These can only be determined by running restore with NuGetAuditMode=all.
   - Recommendation: Run restore with `NuGetAuditMode=all` first, capture advisory URLs, then add `NuGetAuditSuppress` items or switch to `NuGetAuditMode=direct`.

3. **Benchmarks project NoWarn cleanup**
   - What we know: DocAgent.Benchmarks has `NoWarn>NU1608;NU1903`. NU1608 was for version constraint warnings from BDN's Roslyn deps. NU1903 was for high-severity transitive vulnerability.
   - What's unclear: Whether NU1903 is still needed after the Roslyn bump (the vulnerability may be in a different transitive chain).
   - Recommendation: Remove NoWarn entries, test restore, add back only what is still needed.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| Config file | src/Directory.Build.props (shared build props) |
| Quick run command | `dotnet test tests/DocAgent.Tests` |
| Full suite command | `dotnet test` |

### Phase Requirements -> Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PKG-01 | Roslyn packages at 4.14.0, no VersionOverride | smoke | `dotnet restore src/DocAgentFramework.sln && dotnet build src/DocAgentFramework.sln` | N/A (build verification) |
| PKG-01 | No NU1107 warnings across all projects | smoke | `dotnet restore src/DocAgentFramework.sln 2>&1 \| grep -c NU1107` (expect 0) | N/A (restore verification) |
| PKG-02 | NuGetAudit enabled, no known vulnerabilities | smoke | `dotnet restore src/DocAgentFramework.sln` (no NU190x errors) | N/A (restore verification) |
| PKG-02 | All existing tests still pass after upgrade | regression | `dotnet test` | Existing test suite |

### Sampling Rate

- **Per task commit:** `dotnet restore src/DocAgentFramework.sln && dotnet build src/DocAgentFramework.sln`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full test suite green + zero NU1107 + NuGetAudit clean

### Wave 0 Gaps

None -- existing test infrastructure covers all phase requirements. This phase is build configuration, not feature code. Validation is via restore/build/test commands, not new test files.

## Sources

### Primary (HIGH confidence)
- [NuGet Gallery: Microsoft.CodeAnalysis.CSharp 4.14.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.14.0) - Version exists, published May 15, 2025
- [NuGet Gallery: Microsoft.CodeAnalysis.Workspaces.MSBuild 4.14.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/4.14.0) - Version exists, dependencies verified
- [NuGet Gallery: Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit) - Dependency on CSharp.Workspaces >= 1.0.1 (forward-compatible)
- [Microsoft Learn: Auditing package dependencies](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) - NuGetAudit configuration properties
- [NuGet Audit 2.0 Blog Post](https://devblogs.microsoft.com/dotnet/nugetaudit-2-0-elevating-security-and-trust-in-package-management/) - NuGetAuditSuppress feature

### Secondary (MEDIUM confidence)
- [dotnet/roslyn-sdk README](https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md) - Analyzer testing migration to generic packages
- [NuGet Gallery: Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.3](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Analyzer.Testing/) - Successor to deprecated .XUnit package

### Tertiary (LOW confidence)
- Transitive vulnerability details from Microsoft.Build.Tasks.Core -- exact advisory URLs need to be determined by running restore with NuGetAuditMode=all after upgrade

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All package versions confirmed on NuGet with publication dates
- Architecture: HIGH - NuGetAudit MSBuild properties are well-documented by Microsoft
- Pitfalls: HIGH - Based on direct inspection of csproj files and known Roslyn upgrade patterns

**Research date:** 2026-03-06
**Valid until:** 2026-04-06 (stable domain -- NuGet packaging conventions change slowly)
