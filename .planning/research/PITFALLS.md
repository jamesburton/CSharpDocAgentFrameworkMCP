# Pitfalls Research

**Domain:** .NET MCP server — v1.5 Robustness: dependency upgrade, API extension, performance hardening
**Researched:** 2026-03-04
**Confidence:** HIGH

---

## Critical Pitfalls

### Pitfall 1: Roslyn 4.14 Version Conflict in Central Package Management

**What goes wrong:**
`Microsoft.CodeAnalysis.CSharp` is pinned to 4.12.0 in `Directory.Packages.props`, but `BenchmarkDotNet` 0.15.8 pulls `Microsoft.CodeAnalysis.Common` 4.14.0 transitively. Tests already use `VersionOverride=4.14.0` on `Microsoft.CodeAnalysis.Common` to resolve NU1107. Upgrading the Roslyn CSharp/Workspaces packages to 4.14.0 WITHOUT updating every related `Microsoft.CodeAnalysis.*` entry (Common, CSharp, CSharp.Workspaces, Workspaces.MSBuild) to the same version will produce NU1107 errors again, or silently mix assemblies from different Roslyn versions.

**Why it happens:**
The Roslyn packages form a versioned family — all must move together. CPM hides the full transitive picture; developers update one entry and assume the others follow. The existing `VersionOverride` in DocAgent.Tests masks the real conflict until the upgrade exposes it in other projects.

