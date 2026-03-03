# Phase 20: MSBuild Performance Benchmarks - Research

**Researched:** 2026-03-03
**Domain:** BenchmarkDotNet, .NET performance measurement, regression guard testing
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Benchmark output & reporting**
- Console table output for human readability + JSON file artifact for programmatic comparison
- Use BenchmarkDotNet as the benchmark framework (industry standard, handles warm-up, stats, memory diagnostics)
- Baselines stored as a checked-in JSON file committed to the repo
- Baselines updated manually only — developer must review improvement before accepting new values

**Threshold & regression policy**
- Percentage-based thresholds as primary check + absolute ceiling as hard cap (belt and suspenders)
- Exceeding threshold causes hard test failure — forces investigation before merge
- 20% tolerance for latency regression (balances variance vs catching real regressions)
- Same 20% tolerance for memory — keep it simple with one percentage for both

**Benchmark scope & scenarios**
- Reuse existing test fixtures from the test suite — no dedicated benchmark fixture
- Scenarios measured: per-project compilation latency, full solution ingestion, incremental re-ingestion, cold vs warm workspace
- Memory measured via BenchmarkDotNet MemoryDiagnoser (allocations, Gen0/1/2 collections)
- Iteration count: BenchmarkDotNet auto-tuned defaults for statistical rigor

**CI integration & triggers**
- Benchmarks run on-demand only (`dotnet test --filter Benchmark`), not on every build
- Regression guard tests tagged with `[Trait("Category", "Benchmark")]` — separate from normal test suite
- Generous percentage tolerance handles CI environment variance — no per-environment baselines
- No historical persistence beyond console + JSON output — keep it simple for a scaffold project

### Claude's Discretion

- BenchmarkDotNet configuration details (Job config, exporters)
- Exact JSON schema for baselines file
- How regression guard test loads and compares against baselines
- Absolute ceiling values for hard caps (determine from initial baseline runs)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| PERF-01 | MSBuild workspace open and per-project compilation latency is measured and baselined | BenchmarkDotNet [Benchmark] methods on SolutionIngestionService with InProcess toolchain; `Summary.Reports` → `ResultStatistics.Mean` (nanoseconds) → baseline JSON |
| PERF-02 | Memory high-water mark during solution ingestion is measured and baselined | `[MemoryDiagnoser]` attribute captures `AllocatedBytes` per operation; `GcStats.GetBytesAllocatedPerOperation()` API gives bytes; baseline JSON records this alongside latency |
| PERF-03 | Regression guard test asserts ingestion stays under defined thresholds | Separate xUnit `[Fact]` test with `[Trait("Category", "Benchmark")]` loads baseline JSON, runs BenchmarkDotNet via `BenchmarkRunner.Run`, reads `Summary.Reports`, asserts `Mean < baseline * 1.20` and `Mean < absoluteCeiling` |
</phase_requirements>

---

## Summary

BenchmarkDotNet 0.15.8 (latest stable as of 2025-11-30) is the standard tool for this phase and is fully compatible with .NET 10. The library handles statistical rigor automatically: auto-tuned warmup and iteration counts, outlier detection, confidence intervals, and GC collection metrics via `[MemoryDiagnoser]`. For MSBuild-heavy benchmarks that take seconds per iteration (not nanoseconds), BenchmarkDotNet auto-tunes to fewer iterations while still computing valid statistics.

The architecture for this phase has two distinct parts. First, a benchmark executable project (`DocAgent.Benchmarks`) that uses `BenchmarkRunner.Run<T>()` to produce console table output and a JSON artifact in `BenchmarkDotNet.Artifacts/`. Second, regression guard tests live in the existing `DocAgent.Tests` project, tagged `[Trait("Category", "Benchmark")]`, so they are excluded from normal `dotnet test` but runnable on demand via `dotnet test --filter "Category=Benchmark"`. The regression guard reads the checked-in baseline JSON, runs a quick benchmark pass, and asserts the results stay within 20% of the baseline plus an absolute ceiling.

