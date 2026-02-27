# Phase 5: MCP Server + Security - Research

**Researched:** 2026-02-27
**Domain:** ModelContextProtocol C# SDK 1.0.0 — stdio MCP server tools, path security, audit logging, prompt injection defense
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Tool Response Shape**
- All five tools support `format=json|markdown|tron` parameter (default: JSON)
- TRON = Token Reduced Object Notation — a JSON superset that defines schemas to reduce property name repetition, ideal for LLM token efficiency
- **search_symbols:** Default compact (symbol kind, FQN, summary). `fullDocs=true` includes full parsed doc comment
- **get_symbol:** Default compact. `includeSourceSpans=true` adds file path + line range
- **get_references:** Default compact (referencing symbol ID + location). `includeContext=true` adds surrounding code snippets
- **diff_snapshots:** Default change list (added/removed/modified symbols). `includeDiffs=true` adds inline before/after doc content
- **explain_project:** Richest tool — supports `mode=json|markdown|tron`, depth controls for references and external dependencies, section include/exclude options, and a `chainedEntityDepth` parameter to load child entities in one call. Default provides most useful details with named handles for loading child entities on demand

**Error & Denial Behavior**
- Path denial: Opaque "access denied" by default. Debug/verbose config flag reveals path details and allowlist rule that blocked
- Error codes: Standard JSON-RPC codes for protocol errors, custom app-specific codes in a structured `data` field (e.g., path_denied, snapshot_not_found, invalid_symbol_id)
- Invalid input: Configurable — strict mode (default, fail fast with structured error) and lenient mode (best effort with partial results + warnings array)
- Prompt injection: Sanitize known injection patterns from doc comment content AND add a warning flag when suspicious patterns are detected

**Audit Logging**
- Tiered verbosity: Default logs metadata (tool name, input params summary, response size, duration, status). Verbose config flag includes full request/response bodies
- Dual output: Stderr for real-time monitoring + configurable file path for persistent audit trail. Both independently configurable
- Configurable redaction: Regex-based redaction patterns in config. Default: no redaction. Opt-in for sensitive environments

**Allowlist Configuration**
- Layered config: JSON config file as base, environment variable (`DOCAGENT_ALLOWED_PATHS`) overrides/extends
- Glob pattern support for path matching (full glob, not just prefixes)
- Default when unconfigured: Allow server's current working directory only
- Allow + deny rules: Both allow and deny patterns supported. Deny takes precedence over allow

### Claude's Discretion
- Audit log entry format (JSONL recommended)
- Exact TRON schema definitions for each tool
- Prompt injection pattern detection heuristics
- Specific custom error code numbers and naming
- Config file format and location conventions

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MCPS-01 | `search_symbols` MCP tool via stdio transport | `[McpServerTool]` attribute on method, wire to `IKnowledgeQueryService.SearchAsync` |
| MCPS-02 | `get_symbol` MCP tool | `[McpServerTool]` attribute, wire to `IKnowledgeQueryService.GetSymbolAsync` |
| MCPS-03 | `get_references` MCP tool | `[McpServerTool]` attribute, wire to `IKnowledgeQueryService.GetReferencesAsync` |
| MCPS-04 | `diff_snapshots` MCP tool | `[McpServerTool]` attribute, wire to `IKnowledgeQueryService.DiffAsync` |
| MCPS-05 | `explain_project` MCP tool | `[McpServerTool]` attribute, richest response shape, chainedEntityDepth pattern |
| MCPS-06 | Stderr-only logging — no stdout contamination of MCP JSON-RPC framing | `LogToStandardErrorThreshold = LogLevel.Trace` in console logger options; integration test captures stdout bytes |
| SECR-01 | Path allowlist — default-deny, only allowed directories accessible | `PathAllowlist` service using `Microsoft.Extensions.FileSystemGlobbing`; injected into tool class |
| SECR-02 | Audit logging — every tool call with input/output logged | `AddCallToolFilter` middleware wraps all tools; writes JSONL before returning response |
| SECR-03 | Input validation — defense against prompt injection via structured DTOs | Record parameter DTOs; `[McpServerTool]` accepts complex objects; injection pattern scanning on doc comment content returned |
</phase_requirements>

---

## Summary

