# Testing

xUnit + FluentAssertions test suite covering the full ingestion-to-query pipeline. See `CLAUDE.md` for the canonical build and test commands.

**490 total tests | 490 passing in standard `dotnet test` runs**

---

## Test Philosophy

- xUnit + FluentAssertions throughout; `TreatWarningsAsErrors` enforced
- No implicit network calls in tests — all fixtures are local
- Snapshots are deterministic: same input always produces identical `SymbolGraphSnapshot` output
- MSBuild-heavy tests use a `PipelineOverride` seam to run without a live workspace

---

## Running Tests

```bash
# All tests
dotnet test src/DocAgentFramework.sln

# Single test class
dotnet test --filter "FullyQualifiedName~XmlDocParserTests"

# Single category (example)
dotnet test --filter "FullyQualifiedName~IncrementalIngestion"

# Exclude environment-dependent tests (no trait defined — see Known Limitations)
# Run with awareness that 21 tests will fail in environments without MSBuild toolchain
```

---

## Test Categories

| Category | Approx Files | What It Validates | Example Test Class |
|----------|-------------|------------------|--------------------|
| XML parsing + ingestion | 3 | XML doc parsing, member binding, ingestion pipeline | `XmlDocParserTests`, `IngestionServiceTests`, `LocalProjectSourceTests` |
| Roslyn symbol graph | 2 | Symbol IDs, file spans, partial types, interface compilation | `RoslynSymbolGraphBuilderTests`, `InterfaceCompilationTests` |
| Search indexing | 3 | BM25 ranking, persistence, in-memory fallback | `BM25SearchIndexTests`, `BM25SearchIndexPersistenceTests`, `InMemorySearchIndexTests` |
| Snapshot serialization + store | 2 | MessagePack round-trips, store versioning | `SnapshotSerializationTests`, `SnapshotStoreTests` |
| Incremental ingestion | 7 | File manifest, skip-unchanged path, dependency cascade | `IncrementalIngestionEngineTests`, `SolutionIncrementalIngestionTests`, `DependencyCascadeTests` |
| Semantic diff | 8 | Signature diffs, nullability, constraints, accessibility | `SymbolGraphDifferTests`, `SignatureChangeTests`, `NullabilityChangeTests` |
| Change review | 2 | Severity grouping, unusual pattern detection | `ChangeReviewerTests`, `ChangeToolTests` |
| MCP tools + integration | 8 | Tool routing, request/response contracts | `McpToolTests`, `McpIntegrationTests`, `SolutionToolTests`, `IngestionToolTests` |
| Security | 3 | PathAllowlist enforcement, audit logging, prompt injection | `PathAllowlistTests`, `AuditLoggerTests`, `PromptInjectionScannerTests` |
| Solution-level | 5 | Cross-project edges, stub nodes, FQN disambiguation | `SolutionIngestionServiceTests`, `SolutionIngestionToolTests`, `SolutionGraphEnrichmentTests` |
| Roslyn analyzers | 3 | DocCoverage, DocParity, SuspiciousEdit diagnostics | `DocCoverageAnalyzerTests`, `DocParityAnalyzerTests`, `SuspiciousEditAnalyzerTests` |
| Performance / regression | 1 | Benchmark baseline regression guard | `RegressionGuardTests` |
| E2E + determinism | 4 | Full pipeline, snapshot determinism, cross-project queries | `DeterminismTests`, `E2EIntegrationTests`, `IngestAndQueryE2ETests`, `CrossProjectQueryTests` |
| Other | 1 | stdout contamination guard | `StdoutContaminationTests` |

---

## Fixture Patterns

**Golden files** — Expected snapshot outputs stored under `tests/Golden/`. Compared byte-for-byte after serialization to catch non-determinism regressions.

**PipelineOverride seam** — Tests that exercise ingestion without a live MSBuild workspace use a `PipelineOverride` injection point (mirrors the `IngestionService` pattern). Allows fast unit tests that bypass `MSBuildWorkspace.OpenSolutionAsync`.

**In-proc MCP server** — Tool integration tests instantiate the MCP server in-process. No stdio transport needed; tools are called directly via the tool host, validating routing and response contracts without process boundaries.

---

## Known Limitations

### MSBuildWorkspace Tests

Some tests exercise `MSBuildWorkspace.OpenSolutionAsync` to load real Roslyn workspaces. These require:
- .NET SDK with MSBuild toolchain installed at runtime
- `dotnet build` pre-warmed (MEF composition cache populated)

In environments where these conditions are not met (Docker images without SDK, CI runners without pre-build step), these tests fail with MEF composition errors. They are not code bugs — they are infrastructure constraints.

**Count:** Approximately 17 MSBuildWorkspace-dependent tests. These pass on a standard developer machine with .NET 10 SDK installed.

### RegressionGuardTests

`RegressionGuardTests` uses BenchmarkDotNet to compare performance against a stored baseline. It is gated behind the `RUN_BENCHMARKS` environment variable and skips silently during normal `dotnet test` runs. To run benchmarks explicitly:

```bash
RUN_BENCHMARKS=1 dotnet test --filter "Category=Benchmark" -c Release
```

### Test Parallelism and Environment Variables

Tests in `PathAllowlistTests` and `StartupValidatorTests` mutate the `DOCAGENT_ALLOWED_PATHS` environment variable. These classes are placed in a shared `[Collection("EnvVarMutating")]` to prevent parallel execution, avoiding flaky failures from env var pollution. The `ChangeToolTests` path-denied tests use an explicit deny allowlist that is immune to env var leakage.

**All 490 tests run clean on a standard developer machine with .NET 10 SDK installed.**

### v2.1 Optimised Code Paths

`SolutionIncrementalDeterminismTests` and `IncrementalIngestionEngineTests` now also exercise the v2.1 optimised code paths (`HashSet<SymbolId>` dedup, `ArrayPool`/`stackalloc` fingerprinting, O(1) edge filtering) and pass cleanly as part of the standard test run.