A critical constraint for MSBuildWorkspace benchmarks: `[GlobalSetup]` for workspace-open scenarios must open and cache the workspace before the `[Benchmark]` method runs, so the benchmark only measures compilation latency (not workspace open + compile). Cold vs warm distinctions are handled by having two benchmark methods — one that opens workspace fresh each iteration (`IterationSetup`) and one that reuses a warm workspace.

**Primary recommendation:** Add a `tests/DocAgent.Benchmarks` console project with BenchmarkDotNet; write regression guard tests inside `DocAgent.Tests` tagged `[Trait("Category", "Benchmark")]`; store the baseline file at `tests/DocAgent.Benchmarks/baselines.json`.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| BenchmarkDotNet | 0.15.8 | Benchmark harness, stats, memory diagnostics | Industry standard for .NET; used by dotnet/runtime itself; handles warmup, outliers, GC stats automatically |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| BenchmarkDotNet (MemoryDiagnoser) | built-in | Capture allocated bytes + Gen0/1/2 GC collections per operation | Always — add `[MemoryDiagnoser]` to benchmark class |
| System.Text.Json | built-in .NET 10 | Read/write baseline JSON file | For the regression guard test and baseline updater |
| xunit (existing) | 2.9.3 | Host regression guard `[Fact]` tests | Existing test framework — no new package needed |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| BenchmarkDotNet | Stopwatch + manual stats | Loses statistical rigor (no outlier detection, no warm-up, no GC stats) — do not use |
| Separate benchmark csproj | BDN inside DocAgent.Tests | BDN requires Release build; mixing with debug-built test project causes incorrect measurements |
| `[MemoryDiagnoser]` | `GC.GetTotalMemory()` manual | Manual approach misses per-operation allocation; BDN MemoryDiagnoser is 99.5% accurate |

**Installation (new project):**

```bash
dotnet new console -n DocAgent.Benchmarks -o tests/DocAgent.Benchmarks --framework net10.0
dotnet add tests/DocAgent.Benchmarks package BenchmarkDotNet --version 0.15.8
dotnet sln src/DocAgentFramework.sln add tests/DocAgent.Benchmarks/DocAgent.Benchmarks.csproj
```

Add to `Directory.Packages.props`:
```xml
<PackageVersion Include="BenchmarkDotNet" Version="0.15.8" />
```

---

## Architecture Patterns

### Recommended Project Structure

```
tests/
├── DocAgent.Benchmarks/          # BenchmarkDotNet harness (console app)
│   ├── DocAgent.Benchmarks.csproj
│   ├── Program.cs                # BenchmarkSwitcher entry point
│   ├── SolutionIngestionBenchmarks.cs  # [Benchmark] methods
│   └── baselines.json            # Checked-in baseline values
└── DocAgent.Tests/
    └── Performance/
        └── RegressionGuardTests.cs  # [Trait("Category", "Benchmark")] xUnit tests
```

### Pattern 1: Benchmark Class Structure

**What:** BenchmarkDotNet benchmark class with `[MemoryDiagnoser]`, `[GlobalSetup]` for workspace open, and separate `[Benchmark]` methods for compilation scenarios.

**When to use:** All benchmark methods in `DocAgent.Benchmarks`.