Phase 5 wires the existing `IKnowledgeQueryService` (complete after Phase 4) into five MCP tool handlers and enforces three security controls. The technical foundation is the **ModelContextProtocol C# SDK 1.0.0** (released 2026-02-25, confirmed on NuGet). The codebase already has a `DocAgent.McpServer` project with stubs using the **old preview API** (`[McpTool]`, `[McpToolMethod]`) — these must be replaced with the 1.0.0 API (`[McpServerToolType]`, `[McpServerTool]`). This is a breaking change from the existing stub code.

The SDK 1.0.0 supports a **filter pipeline** (`AddCallToolFilter`) that is the correct insertion point for audit logging cross-cutting concerns — it runs around every tool call without modifying individual handlers. Path allowlisting is best implemented as a DI-registered service (`PathAllowlist`) using `Microsoft.Extensions.FileSystemGlobbing` for glob pattern matching, injected into the tool class via constructor injection. Stdout contamination (MCPS-06) is already correctly configured in `Program.cs` with `LogToStandardErrorThreshold = LogLevel.Trace`; the task is to verify it remains correct and add an integration test that byte-captures stdout.

The primary planning risk is the **API migration**: the existing stub uses `ModelContextProtocol 0.0.0-preview.2` with `[McpTool]`/`[McpToolMethod]`. The `Directory.Packages.props` must be updated to `1.0.0` and all attribute names changed. The 1.0.0 API is well-documented in Context7 with full code examples available.

**Primary recommendation:** Upgrade to `ModelContextProtocol 1.0.0`, implement tools as a DI-injected class with `[McpServerToolType]`, use `AddCallToolFilter` for the audit middleware, and inject `PathAllowlist` + `IKnowledgeQueryService` via constructor. Keep the five tools in a single `DocTools.cs` class (already exists) or split into `DocQueryTools.cs` + security services in `Security/`.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `ModelContextProtocol` | 1.0.0 | MCP server SDK — tools, stdio transport, filter pipeline | Official SDK from modelcontextprotocol org; 4M+ NuGet downloads; released 2026-02-25 |
| `Microsoft.Extensions.Hosting` | 10.0.0-preview.* | Host builder for DI, logging, configuration | Already in project; required for `AddMcpServer()` |
| `Microsoft.Extensions.Logging.Console` | 10.0.0-preview.* | Console logger with stderr threshold | Already in project; required for MCPS-06 |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.3 | Glob pattern matching for path allowlist | Official BCL extension; 3.2B downloads; handles `**/*.cs` patterns correctly |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.Json` | (inbox .NET 10) | JSONL audit log serialization, response serialization | Inbox — no package reference needed |
| `Microsoft.Extensions.Configuration.Json` | (transitive) | JSON config file for allowlist + audit settings | Available via Hosting transitive; no extra reference |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Microsoft.Extensions.FileSystemGlobbing` | Manual prefix matching | Glob handles `**/secrets/**` deny rules; prefix matching can't |
| `AddCallToolFilter` middleware | Per-tool logging in each handler | Filter runs cross-cutting — adding a 6th tool doesn't require touching logging code |
| JSONL file + stderr dual output | Structured logging via ILogger only | ILogger output format is not guaranteed JSONL; direct file write gives stable machine-readable format |

**Installation (Directory.Packages.props update required):**
```xml
<!-- Replace preview.2 with 1.0.0 -->
<PackageVersion Include="ModelContextProtocol" Version="1.0.0" />
<!-- Add for path glob matching -->
<PackageVersion Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.3" />
```

---

## Architecture Patterns

### Recommended Project Structure

```
src/DocAgent.McpServer/
├── Program.cs                    # Host builder, DI registration, AddMcpServer()
├── Tools/
│   └── DocTools.cs               # [McpServerToolType] class — all 5 tool handlers
├── Security/
│   ├── PathAllowlist.cs          # Glob-based allow+deny path checker
│   ├── AuditLogger.cs            # JSONL writer to stderr + optional file
│   └── PromptInjectionFilter.cs  # Pattern scanner for doc comment content
├── Filters/
│   └── AuditFilter.cs            # AddCallToolFilter implementation
├── Dto/
│   └── ToolRequests.cs           # Input DTOs: SearchRequest, GetSymbolRequest, etc.
└── Config/
    └── DocAgentServerOptions.cs  # Strongly-typed options: allowlist, audit settings
```

