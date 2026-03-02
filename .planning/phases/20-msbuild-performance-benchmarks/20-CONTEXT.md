# Phase 20: MSBuild Performance Benchmarks - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Measure, baseline, and regression-guard MSBuild workspace open latency and solution ingestion memory usage. This phase delivers a benchmark suite and regression guard tests — not performance optimization itself.

</domain>

<decisions>
## Implementation Decisions

### Benchmark output & reporting
- Console table output for human readability + JSON file artifact for programmatic comparison
- Use BenchmarkDotNet as the benchmark framework (industry standard, handles warm-up, stats, memory diagnostics)
- Baselines stored as a checked-in JSON file committed to the repo
- Baselines updated manually only — developer must review improvement before accepting new values

### Threshold & regression policy
- Percentage-based thresholds as primary check + absolute ceiling as hard cap (belt and suspenders)
- Exceeding threshold causes hard test failure — forces investigation before merge
- 20% tolerance for latency regression (balances variance vs catching real regressions)
- Same 20% tolerance for memory — keep it simple with one percentage for both

### Benchmark scope & scenarios
- Reuse existing test fixtures from the test suite — no dedicated benchmark fixture
- Scenarios measured: per-project compilation latency, full solution ingestion, incremental re-ingestion, cold vs warm workspace
- Memory measured via BenchmarkDotNet MemoryDiagnoser (allocations, Gen0/1/2 collections)
- Iteration count: BenchmarkDotNet auto-tuned defaults for statistical rigor

### CI integration & triggers
- Benchmarks run on-demand only (`dotnet test --filter Benchmark`), not on every build
- Regression guard tests tagged with `[Trait("Category", "Benchmark")]` — separate from normal test suite
- Generous percentage tolerance handles CI environment variance — no per-environment baselines
- No historical persistence beyond console + JSON output — keep it simple for a scaffold project

### Claude's Discretion
- BenchmarkDotNet configuration details (Job config, exporters)
- Exact JSON schema for baselines file
- How regression guard test loads and compares against baselines
- Absolute ceiling values for hard caps (determine from initial baseline runs)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard BenchmarkDotNet approaches. Key constraint: benchmarks must not slow down normal `dotnet test` runs.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 20-msbuild-performance-benchmarks*
*Context gathered: 2026-03-02*