**Example:**
```csharp
// Source: https://benchmarkdotnet.org/articles/configs/diagnosers.html
[MemoryDiagnoser]
[JsonExporter]
public class SolutionIngestionBenchmarks
{
    private string _slnPath = null!;
    private SolutionIngestionService _service = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Uses the repo's own solution as a real fixture — no dedicated fixture needed
        _slnPath = Path.GetFullPath("../../../../../src/DocAgentFramework.sln");
        _store = new SnapshotStore(Path.Combine(Path.GetTempPath(), $"bdn-{Guid.NewGuid():N}"));
        _service = new SolutionIngestionService(_store, new InMemorySearchIndex(),
            NullLogger<SolutionIngestionService>.Instance);
    }

    [Benchmark(Description = "FullSolutionIngestion")]
    public async Task FullIngestion()
    {
        await _service.IngestAsync(_slnPath, null, CancellationToken.None);
    }

    [Benchmark(Description = "PerProjectCompilation")]
    public async Task SingleProjectCompilation()
    {
        // Compile one known project for focused latency measurement
        // Workspace open is in GlobalSetup — benchmark only the compile step
    }

    [GlobalCleanup]
    public void Cleanup() { /* delete temp store */ }
}
```

### Pattern 2: JSON Exporter Configuration

**What:** Add `JsonExporter.Full` to the config or use `[JsonExporter]` attribute to produce artifact file at `BenchmarkDotNet.Artifacts/results/`.

**Example:**
```csharp
// Source: https://benchmarkdotnet.org/articles/configs/exporters.html
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddExporter(JsonExporter.Full)         // writes *.json to Artifacts/results/
    .AddDiagnoser(MemoryDiagnoser.Default)  // AllocatedBytes, Gen0/1/2
    .WithArtifactsPath("BenchmarkDotNet.Artifacts");

BenchmarkRunner.Run<SolutionIngestionBenchmarks>(config);
```

Or via attribute:
```csharp
[MemoryDiagnoser]
[JsonExporter]   // produces brief JSON (excludes raw measurements, keeps summary)
public class SolutionIngestionBenchmarks { ... }
```

### Pattern 3: Baseline JSON Schema

**What:** A hand-crafted JSON file checked into the repo that stores the reference values produced on a first baseline run. The regression guard reads this, not BDN artifacts.