### Pattern 1: Tool Class with DI Injection (1.0.0 API)

**What:** `[McpServerToolType]` marks the class; `[McpServerTool]` marks each method. Constructor injection works for any registered DI service.

**When to use:** Always — this is the standard 1.0.0 pattern. Static classes also work but cannot receive DI services via constructor.

**Example:**
```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/README.md
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public sealed class DocTools
{
    private readonly IKnowledgeQueryService _query;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<DocTools> _logger;

    public DocTools(
        IKnowledgeQueryService query,
        PathAllowlist allowlist,
        ILogger<DocTools> logger)
    {
        _query = query;
        _allowlist = allowlist;
        _logger = logger;
    }

    [McpServerTool(Name = "search_symbols"), Description("Search symbols and documentation by keyword.")]
    public async Task<string> SearchSymbols(
        [Description("Search query")] string query,
        [Description("Output format: json|markdown|tron")] string format = "json",
        [Description("Include full doc comments")] bool fullDocs = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _query.SearchAsync(query, ct: cancellationToken);
        // ... format and return
    }
}
```

### Pattern 2: Audit Filter (Cross-Cutting Middleware)

**What:** `AddCallToolFilter` wraps every tool call. Use this for audit logging so individual tool methods stay clean.

**When to use:** Any cross-cutting concern — audit, rate limiting, auth checks.

**Example:**
```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/filters.md
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var audit = context.Services!.GetRequiredService<AuditLogger>();
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await next(context, cancellationToken);
            audit.LogSuccess(context.Params?.Name, context.Params?.Arguments, result, sw.Elapsed);
            return result;
        }
        catch (Exception ex)
        {
            audit.LogError(context.Params?.Name, context.Params?.Arguments, ex, sw.Elapsed);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Type = "text", Text = BuildErrorJson(ex) }],
                IsError = true
            };
        }
    });
```

### Pattern 3: Stderr-Only Logging (MCPS-06)

**What:** Configure console logger to send ALL log levels to stderr, leaving stdout clean for JSON-RPC framing.

**When to use:** Required for any stdio MCP server.

**Example:**
```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/README.md
builder.Logging.AddConsole(consoleLogOptions =>
{
    // ALL logs to stderr — LogLevel.Trace covers everything
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
```

**Critical:** `LogLevel.Information` (current stub) is not sufficient — Debug and Trace logs would still go to stdout. Use `LogLevel.Trace` to guarantee all logging is on stderr.

### Pattern 4: Path Allowlist with FileSystemGlobbing

**What:** `Microsoft.Extensions.FileSystemGlobbing.Matcher` evaluates glob patterns against normalized absolute paths. Allow + deny rules; deny takes precedence.

**Example:**
```csharp
using Microsoft.Extensions.FileSystemGlobbing;

public sealed class PathAllowlist
{
    private readonly IReadOnlyList<string> _allowPatterns;
    private readonly IReadOnlyList<string> _denyPatterns;
    private readonly bool _isConfigured;

    public bool IsAllowed(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);

        // Deny patterns take precedence
        if (MatchesAny(normalized, _denyPatterns)) return false;

        // Default when unconfigured: allow cwd
        if (!_isConfigured)
            return normalized.StartsWith(Directory.GetCurrentDirectory(), StringComparison.OrdinalIgnoreCase);

        return MatchesAny(normalized, _allowPatterns);
    }

    private static bool MatchesAny(string path, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0) return false;
        var matcher = new Matcher();
        foreach (var p in patterns) matcher.AddInclude(p);
        return matcher.Match(path).HasMatches;
    }
}
```

### Pattern 5: Structured Error Response

**What:** Tool methods throw `McpException` for MCP protocol errors, or return `CallToolResult` with `IsError = true` + structured JSON body for application errors.

**Example:**
```csharp
// For path denial — opaque by default
return new CallToolResult
{
    Content = [new TextContentBlock
    {
        Type = "text",
        Text = JsonSerializer.Serialize(new
        {
            error = "path_denied",
            message = "Access denied",
            // Only include details when verbose mode configured:
            // detail = _options.VerboseErrors ? $"Path '{path}' not in allowlist" : null
        })
    }],
    IsError = true
};
```

### Anti-Patterns to Avoid