**How to avoid:**
Update all five `Microsoft.CodeAnalysis.*` entries in `Directory.Packages.props` simultaneously to 4.14.0. Remove the `VersionOverride` from `DocAgent.Tests.csproj` once the root versions align. Verify with `dotnet restore --verbosity detailed` — watch for NU1107 on any CodeAnalysis package. Also check the analyzer-testing packages (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`, `Microsoft.CodeAnalysis.Testing.Verifiers.XUnit`) which have their own Roslyn floor — if they require <= 4.12.0, the upgrade cannot proceed without also upgrading them.

**Warning signs:**
- NU1107 "Two different compatible versions" warnings after partial update
- `dotnet build` succeeds but analyzer tests fail with `MissingMethodException` at runtime
- `DocAgent.Benchmarks` builds but crashes on first BDN iteration with `FileLoadException`

**Phase to address:**
Dependency upgrade phase — do this as the first task, before any feature work.

---

### Pitfall 2: Breaking Existing MCP Clients With Pagination

**What goes wrong:**
Adding `offset`/`limit` parameters to existing tools (`search_symbols`, `get_references`) changes the tool schema. MCP clients that hard-code tool calls with positional arguments, or that cache the tool manifest, will receive unexpected results or errors. If the unpaginated default behavior changes (e.g., result count caps at a new default limit), clients that relied on "return everything" semantics silently get truncated results.

**Why it happens:**
MCP tool schemas are typically consumed by agents that parse the tool description at startup. Adding optional parameters is nominally backward-compatible, but changing the default return count is a semantic breaking change even if the signature is still valid. The ModelContextProtocol SDK does not version tool schemas — there is no negotiation.

**How to avoid:**
Keep `limit` defaulting to a value at least as large as the current unbounded maximum result count (or `int.MaxValue` sentinel for "no limit"). Do not change the default behavior unless the current behavior is actively harmful. Add pagination metadata (`total_count`, `next_offset`) to the response envelope but only when `limit` is explicitly supplied — callers that omit `limit` get the same response shape as before.

**Warning signs:**
- Existing integration tests that assert on result count start failing after the change
- Test snapshots (Verify.Xunit) show response shape change for calls that did not supply `limit`

**Phase to address:**
Pagination phase — enforce backward-compat contract in the design decision before writing code.

---

### Pitfall 3: Rate Limiting a stdio MCP Server Has No Natural Enforcement Point

**What goes wrong:**
HTTP-based servers can apply rate limiting in middleware (ASP.NET Core `RateLimiter`, YARP, etc.). A stdio-based MCP server processes tool calls synchronously on the same process, with no HTTP layer. A naive implementation — a global static counter with a `SemaphoreSlim` — works in isolation but does not compose correctly with the cancellation tokens that tool methods already accept, and introduces shared mutable state that is not visible to callers.

**Why it happens:**
Rate limiting is naturally a cross-cutting concern for request/response infrastructure. Without an HTTP layer, there is no standard hook point. Developers reach for a static throttle but forget that: (a) the MCP SDK may pipeline multiple tool calls; (b) the rate limiter needs to reset on a wall-clock window, not per-process-lifetime; (c) it must not dead-lock when the MCP process is also awaiting its own tool dispatch.

**How to avoid:**
Use `System.Threading.RateLimiting.TokenBucketRateLimiter` or `FixedWindowRateLimiter` (built into .NET 7+, available in .NET 10). Add a rate-limiting wrapper service registered in DI, injected into tool classes alongside the existing `PathAllowlist`. Make it instance-scoped (not static) so tests can inject a no-op limiter. Honour the incoming `CancellationToken` in the `WaitAsync` call so the MCP SDK can cancel stalled tool calls. Return a structured error response (not an exception) when the rate limit is exceeded — the MCP SDK converts unhandled exceptions to opaque errors.

**Warning signs:**
- Rate limiter tests that pass in isolation but deadlock when run under `dotnet test --parallel`
- Tool calls during ingestion (which can run for minutes) triggering the rate limit mid-flight
- Static state leaking between test runs, causing intermittent failures

**Phase to address:**
Rate limiting phase — design the DI integration before writing the limiter logic.

---

### Pitfall 4: O(1) Symbol Lookup Refactor Breaks Determinism

**What goes wrong:**
Replacing linear `List<SymbolNode>` scans with `Dictionary<string, SymbolNode>` lookups is the right move, but `Dictionary` iteration order is not guaranteed in .NET. Any code path that builds a result by iterating the dictionary (e.g., `explain_project`, snapshot serialization) will produce non-deterministic output — violating the project's "same input → identical output" constraint and breaking `Verify.Xunit` golden-file tests.

**Why it happens:**
The existing `List<SymbolNode>` in `SymbolGraphSnapshot` has a defined iteration order (insertion order). Switching to `Dictionary` preserves O(1) lookup but loses ordering. If serialization or response building traverses the collection without an explicit `OrderBy`, output order varies between runs.

**How to avoid:**
When adding a dictionary for lookup, keep the existing list as the canonical ordered collection (or sort by a stable key — e.g., `FQN` — before any output operation). Alternatively, use `SortedDictionary<string, SymbolNode>` where the lookup AND iteration order are both defined. Never replace the list with a dictionary directly; the list remains the source of truth for serialization and deterministic output.

**Warning signs:**
- `Verify.Xunit` snapshot tests start failing with "received differs from verified" after the refactor
- `dotnet test` passes on first run but fails on second run against the same input (hash mismatch)
- `diff_snapshots` reports spurious changes when comparing two snapshots built from identical input

**Phase to address:**
Performance optimization phase — add explicit `OrderBy` or use `SortedDictionary` as part of the implementation, not as a post-hoc fix.

---

### Pitfall 5: `find_implementations` Crosses Project Boundaries and Hits Stub Nodes

**What goes wrong:**
`find_implementations` must traverse `SymbolEdge` relationships to find concrete types that implement an interface or override a base method. In a solution snapshot, stub nodes (synthesized for NuGet/BCL types) also participate in the edge graph. A naive traversal returns stub nodes as "implementations," producing results that reference non-existent source locations and confusing agents.

**Why it happens:**
Stub node filtering was built for the BM25 search index (index-time filter) but not for graph traversal queries. New tools that walk edges do not automatically inherit that filter. The `NodeKind.Stub` flag exists in `SymbolNode` but callers must explicitly check it.

**How to avoid:**
Add a shared helper (e.g., `SymbolGraphQueries.FindImplementations`) that always filters `NodeKind.Stub` nodes from results. Do not inline the traversal logic per-tool. Add a unit test that explicitly places a stub node in the implementation chain and asserts it is excluded from tool output.

**Warning signs:**
- `find_implementations` returns nodes with `SourceSpan.FilePath == null` or empty
- Agent clients attempt to navigate to stub node file paths and get "file not found"
- Tool returns more results than `get_references` for the same symbol

**Phase to address:**
`find_implementations` implementation phase — stub filtering must be in the initial design.

---

### Pitfall 6: Startup Config Validation Fails Silently in Aspire AppHost

**What goes wrong:**
Adding `IValidateOptions<DocAgentServerOptions>` or calling `services.AddOptions<T>().ValidateOnStart()` will throw at `IHost.StartAsync()` if configuration is invalid. Under Aspire's `AppHost`, the child process exits with a non-zero code and Aspire reports a generic "process exited" error — not the validation message. The actual `OptionsValidationException` is swallowed.

**Why it happens:**
`ValidateOnStart()` throws during `IHost.StartAsync()`. Aspire captures the exit code but not the stderr of the child process during normal dashboard startup. The developer sees "resource crashed" with no diagnostic detail.

**How to avoid:**
Wrap `ValidateOnStart()` with a try/catch in `Program.cs` that logs the exception message to `stderr` before rethrowing (or before `Environment.Exit(1)`). Alternatively, call the validator manually in a hosted startup service so the error appears in the OTel logs (which Aspire DOES surface). Add a test for the validator itself using `ServiceCollection` directly — do not rely on Aspire integration tests for validation logic.

**Warning signs:**
- `dotnet run --project DocAgent.AppHost` crashes immediately with no diagnostic output
- Aspire dashboard shows "Exited (1)" with no log output from the McpServer resource
- Validation logic works in unit tests but the process still crashes at runtime

**Phase to address:**
Startup validation phase — test the validator in isolation before wiring into the host lifecycle.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Upgrade Roslyn CSharp only, leave Common at old version | Faster PR | NU1107 in next CI run, mixed assemblies | Never |
| Inline rate limiter as static field in tool class | No DI changes needed | Shared mutable state, untestable, leaks between tests | Never |
| Add pagination but keep `total_count` always populated | Simpler response shape | Forces full result materialization even when caller wants only first page | Never for large graphs |
| Use `Dictionary` in `SymbolGraphSnapshot` for lookup, remove `List` | O(1) lookup | Breaks determinism, breaks serialization order | Never without explicit sort |
| Validate startup config only in integration tests | Easy to write | Silent failure in production Aspire deployments | Never for required config |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| BenchmarkDotNet + Roslyn upgrade | Assuming BDN's Roslyn dependency auto-resolves with CPM | Explicitly set `VersionOverride` on `Microsoft.CodeAnalysis.Common` in Benchmarks project until root version is updated |
| `System.Threading.RateLimiting` in tool DI | Registering `RateLimiter` as singleton then disposing in a using block inside the tool | Register as `IDisposable` singleton; lifetime managed by DI container, not per-call |
| `ModelContextProtocol` SDK + new tool parameters | Adding non-optional parameters to existing tools expecting backward compat | All new parameters on existing tools must have defaults; test with the old call signature |
| `Verify.Xunit` golden files + new response fields | Adding `total_count` field to existing tool responses invalidates all existing snapshots | Run `VERIFY_APPROVE_ALL=true dotnet test` once after intentional schema change, commit the updated snapshots |
| Aspire `ValidateOnStart()` | Relying on Aspire log capture for validation errors | Log to stderr explicitly before the host throws |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Materializing full result set to compute `total_count` before applying `limit` | Memory spike; slow first-page response | Count via the index; do not load all nodes into memory | Snapshots with >10,000 symbols |
| Caching search metadata in a mutable `Dictionary` without locking | Intermittent `KeyNotFoundException` under concurrent tool calls | Use `ConcurrentDictionary` or ensure cache is populated once at startup | Any concurrent MCP client |
| Batching project resolution with `Task.WhenAll` but sharing a single `MSBuildWorkspace` | `MSBuildWorkspace` is not thread-safe; exceptions from one project cancel others | Use per-batch sequential resolution, or create a workspace per task in the batch | First multi-project batch |
| Rate limiter window reset using `Stopwatch` (process uptime) instead of wall clock | Rate limit resets on process restart; long-running processes accumulate drift | Use `DateTimeOffset.UtcNow` for window boundaries, not elapsed ticks | Long-running server instances |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Rate limit keyed on MCP tool name only, not caller identity | A single caller can exhaust quota for all callers | For stdio transport, key on process lifetime (single-caller assumption is valid); document this assumption explicitly |
| Returning `total_count` of all symbols before allowlist filtering | Leaks graph size / symbol count to unauthenticated callers | Apply allowlist filter before counting; return count of accessible results only |
| `find_implementations` returning full FQNs of private types | Leaks internal API surface to agents operating outside their scope | Filter by `PathAllowlist` before returning; same pattern as existing tools |

---

## "Looks Done But Isn't" Checklist

- [ ] **Roslyn upgrade:** Verify `dotnet restore` produces zero NU1107 warnings across ALL projects including Benchmarks and Analyzers
- [ ] **Roslyn upgrade:** Verify the `VersionOverride` in `DocAgent.Tests.csproj` has been removed (it was a workaround that should be cleaned up post-upgrade)
- [ ] **Pagination:** Verify a call without `limit` parameter returns the same result count and shape as before pagination was added (regression test)
- [ ] **Pagination:** Verify `offset` past the end of results returns empty list, not an error
- [ ] **Rate limiting:** Verify the limiter rejects the (N+1)th call and returns a structured error (not an exception trace)
- [ ] **Rate limiting:** Verify the limiter does not fire during long-running ingestion tool calls (ingestion should not count against the query rate limit)
- [ ] **`find_implementations`:** Verify stub nodes are excluded from results
- [ ] **`find_implementations`:** Verify cross-project implementations are included when the snapshot is a solution snapshot
- [ ] **O(1) lookup:** Verify `Verify.Xunit` golden files still pass after the `Dictionary` introduction — no ordering changes
- [ ] **Startup validation:** Verify that an invalid `AllowedPaths` config causes a clear error message on stderr before process exit
- [ ] **CLAUDE.md refresh:** Verify the tool count (12) and all tool names match the actual registered `[McpServerTool]` methods

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Roslyn mixed-version assembly load exception | MEDIUM | Roll back all CodeAnalysis versions together; do not partial-revert |
| Pagination broke existing client behavior | MEDIUM | Revert `limit` default; introduce separate `search_symbols_paged` tool if needed |
| Rate limiter deadlock under parallel tests | LOW | Replace `SemaphoreSlim` with `FixedWindowRateLimiter`; add test isolation via DI |
| Determinism broken by Dictionary ordering | MEDIUM | Add explicit `OrderBy(n => n.FQN)` at all output-producing call sites |
| Startup validation swallowed by Aspire | LOW | Add explicit `try/catch` + `Console.Error.WriteLine` in `Program.cs` before host.RunAsync |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Roslyn 4.14 version conflict | Dependency upgrade (Phase 1) | `dotnet restore --verbosity detailed` — zero NU1107 |
| Pagination breaking clients | Pagination phase | Run existing tool integration tests with no `limit` arg |
| Rate limiter shared-state issues | Rate limiting phase | `dotnet test --parallel` — no deadlocks or counter leakage |
| Dictionary ordering breaks determinism | Performance optimization phase | All `Verify.Xunit` snapshots pass unchanged |
| `find_implementations` returning stubs | New tools phase | Unit test with explicit stub node in graph |
| Startup validation silent failure | Startup validation phase | Integration test with bad config — assert stderr message |

---

## Sources

- Microsoft.CodeAnalysis release notes and NuGet package dependencies (family versioning requirement): HIGH confidence — verified against current `Directory.Packages.props` and existing `VersionOverride` workaround in codebase
- `System.Threading.RateLimiting` API (built into .NET 7+, `FixedWindowRateLimiter`, `TokenBucketRateLimiter`): HIGH confidence — .NET 10 standard library
- MCP SDK tool schema backward compatibility: MEDIUM confidence — ModelContextProtocol 1.0.0 stable; optional parameter behavior verified against SDK source patterns
- Aspire `ValidateOnStart()` stderr capture behavior: MEDIUM confidence — based on Aspire process model; stderr logging workaround is defensive best practice regardless
- `MSBuildWorkspace` thread-safety: HIGH confidence — documented as not thread-safe in Roslyn workspace docs; matches existing patterns in codebase (single workspace per ingestion call)
- Determinism requirement and `Verify.Xunit` golden files: HIGH confidence — explicit project constraint in CLAUDE.md and PROJECT.md; `Dictionary` ordering is documented .NET behavior

---
*Pitfalls research for: DocAgentFramework v1.5 Robustness*
*Researched: 2026-03-04*