**Recommended schema (Claude's discretion):**
```json
{
  "capturedAt": "2026-03-03T00:00:00Z",
  "capturedBy": "developer-name",
  "thresholds": {
    "tolerancePercent": 20
  },
  "baselines": [
    {
      "benchmarkName": "FullSolutionIngestion",
      "meanNanoseconds": 12500000000,
      "allocatedBytes": 52428800,
      "absoluteCeilingNanoseconds": 60000000000,
      "absoluteCeilingAllocatedBytes": 209715200
    },
    {
      "benchmarkName": "PerProjectCompilation",
      "meanNanoseconds": 3500000000,
      "allocatedBytes": 15728640,
      "absoluteCeilingNanoseconds": 30000000000,
      "absoluteCeilingAllocatedBytes": 104857600
    }
  ]
}
```

**Absolute ceiling values:** Leave as `null` in the initial commit; populate after the first real run. The regression guard skips ceiling check when null.

### Pattern 4: Regression Guard Test

**What:** xUnit `[Fact]` tagged `[Trait("Category", "Benchmark")]` that runs BenchmarkDotNet programmatically, reads `Summary.Reports`, and asserts against the baseline JSON.

**When to use:** The single regression guard test in `DocAgent.Tests/Performance/RegressionGuardTests.cs`.

**Example:**
```csharp
// Source: https://code-maze.com/how-to-integrate-benchmarkdotnet-with-unit-tests/
[Trait("Category", "Benchmark")]
public class RegressionGuardTests
{
    [Fact]
    public async Task SolutionIngestion_DoesNotRegressBeyondBaseline()
    {
        // Load baseline
        var baselinePath = Path.GetFullPath(
            "../../../../../tests/DocAgent.Benchmarks/baselines.json");
        var baselines = JsonSerializer.Deserialize<BaselineFile>(
            await File.ReadAllTextAsync(baselinePath))!;

        // Run benchmarks
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)  // allow Debug in test context
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(Job.ShortRun);  // fewer iterations for CI speed

        var summary = BenchmarkRunner.Run<SolutionIngestionBenchmarks>(config);

        // Assert per-benchmark
        foreach (var baseline in baselines.Baselines)
        {
            var report = summary.Reports.FirstOrDefault(r =>
                r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo == baseline.BenchmarkName);
            Assert.NotNull(report);

            var stats = report.ResultStatistics!;
            var tolerance = 1.0 + (baselines.Thresholds.TolerancePercent / 100.0);

            // Latency regression check
            Assert.True(stats.Mean <= baseline.MeanNanoseconds * tolerance,
                $"{baseline.BenchmarkName}: mean {stats.Mean:N0} ns exceeds baseline " +
                $"{baseline.MeanNanoseconds:N0} ns * {tolerance}");

            // Absolute ceiling check (skip if ceiling is null/unset)
            if (baseline.AbsoluteCeilingNanoseconds.HasValue)
                Assert.True(stats.Mean <= baseline.AbsoluteCeilingNanoseconds.Value,
                    $"{baseline.BenchmarkName}: mean {stats.Mean:N0} ns exceeds hard ceiling " +
                    $"{baseline.AbsoluteCeilingNanoseconds.Value:N0} ns");

            // Memory regression check
            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
            Assert.True(allocated <= baseline.AllocatedBytes * tolerance,
                $"{baseline.BenchmarkName}: allocated {allocated:N0} bytes exceeds baseline " +
                $"{baseline.AllocatedBytes:N0} * {tolerance}");
        }
    }
}
```

### Pattern 5: Program.cs Entry Point

**What:** Console entry point using `BenchmarkSwitcher` so developers can run any subset interactively.

```csharp
// tests/DocAgent.Benchmarks/Program.cs
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

Run all: `dotnet run -c Release --project tests/DocAgent.Benchmarks`
Run specific: `dotnet run -c Release --project tests/DocAgent.Benchmarks -- --filter *FullIngestion*`

### Anti-Patterns to Avoid

- **MSBuildWorkspace in [Benchmark] body:** Opening `MSBuildWorkspace.Create()` inside the benchmark method measures workspace discovery overhead, not compilation. Always open workspace in `[GlobalSetup]` and cache it; use `[IterationSetup]` only for "cold" scenarios where you intentionally want to include open latency.
- **Running BenchmarkDotNet in Debug mode without `DisableOptimizationsValidator`:** BDN will throw an exception. The regression guard test must either use `DisableOptimizationsValidator` or configure `Job.InProcess` with explicit settings.
- **Putting benchmark `.csproj` in `src/`:** Benchmark projects should live in `tests/` — they reference production code and generate artifacts.
- **Committing BDN artifact output directory:** `BenchmarkDotNet.Artifacts/` should be in `.gitignore`; only `baselines.json` is committed.
- **Forgetting `[GlobalCleanup]`:** Temp snapshot stores created in `[GlobalSetup]` will accumulate on disk without cleanup.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Statistical warm-up | Manual loop with Stopwatch | BDN auto-tuned job | Warm-up count is not fixed — BDN detects when variance stabilizes |
| Memory measurement | `GC.GetTotalMemory()` before/after | `[MemoryDiagnoser]` | GC.GetTotalMemory is process-wide; BDN measures per-operation allocation with 99.5% accuracy |
| Outlier removal | Custom percentile trimming | BDN default outlier detector | BDN uses Tukey fences; hand-rolled trimming introduces bias |
| JSON result output | Custom serialization of Stopwatch | `[JsonExporter]` or `JsonExporter.Full` | BDN JSON schema is stable and complete |

**Key insight:** BDN's value is that it eliminates the dozen subtle mistakes in manual benchmarking (JIT warm-up, GC pressure during measurement, CPU affinity, etc.). Use it as a black box for measurement; only apply custom config when absolutely necessary.

---

## Common Pitfalls

### Pitfall 1: BDN Requires Release Build

**What goes wrong:** `BenchmarkRunner.Run<T>()` throws or warns that optimizations are disabled. The regression guard test in `DocAgent.Tests` normally runs in Debug via `dotnet test`.

**Why it happens:** BDN validates that the assembly is compiled with optimizations. Debug builds produce artificially slow results.

**How to avoid:** In the regression guard test's `ManualConfig`, call `.WithOptions(ConfigOptions.DisableOptimizationsValidator)`. This allows measurements from Debug but reduces accuracy — acceptable for a regression guard that uses a generous 20% tolerance. Alternatively, run the regression guard only in Release: `dotnet test -c Release --filter "Category=Benchmark"`.

**Warning signs:** `BenchmarkDotNet.Running.BenchmarkRunner` throwing `InvalidBenchmarkDeclarationException` or printing a warning about optimizations.

### Pitfall 2: MSBuildWorkspace Requires MSBuild Toolset at Runtime

**What goes wrong:** `MSBuildWorkspace.Create()` throws `MSB1003` or silently produces 0 projects because the .NET SDK isn't locatable from the test host process.

**Why it happens:** The benchmark process must have the .NET SDK on `PATH`. When run via `dotnet test`, the host process inherits the shell's PATH — usually fine on developer machines. CI agents may lack SDK tools.

**How to avoid:** Check `workspace.Diagnostics` or subscribe to `workspace.WorkspaceFailed` in `[GlobalSetup]`; skip the benchmark with `Skip.If(...)` if workspace opens 0 projects.

**Warning signs:** `TotalProjectCount == 0` in ingestion result during benchmark setup.

### Pitfall 3: Async Benchmark Methods with InProcess Toolchain

**What goes wrong:** `[GlobalSetup]` with `async Task` is not awaited when BDN uses the InProcess toolchain.

**Why it happens:** GitHub issue #1738 — InProcess toolchain invokes async setup without awaiting the result.

**How to avoid:** In `[GlobalSetup]`, call `.GetAwaiter().GetResult()` on async operations, or use the standard (out-of-process) toolchain (the default). The standard toolchain spawns a separate process for each job, which correctly handles async.

**Warning signs:** Benchmark hangs or workspace is null during benchmark execution.

### Pitfall 4: Baseline JSON Path Portability

**What goes wrong:** Regression guard test uses a hard-coded absolute path to `baselines.json`; fails on other developer machines or CI agents.

**Why it happens:** Test output directory differs from source tree.

**How to avoid:** Compute the path relative to the test assembly location using `Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/DocAgent.Benchmarks/baselines.json"))`. Alternatively, embed `baselines.json` as an embedded resource in `DocAgent.Tests`.

**Warning signs:** `FileNotFoundException` for baselines.json.

### Pitfall 5: BDN Artifact Output Committed to Git

**What goes wrong:** Developers accidentally `git add` the `BenchmarkDotNet.Artifacts/` directory, polluting the repo with machine-specific measurement files.

**Why it happens:** The artifacts directory is created in the working directory by default.

**How to avoid:** Add `BenchmarkDotNet.Artifacts/` to `.gitignore` before the first benchmark run.

---

## Code Examples

Verified patterns from official sources:

### BenchmarkDotNet MemoryDiagnoser — Reading Allocated Bytes

```csharp
// Source: https://benchmarkdotnet.org/articles/configs/diagnosers.html
// GcStats.GetBytesAllocatedPerOperation returns long
long allocatedBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
long gen0Collections = report.GcStats.Gen0Collections;
long gen1Collections = report.GcStats.Gen1Collections;
```

### BenchmarkDotNet Summary — Reading Mean from Reports

```csharp
// Source: https://code-maze.com/how-to-integrate-benchmarkdotnet-with-unit-tests/ (March 2024)
var report = summary.Reports.First(r =>
    r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo == "MethodName");
double meanNs = report.ResultStatistics!.Mean;  // always nanoseconds
double p95Ns  = report.ResultStatistics!.Percentiles.P95;
```

### ManualConfig with ShortRun for Regression Guard Speed

```csharp
// Source: BenchmarkDotNet docs — Job presets
// Job.ShortRun: 1 launch, 3 warmup, 5 target iterations — fast but lower statistical confidence
// Acceptable for regression guard with 20% tolerance
var config = ManualConfig.Create(DefaultConfig.Instance)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddJob(Job.ShortRun)
    .AddExporter(JsonExporter.Brief);
```

### xUnit Trait-Based Filtering

```bash
# Run only Benchmark-tagged tests (regression guards) — on demand
dotnet test tests/DocAgent.Tests --filter "Category=Benchmark"

# Run all non-benchmark tests (normal CI gate)
dotnet test tests/DocAgent.Tests --filter "Category!=Benchmark"

# Run full benchmark suite directly (not via dotnet test)
dotnet run -c Release --project tests/DocAgent.Benchmarks
```

### Baseline Updater Pattern

The developer runs the full benchmark, then updates baselines manually:

```bash
# 1. Run benchmarks to produce artifact
dotnet run -c Release --project tests/DocAgent.Benchmarks -- --exporters json

# 2. Review output in BenchmarkDotNet.Artifacts/results/*.json
# 3. Manually copy Mean and AllocatedBytes values into tests/DocAgent.Benchmarks/baselines.json
# 4. Commit with explanation: "perf: update baselines after optimization X"
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| BDN 0.13.x | BDN 0.15.x | ~2024 | `[MemoryDiagnoser]` is no longer enabled by default; must be explicit |
| `Job.Default` auto-tunes everything | `Job.ShortRun` available for fast passes | Long-standing | ShortRun (3 warmup, 5 target) is ideal for CI regression guards with slow benchmarks |
| Separate `ManualConfig` class | `.WithOptions()` fluent API on `ManualConfig.Create()` | 0.14+ | Less boilerplate |

**Deprecated/outdated:**
- `Add(new MemoryDiagnoser())` (old API): replaced by `AddDiagnoser(MemoryDiagnoser.Default)` — same effect, cleaner.
- `BenchmarkDotNet.Core` package: merged into `BenchmarkDotNet` — use the main package only.

---

## Open Questions

1. **Absolute ceiling values for hard caps**
   - What we know: User decided to use absolute ceilings as hard caps, but values must come from initial baseline run
   - What's unclear: Initial values unknown until benchmarks are run on real hardware; MSBuildWorkspace for a 5-project solution likely takes 5-30 seconds for cold open
   - Recommendation: Populate `absoluteCeilingNanoseconds` and `absoluteCeilingAllocatedBytes` as `null` in the initial baseline JSON; regression guard skips ceiling check when null. Developer fills them in after first run.

2. **Cold vs warm workspace benchmark isolation**
   - What we know: User requested cold vs warm workspace as a measured scenario
   - What's unclear: BDN standard toolchain spawns a new process per iteration, making every iteration "cold" for process-level state. True warm measurement requires `[IterationSetup]` to reuse a cached workspace within one process.
   - Recommendation: Use `[GlobalSetup]` for workspace open (one-time), `[Benchmark]` for compile-only (warm). Add a separate `[Benchmark]` method that recreates the workspace using `[IterationSetup]` for the cold scenario. Name clearly: `WarmCompilation` vs `ColdWorkspaceOpen`.

3. **Regression guard test project placement**
   - What we know: User wants benchmarks on-demand via `dotnet test --filter Benchmark`; project uses `[Trait("Category", "Benchmark")]` convention (confirmed from CrossProjectQueryTests.cs using same Trait pattern)
   - What's unclear: Whether regression guard `[Fact]` lives in `DocAgent.Tests` or a new project
   - Recommendation: Place regression guard in `DocAgent.Tests/Performance/` (no new project). The `[Trait("Category", "Benchmark")]` filter already excludes it from normal CI runs. Separate project adds complexity for no benefit in a scaffold project.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | none — inferred |
| Quick run command | `dotnet test tests/DocAgent.Tests --filter "Category=Benchmark" -c Release` |
| Full suite command | `dotnet run -c Release --project tests/DocAgent.Benchmarks` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PERF-01 | Latency measured and baselined | benchmark + manual | `dotnet run -c Release --project tests/DocAgent.Benchmarks` | ❌ Wave 0 |
| PERF-02 | Memory high-water mark measured and baselined | benchmark + manual | `dotnet run -c Release --project tests/DocAgent.Benchmarks` | ❌ Wave 0 |
| PERF-03 | Regression guard test fails if thresholds exceeded | unit (xUnit Fact) | `dotnet test tests/DocAgent.Tests --filter "Category=Benchmark" -c Release` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet build src/DocAgentFramework.sln` (build only — benchmarks are slow)
- **Per wave merge:** `dotnet test tests/DocAgent.Tests --filter "Category!=Benchmark"` (exclude benchmarks from normal gate)
- **Phase gate:** `dotnet test tests/DocAgent.Tests --filter "Category=Benchmark" -c Release` (run regression guard once before phase close)

### Wave 0 Gaps

- [ ] `tests/DocAgent.Benchmarks/DocAgent.Benchmarks.csproj` — new console project with BDN reference
- [ ] `tests/DocAgent.Benchmarks/Program.cs` — BenchmarkSwitcher entry point
- [ ] `tests/DocAgent.Benchmarks/SolutionIngestionBenchmarks.cs` — [Benchmark] methods (PERF-01, PERF-02)
- [ ] `tests/DocAgent.Benchmarks/baselines.json` — checked-in baseline file (initially null ceilings)
- [ ] `tests/DocAgent.Tests/Performance/RegressionGuardTests.cs` — [Trait("Category", "Benchmark")] [Fact] (PERF-03)
- [ ] `Directory.Packages.props` — add `BenchmarkDotNet` 0.15.8 package version
- [ ] `.gitignore` — add `BenchmarkDotNet.Artifacts/` entry

---

## Sources

### Primary (HIGH confidence)
- https://benchmarkdotnet.org/articles/configs/diagnosers.html — MemoryDiagnoser configuration, GcStats API
- https://benchmarkdotnet.org/articles/configs/exporters.html — JsonExporter, ArtifactsPath configuration
- https://benchmarkdotnet.org/articles/features/vstest.html — VSTest integration, dotnet test support
- https://www.nuget.org/packages/BenchmarkDotNet — version 0.15.8, .NET 10 support confirmed

### Secondary (MEDIUM confidence)
- https://code-maze.com/how-to-integrate-benchmarkdotnet-with-unit-tests/ (March 2024) — BenchmarkFixture + xUnit pattern, `Summary.Reports` + `ResultStatistics.Mean` usage
- https://jkrussell.dev/blog/benchmarkdotnet-dotnet-10-benchmarks/ (November 2025) — .NET 10 compatibility confirmed
- https://stackoverflow.com/questions/60828785/running-benchmarkdotnet-within-xunit — AccumulationLogger pattern, DisableOptimizationsValidator

### Tertiary (LOW confidence)
- https://github.com/dotnet/BenchmarkDotNet/issues/1738 — async GlobalSetup InProcess issue (unverified fix status)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — BDN 0.15.8 confirmed on NuGet; .NET 10 compatibility confirmed by November 2025 blog post and NuGet listing
- Architecture: HIGH — BenchmarkFixture + xUnit regression guard pattern verified in Code Maze March 2024 article; `[Trait("Category", "Benchmark")]` convention confirmed in this codebase
- Pitfalls: MEDIUM — Debug/Release pitfall and MSBuildWorkspace runtime requirements are known patterns; async GlobalSetup issue is LOW (issue status not re-verified)

**Research date:** 2026-03-03
**Valid until:** 2026-06-01 (BDN is stable; unlikely to change in 3 months)