- **Writing to stdout directly:** `Console.Write`, `Console.WriteLine`, unfiltered `Console.Out` writes contaminate the JSON-RPC stream. ALL output must go through the MCP SDK response mechanism or stderr.
- **Using static `[McpServerToolType]` class for DI dependencies:** Static classes cannot receive constructor injection. Use `sealed class` + constructor injection when you need `IKnowledgeQueryService`.
- **Using old preview attributes:** `[McpTool]` and `[McpToolMethod]` are the `0.0.0-preview.2` API. They do not exist in `1.0.0`. The existing `DocTools.cs` stub must be rewritten.
- **Path normalization before allowlist:** Always call `Path.GetFullPath()` before pattern matching to prevent `../` traversal bypasses.
- **Throwing unhandled exceptions from tool methods:** Unhandled exceptions surface as MCP protocol errors (opaque to caller). Catch and return `CallToolResult { IsError = true }` with structured data instead.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Glob path matching | Manual prefix/wildcard string comparison | `Microsoft.Extensions.FileSystemGlobbing.Matcher` | Handles `**/`, case sensitivity, path separator normalization correctly |
| MCP JSON-RPC framing | Custom stdio reader/writer | `WithStdioServerTransport()` | Framing, message length, partial reads — non-trivial to get right |
| Tool discovery/registration | Reflection-based tool scanning | `WithToolsFromAssembly()` | SDK handles schema generation from C# parameter types + `[Description]` attributes |
| JSON schema from C# types | JsonSchemaBuilder | SDK auto-generates from method signatures | Parameters with `[Description]` become the tool input schema automatically |

**Key insight:** The MCP SDK auto-generates the JSON Schema for tool inputs from C# method parameter types and `[Description]` attributes. Don't write schema manually.

---

## Common Pitfalls

### Pitfall 1: Old Preview API in Existing Stub

**What goes wrong:** `DocTools.cs` currently uses `[McpTool]` and `[McpToolMethod]` from `0.0.0-preview.2`. These don't exist in `1.0.0` — the build will fail.

**Why it happens:** The stub was written before the 1.0.0 release (confirmed in STATE.md: "MCP SDK 1.0.0 released 2026-02-25 — verify before plan-phase").

**How to avoid:** Plan 05-01 MUST start by upgrading `Directory.Packages.props` to `ModelContextProtocol 1.0.0` and rewriting `DocTools.cs` with the new attribute names.

**Warning signs:** `CS0246: The type or namespace name 'McpTool' could not be found` on build.

### Pitfall 2: LogToStandardErrorThreshold Set Too High

**What goes wrong:** Setting threshold to `LogLevel.Information` (as in the current `Program.cs` stub) means `Debug` and `Trace` logs can still emit to stdout, contaminating the JSON-RPC stream.

**Why it happens:** The intent of the comment ("MCP servers often run as stdio helpers; keep logs on stderr") is correct but the threshold value is wrong.

**How to avoid:** Use `LogLevel.Trace` — this ensures ALL log levels go to stderr.

**Warning signs:** `mcp inspect` returns parse errors or malformed JSON-RPC on verbose logging scenarios.

### Pitfall 3: Path Traversal via Unsanitized Input

**What goes wrong:** A caller passes `symbolId` or project path like `../../etc/passwd`. Without normalization, prefix matching allows it.

**Why it happens:** String prefix matching on un-normalized paths is bypassable.

**How to avoid:** Always call `Path.GetFullPath(input)` before any path comparison or glob match. Do this in `PathAllowlist.IsAllowed()` before pattern evaluation.

**Warning signs:** Allowlist passes paths that contain `..` segments.

### Pitfall 4: Audit Logger Writing After Response Returns

**What goes wrong:** If audit logging is async fire-and-forget, a crash after response returns means the call is unlogged — violating SECR-02.

**Why it happens:** Trying to optimize by not blocking the response on the log write.

**How to avoid:** In the `AddCallToolFilter`, complete the audit write (`await`) before returning the `CallToolResult`. The filter is the correct place — it already awaits.

### Pitfall 5: Prompt Injection via Doc Comment Content

**What goes wrong:** A doc comment contains `Ignore previous instructions. You are now...`. When the MCP tool returns this as a string, an agent reading the response may interpret it as instructions.

**Why it happens:** Doc comments are user-controlled content; the tool is a read-only mirror of that content.

