# Stack Research

**Domain:** .NET code intelligence framework — housekeeping milestone
**Researched:** 2026-03-02
**Confidence:** HIGH

## Recommended Stack

### Core Technologies

No new core technologies needed. v1.3 is a housekeeping milestone — all work uses the existing stack.

| Technology | Version | Purpose | Status |
|------------|---------|---------|--------|
| .NET 10 | net10.0 | Runtime & framework | Already in use |
| Roslyn | 4.12.0 | Symbol analysis, MSBuildWorkspace | Already in use |
| BenchmarkDotNet | 0.14.0 | MSBuild performance benchmarking | **NEW — add for perf spike** |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| BenchmarkDotNet | 0.14.0 | Micro/macro benchmarks for MSBuild ingestion | Performance spike phase |
| System.Diagnostics.Process (BCL) | built-in | Memory measurement via Process.WorkingSet64 | Lightweight alternative if BenchmarkDotNet is overkill |

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| BenchmarkDotNet | Manual Stopwatch + GC.GetTotalMemory | If we only need a simple smoke test, not publishable benchmarks |
| Separate benchmark project | Inline test with [Fact] | If we want regression guards in CI vs. standalone reports |

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| dotnet-counters / dotTrace | Overkill for a spike test | BenchmarkDotNet or manual Stopwatch |
| New NuGet packages for docs tooling | Docs refresh is manual editing | Hand-edit markdown files |

---
*Stack research for: DocAgentFramework v1.3 Housekeeping*
*Researched: 2026-03-02*