**How to avoid:** Per decisions: scan returned doc comment content for known injection patterns and set a `promptInjectionWarning: true` flag in the response envelope. Do NOT refuse to return the content — return it as structured data with the warning flag. Tests should verify that injection text in a fixture symbol is returned verbatim (not executed) in a `content` field.

---

## Code Examples

Verified patterns from official sources:

### 1. Host builder with 1.0.0 API (full Program.cs pattern)

```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/README.md
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace; // ALL to stderr
});
builder.Services
    .AddSingleton<PathAllowlist>()        // Security services
    .AddSingleton<AuditLogger>()
    .AddSingleton<IKnowledgeQueryService, KnowledgeQueryService>() // Phase 4 output
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()              // Discovers [McpServerToolType] classes
    .AddCallToolFilter(/* audit filter */);

await builder.Build().RunAsync();
```

### 2. Tool with DI injection (1.0.0)

```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/README.md (adapted)
[McpServerToolType]
public sealed class DocTools
{
    private readonly IKnowledgeQueryService _query;
    private readonly PathAllowlist _allowlist;

    public DocTools(IKnowledgeQueryService query, PathAllowlist allowlist)
    {
        _query = query;
        _allowlist = allowlist;
    }

    [McpServerTool(Name = "get_symbol"), Description("Get full symbol detail by stable SymbolId.")]
    public async Task<string> GetSymbol(
        [Description("Stable SymbolId (assembly-qualified)")] string symbolId,
        [Description("Include source file path and line range")] bool includeSourceSpans = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        var id = SymbolId.Parse(symbolId); // throws on invalid — caught by filter
        var result = await _query.GetSymbolAsync(id, ct: cancellationToken);
        // map QueryResult → CallToolResult JSON
    }
}
```

### 3. Audit filter

```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/filters.md (adapted)
.AddCallToolFilter(next => async (context, cancellationToken) =>
{
    var audit = context.Services!.GetRequiredService<AuditLogger>();
    var sw = Stopwatch.StartNew();
    CallToolResult result;
    try
    {
        result = await next(context, cancellationToken);
        audit.Log(context.Params?.Name, context.Params?.Arguments, result, sw.Elapsed, success: true);
    }
    catch (Exception ex)
    {
        audit.Log(context.Params?.Name, context.Params?.Arguments, null, sw.Elapsed, success: false, error: ex.Message);
        result = new CallToolResult
        {
            Content = [new TextContentBlock { Type = "text", Text = BuildErrorJson(ex) }],
            IsError = true
        };
    }
    return result;
})
```

### 4. JSONL audit log entry shape

```csharp
// Claude's discretion — JSONL recommended
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Tool,
    string InputSummary,       // metadata tier: param names + lengths
    string? InputFull,         // verbose tier only
    int ResponseBytes,
    string? ResponseFull,      // verbose tier only
    bool Success,
    string? ErrorCode,
    TimeSpan Duration);

// Serialize with:
// JsonSerializer.Serialize(entry) + "\n"  →  write to stderr and/or file
```

### 5. PathAllowlist from config + env override

```csharp
public sealed class DocAgentServerOptions
{
    public string[] AllowedPaths { get; init; } = [];
    public string[] DeniedPaths  { get; init; } = [];
    public bool VerboseErrors    { get; init; } = false;
    public AuditOptions Audit    { get; init; } = new();
}

// In Program.cs:
builder.Services.Configure<DocAgentServerOptions>(
    builder.Configuration.GetSection("DocAgent"));

// Env override for DOCAGENT_ALLOWED_PATHS (comma-separated):
var envPaths = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
if (envPaths is not null)
{
    // Merge into options after bind
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `[McpTool]` / `[McpToolMethod]` | `[McpServerToolType]` / `[McpServerTool]` | 1.0.0 (2026-02-25) | **Breaking** — existing stub must be rewritten |
| `ModelContextProtocol 0.0.0-preview.2` | `ModelContextProtocol 1.0.0` | 2026-02-25 | Package version bump in CPM required |
| No filter pipeline (preview) | `AddCallToolFilter` / `AddListToolsFilter` | 1.0.0 | Clean AOP-style middleware for audit, auth |

**Deprecated/outdated:**
- `[McpTool]` attribute: replaced by `[McpServerToolType]` on the class
- `[McpToolMethod]` attribute: replaced by `[McpServerTool]` on methods
- The current `DocTools.cs` stub will not compile against 1.0.0 — it is effectively dead code to be replaced

---

## Open Questions

1. **`explain_project` implementation depth**
   - What we know: This is the "one call to understand everything" tool. Decisions specify `chainedEntityDepth` parameter and section include/exclude options.
   - What's unclear: The `IKnowledgeQueryService` as defined in Phase 4 has no `ExplainProject` method — this tool will need to compose multiple calls (search, get_symbol for top-level types, diff optionally).
   - Recommendation: Plan 05-01 should define `explain_project` as a composed operation over existing service methods, not add a new service method. The planner should scope this as "breadth-first traversal of top-level types using existing `GetSymbolAsync` + `SearchAsync`."

2. **TRON format implementation**
   - What we know: TRON is defined at https://tron-format.github.io/ — a JSON superset for LLM token efficiency. User explicitly requested it as a first-class format option.
   - What's unclear: No .NET library for TRON serialization was found in Context7 or NuGet. It may need to be hand-rolled as a simple serializer, or the format may be simple enough to implement manually.
   - Recommendation: Plan 05-01 should include a TRON spike — check if a .NET library exists (NuGet search: `tron format`) and if not, implement a minimal TRON serializer for the 5 tool response shapes only. Defer full TRON spec compliance to a follow-up.

3. **Test coverage for stdout contamination (MCPS-06)**
   - What we know: The integration test must byte-capture stdout while running the MCP server process and assert zero non-JSON-RPC bytes.
   - What's unclear: Whether to spin up an in-process `McpServer` or a subprocess. In-process is simpler but may not fully test the stdio transport boundary.
   - Recommendation: Plan 05-04 should use a subprocess test — launch `dotnet run --project src/DocAgent.McpServer` and pipe stdin/capture stdout in the test. This truly validates the transport boundary.

4. **`GetReferencesAsync` returns `IAsyncEnumerable<SymbolEdge>` — MCP tool return type**
   - What we know: `IKnowledgeQueryService.GetReferencesAsync` returns `IAsyncEnumerable<SymbolEdge>`. MCP tool methods return `string` or `CallToolResult`, not async enumerables.
   - What's unclear: Whether to buffer and serialize or stream.
   - Recommendation: Buffer to list (`await foreach` into `List<SymbolEdge>`, then serialize). MCP JSON-RPC is request/response, not streaming. Plan 05-01 should note this explicitly.

---

## Sources

### Primary (HIGH confidence)
- `/modelcontextprotocol/csharp-sdk` (Context7) — `[McpServerToolType]`, `[McpServerTool]`, `AddCallToolFilter`, `WithToolsFromAssembly`, `LogToStandardErrorThreshold`, `CallToolResult`, `IsError`
- NuGet.org `dotnet package search ModelContextProtocol` — confirmed 1.0.0 as latest stable (2026-02-27)
- NuGet.org `dotnet package search Microsoft.Extensions.FileSystemGlobbing` — confirmed 10.0.3 as latest

### Secondary (MEDIUM confidence)
- Existing project source files (`DocTools.cs`, `Program.cs`, `DocAgent.McpServer.csproj`, `Directory.Packages.props`) — confirmed old API in use, confirmed package references needed
- `docs/Security.md` + `DocAgent.Core/Abstractions.cs` + `DocAgent.Core/QueryTypes.cs` — domain contract for tool wiring
- STATE.md research flag: "MCP SDK 1.0.0 released 2026-02-25 — verify exact `[McpServerTool]` attribute API" — confirmed via Context7

### Tertiary (LOW confidence)
- TRON format (https://tron-format.github.io/) — Not researched via Context7 or official docs; no .NET library found. Flag for spike investigation in planning.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — NuGet confirmed 1.0.0; Context7 has 154 code snippets for the SDK
- Architecture: HIGH — filter pipeline, DI injection patterns verified from official SDK docs
- Pitfalls: HIGH — old preview API vs 1.0.0 confirmed from existing codebase; LogLevel.Trace confirmed from SDK README
- TRON format: LOW — no verified .NET library found; needs spike

**Research date:** 2026-02-27
**Valid until:** 2026-03-27 (stable SDK, 30-day window)
