# Phase 6: Analysis + Hosting - Research

**Researched:** 2026-02-27
**Domain:** Roslyn DiagnosticAnalyzer authoring + .NET Aspire AppHost wiring + OpenTelemetry instrumentation
**Confidence:** HIGH (Roslyn analyzer API), MEDIUM (Aspire AppHost for non-HTTP process), HIGH (OpenTelemetry)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Analyzer severity:** Default Warning; teams escalate to Error via .editorconfig or project-level severity overrides.
- **Target scope:** All public members — types, methods, properties, events, fields.
- **Suppression:** Support both custom `[ExcludeFromDocCoverage]` attribute AND standard `#pragma warning disable` / `[SuppressMessage]`.
- **ANLY-02 scope:** Signature changes (parameter types, return type, accessibility, generic constraints) + observable contracts (new throw statements, nullability annotation changes, attribute changes).
- **ANLY-03 threshold:** 80% of public symbols must have `<summary>` doc; per-project measurement.
- **ANLY-03 reporting:** Summary line + list of undocumented symbols.
- **ANLY-03 configuration:** Both MSBuild property (`<DocCoverageThreshold>80</DocCoverageThreshold>`) and EditorConfig.
- **Telemetry default mode:** tool name, duration, success/error status, symbol count returned.
- **Telemetry verbose mode:** full input parameters, output size, result count, nested pipeline stage spans. Verbose is default in Debug/Development builds.
- **Telemetry exporter:** OTLP to Aspire dashboard (standard pipeline); redirectable via configuration.
- **Aspire resource name:** `docagent-mcp` with health check endpoint; green/red status in dashboard.
- **Aspire config:** AppHost is primary source of truth for artifacts directory and allowlist paths via Aspire resource config. Env vars as fallback.
- **Structured logging:** Wire ILogger through OpenTelemetry log exporter so logs appear in Aspire dashboard. Stderr still for MCP transport isolation.

### Claude's Discretion

- Analyzer diagnostic IDs and naming conventions.
- Health check implementation details (what constitutes "healthy").
- Exact OpenTelemetry span naming conventions.
- How verbose mode is toggled (config flag, environment variable, etc.).
- Internal Roslyn syntax analysis approach for detecting observable contract changes.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| ANLY-01 | Roslyn analyzer: detect public API changes not reflected in documentation | RegisterSymbolAction on NamedType/Method/Property/Event/Field; check GetDocumentationCommentXml() for non-empty; report diagnostic at symbol location |
| ANLY-02 | Roslyn analyzer: detect suspicious edits (semantic changes without doc/test updates) | RegisterSymbolStartAction/End for stateful per-symbol tracking; SyntaxNodeAnalysis for throw statements; compare parameter types/return type via ISymbol metadata |
| ANLY-03 | Doc coverage policy enforcement (configurable threshold) | RegisterCompilationStartAction + CompilationEnd to aggregate across all symbols; read threshold from AnalyzerConfigOptions; report per-project summary diagnostic |
| HOST-01 | Aspire app host with DI extension methods (`AddDocAgentCore`, etc.) | Convert DocAgent.AppHost.csproj to use Aspire.AppHost.Sdk; AddProject<Projects.DocAgent_McpServer>; WithEnvironment for ArtifactsDir; WithHttpEndpoint for health; AddServiceDefaults in McpServer |
| HOST-02 | OpenTelemetry wiring for tool call observation | ActivitySource in McpServer for tool call spans; AddOpenTelemetry().WithTracing(t => t.AddSource("DocAgent.McpServer")); OTLP exporter via OTEL_EXPORTER_OTLP_ENDPOINT env var; verbose mode via ASPNETCORE_ENVIRONMENT or custom env var |
</phase_requirements>

---

## Summary

Phase 6 has two distinct technical domains: Roslyn analyzer authoring (ANLY-01/02/03) and Aspire hosting + OpenTelemetry wiring (HOST-01/02). Both domains are well-understood by the ecosystem and have clear standard patterns, but some project-specific constraints require care.

**Roslyn analyzers** are authored as classes deriving from `DiagnosticAnalyzer` in a `netstandard2.0` project. They use `RegisterSymbolAction` for per-symbol checks (ANLY-01/02) and `RegisterCompilationStartAction` + `CompilationEndAction` for cross-symbol aggregation (ANLY-03). The key API for checking documentation is `ISymbol.GetDocumentationCommentXml()` — it returns null or empty string when no doc comment is present. Suppressions via `[SuppressMessage]` and `#pragma warning disable` are handled automatically by the Roslyn infrastructure if analyzers are properly packaged. The custom `[ExcludeFromDocCoverage]` attribute suppression requires explicit opt-out checking in the analyzer code itself.

**Aspire hosting** for a non-HTTP stdio process (the MCP server) has one confirmed pitfall: `AddProject<T>` works correctly when the AppHost project uses `Aspire.AppHost.Sdk` (not `Microsoft.NET.Sdk`). The MCP server should add OpenTelemetry via `AddOpenTelemetry()` extension (from `OpenTelemetry.Extensions.Hosting`) with a custom `ActivitySource` named `"DocAgent.McpServer"`. The Aspire dashboard will consume OTLP telemetry automatically when the MCP server process is launched under Aspire (env vars injected). Health check for the MCP server requires exposing a minimal HTTP `/health` endpoint alongside the stdio transport — the simplest approach is `Microsoft.Extensions.Diagnostics.HealthChecks` wired via `IHostedService` or a minimal `Kestrel` endpoint.

**Primary recommendation:** Build three analyzer classes in a new `DocAgent.Analyzers` project targeting `netstandard2.0`. Wire Aspire AppHost by upgrading the `.csproj` to `Aspire.AppHost.Sdk/13.1`, reference the McpServer project via `AddProject<Projects.DocAgent_McpServer>`, inject config via `WithEnvironment`, and add OTLP tracing inside the McpServer's `Program.cs` using a custom `ActivitySource`.

---

## Standard Stack

### Core — Roslyn Analyzers

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.CodeAnalysis.CSharp` | 4.12.0 (already pinned) | DiagnosticAnalyzer base types, ISymbol, syntax APIs | The official Roslyn SDK — required for all analyzer work |
| `Microsoft.CodeAnalysis.Analyzers` | 3.11.0 | Analyzer meta-rules (validates analyzer code itself) | Included transitively by `Microsoft.CodeAnalysis`; provides build-time validation |

> **Note:** Analyzers must target `netstandard2.0`. They run inside the compiler host (can be .NET Framework or .NET Core). The rest of the solution targets `net10.0` — the analyzer project must have its own `TargetFramework`.

### Core — Aspire Hosting

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Aspire.AppHost.Sdk` | 13.1 | AppHost SDK — generates `Projects.*` types, injects DCP | Required for AppHost projects since Aspire 9.2; replaces `IsAspireHost` property |
| `Aspire.Hosting` | 13.1.1 | `DistributedApplication`, `AddProject`, `WithEnvironment` | Hosting API for all resource declarations |

### Core — OpenTelemetry

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `OpenTelemetry.Extensions.Hosting` | latest stable (~1.11) | `AddOpenTelemetry()` extension on `IHostApplicationBuilder` | Official integration point for .NET Generic Host |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | latest stable (~1.11) | OTLP exporter to Aspire dashboard | Standard exporter; Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` automatically |
| `OpenTelemetry.Instrumentation.Runtime` | latest stable | Runtime metrics (GC, thread pool) | Recommended by Aspire service defaults template |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Extensions.Diagnostics.HealthChecks` | 10.0.x | `/health` endpoint for Aspire dashboard | Needed so Aspire can show green/red for `docagent-mcp` resource |
| `Microsoft.AspNetCore.Diagnostics.HealthChecks` | 10.0.x | `MapHealthChecks("/health")` route | Only if McpServer adds minimal Kestrel alongside stdio |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom health HTTP endpoint | Named pipe or file-based heartbeat | HTTP is what Aspire `.WithHttpHealthCheck()` expects — don't hand-roll |
| Custom `ActivitySource` | Auto-instrumentation of HttpClient | MCP tool calls are custom entry points; auto-instrumentation doesn't cover them |
| Separate `DocAgent.Analyzers` project | Inline analyzers in `DocAgent.Core` | Core must remain `net10.0`; analyzers require `netstandard2.0` — separate project required |

### Installation

```bash
# Analyzer project (new — netstandard2.0)
dotnet new classlib -n DocAgent.Analyzers --framework netstandard2.0

# AppHost upgrade (edit .csproj Sdk attribute)
# Sdk="Aspire.AppHost.Sdk/13.1"

# McpServer additions
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.Runtime
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks
```

---

## Architecture Patterns

### Recommended Project Structure

```
src/
├── DocAgent.Analyzers/          # NEW — netstandard2.0 analyzer project
│   ├── DocAgent.Analyzers.csproj
│   ├── DocParity/
│   │   └── DocParityAnalyzer.cs          # ANLY-01
│   ├── SuspiciousEdit/
│   │   └── SuspiciousEditAnalyzer.cs     # ANLY-02
│   ├── Coverage/
│   │   └── DocCoverageAnalyzer.cs        # ANLY-03
│   └── Suppression/
│       └── ExcludeFromDocCoverageAttribute.cs  # custom attribute marker
├── DocAgent.AppHost/            # EXISTING — upgrade SDK
│   └── DocAgent.AppHost.csproj  # change to Aspire.AppHost.Sdk/13.1
│   └── Program.cs               # AddProject + WithEnvironment + health
└── DocAgent.McpServer/          # EXISTING — add OTel wiring
    └── Telemetry/
        └── DocAgentActivitySource.cs     # static ActivitySource
    └── Program.cs               # AddOpenTelemetry, health endpoint
```

### Pattern 1: RegisterSymbolAction for Per-Symbol Doc Check (ANLY-01)

**What:** For every public symbol, check if `GetDocumentationCommentXml()` returns a non-null, non-empty string containing `<summary>`. Report a diagnostic if missing.

**When to use:** ANLY-01 (doc parity on public API changes). Also the base pattern for ANLY-02.

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix
// Source: https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocParityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DOCAGENT001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Public member lacks XML documentation",
        messageFormat: "Public member '{0}' has no <summary> documentation",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,   // CONTEXT: Warning, escalate via .editorconfig
        isEnabledByDefault: true,
        description: "All public API members should have XML documentation.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeSymbol,
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Event,
            SymbolKind.Field);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        // Only public members
        if (symbol.DeclaredAccessibility != Accessibility.Public)
            return;

        // Check for custom suppression attribute
        if (symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "ExcludeFromDocCoverageAttribute"))
            return;

        // Check for XML doc comment with <summary>
        var xml = symbol.GetDocumentationCommentXml(
            cancellationToken: context.CancellationToken);

        if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<summary>"))
        {
            var diagnostic = Diagnostic.Create(Rule, symbol.Locations[0], symbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
```

### Pattern 2: RegisterCompilationStartAction + End for Coverage (ANLY-03)

**What:** Accumulate public symbol counts and undocumented symbol names across all symbols in the compilation; report a single diagnostic at compilation end if threshold not met.

**When to use:** ANLY-03 — aggregation across entire project is required for a coverage ratio.

```csharp
// Source: https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md

public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.EnableConcurrentExecution();

    context.RegisterCompilationStartAction(compilationContext =>
    {
        // Thread-safe accumulation (EnableConcurrentExecution is set)
        var totalPublic = 0;
        var undocumented = new ConcurrentBag<string>();

        compilationContext.RegisterSymbolAction(symbolContext =>
        {
            var symbol = symbolContext.Symbol;
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (symbol.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "ExcludeFromDocCoverageAttribute")) return;

            Interlocked.Increment(ref totalPublic);

            var xml = symbol.GetDocumentationCommentXml(
                cancellationToken: symbolContext.CancellationToken);
            if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<summary>"))
                undocumented.Add(symbol.ToDisplayString());

        }, SymbolKind.NamedType, SymbolKind.Method, SymbolKind.Property,
           SymbolKind.Event, SymbolKind.Field);

        compilationContext.RegisterCompilationEndAction(endContext =>
        {
            if (totalPublic == 0) return;

            // Read threshold from AnalyzerConfig (MSBuild property or .editorconfig)
            // Key: "build_property.DocCoverageThreshold" (MSBuild property mapping)
            int threshold = 80;
            if (endContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue("build_property.DocCoverageThreshold", out var raw)
                && int.TryParse(raw, out var parsed))
            {
                threshold = parsed;
            }

            double coverage = (totalPublic - undocumented.Count) * 100.0 / totalPublic;
            if (coverage < threshold)
            {
                var symbolList = string.Join(", ", undocumented.Take(20));
                endContext.ReportDiagnostic(
                    Diagnostic.Create(CoverageRule, Location.None,
                        $"{coverage:F1}%", threshold, symbolList));
            }
        });
    });
}
```

### Pattern 3: Aspire AppHost AddProject for stdio Process (HOST-01)

**What:** Declare the MCP server as a named Aspire resource using `AddProject<T>`, inject config as environment variables, add health check via HTTP endpoint.

**When to use:** HOST-01.

```csharp
// Source: https://aspire.dev/get-started/app-host/
// AppHost/Program.cs

var builder = DistributedApplication.CreateBuilder(args);

var artifactsDir = builder.AddParameter("artifacts-dir",
    defaultValue: "./artifacts");

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR", artifactsDir)
    .WithHttpEndpoint(port: 8089, name: "health")   // for Aspire health probe
    .WithHttpHealthCheck("/health");                 // green/red in dashboard

builder.Build().Run();
```

> **Critical note:** The AppHost `.csproj` must use `Sdk="Aspire.AppHost.Sdk/13.1"` for the `Projects.DocAgent_McpServer` type to be generated. The current placeholder uses `Microsoft.NET.Sdk` — this must change.

### Pattern 4: OpenTelemetry in McpServer (HOST-02)

**What:** Declare a static `ActivitySource`, start spans around tool calls, add OpenTelemetry hosting with OTLP exporter.

```csharp
// DocAgent.McpServer/Telemetry/DocAgentActivitySource.cs
public static class DocAgentTelemetry
{
    public const string SourceName = "DocAgent.McpServer";
    public static readonly ActivitySource Source = new(SourceName);
}

// Usage in tool methods (DocTools.cs):
using var activity = DocAgentTelemetry.Source.StartActivity("tool.search_symbols");
activity?.SetTag("tool.name", "search_symbols");
activity?.SetTag("tool.query", request.Query);
// ... do work ...
activity?.SetTag("tool.result_count", results.Count);
```

```csharp
// Program.cs — add after existing builder setup
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(DocAgentTelemetry.SourceName);
        // Verbose in Dev, minimal in Prod
        if (builder.Environment.IsDevelopment())
            tracing.SetSampler(new AlwaysOnSampler());
    })
    .WithMetrics(metrics =>
    {
        metrics.AddRuntimeInstrumentation();
    });

// Wire logs through OTel so they appear in Aspire dashboard
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

// OTLP exporter — Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT automatically
builder.Services.Configure<OtlpExporterOptions>(options =>
{
    // Reads from OTEL_EXPORTER_OTLP_ENDPOINT env var by default
});
builder.Services.AddOpenTelemetry()
    .UseOtlpExporter();  // reads OTEL_EXPORTER_OTLP_ENDPOINT
```

### Anti-Patterns to Avoid

- **Analyzer in net10.0 project:** Roslyn analyzers run in the compiler host (may be .NET Framework). They MUST target `netstandard2.0`. Putting analyzer code in `DocAgent.Core` (net10.0) will fail to load in build-time analysis.
- **Using `RegisterSyntaxNodeAction` for symbol-level doc check:** Syntax nodes don't have semantic context (accessibility, doc comment XML). Use `RegisterSymbolAction` instead.
- **Calling `GetDocumentationCommentXml()` with `expandIncludes: true` from CompilationEnd context:** This is expensive at compile-end aggregation. Call without expansion (default) for coverage checks; only expand if doc content must be deeply validated.
- **Reporting coverage diagnostic with `Location.None` and `TreatWarningsAsErrors`:** A `Location.None` diagnostic without a file makes MSBuild show it at project level — expected behavior. Document this for users.
- **Forgetting `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`:** Without this, analyzers fire on generated code (e.g., `*.g.cs`), causing spurious diagnostics on auto-generated files.
- **AppHost using `Microsoft.NET.Sdk` without Aspire.AppHost.Sdk:** The `Projects.*` namespace is generated by the Aspire SDK. Without the SDK switch, `AddProject<Projects.DocAgent_McpServer>` won't compile.
- **Hardcoding OTLP endpoint:** Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` automatically when running under the AppHost. Don't hardcode — use `UseOtlpExporter()` which reads the env var.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| `#pragma warning disable` / `[SuppressMessage]` suppression | Custom suppression registry | Roslyn's built-in suppression infrastructure | Handled automatically when analyzer is packaged as analyzer assembly; implementing custom suppression is complex and fragile |
| OTLP trace export | Custom HTTP telemetry writer | `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Protocol versioning, batching, retry — OpenTelemetry SDK handles all of it |
| Health check HTTP listener | Custom `TcpListener` / `HttpListener` | `Microsoft.Extensions.Diagnostics.HealthChecks` + `MapHealthChecks` | Integrates with Aspire `.WithHttpHealthCheck()` which requires a standard HTTP endpoint |
| Cross-platform activation of analyzers | Custom MSBuild `.targets` authoring | Roslyn's `analyzers/dotnet/cs/` NuGet folder convention | NuGet/MSBuild automatically activates analyzer assemblies placed in this folder |
| Concurrent symbol aggregation | Locks or per-thread state | `ConcurrentBag<T>` + `Interlocked` | `EnableConcurrentExecution()` means symbol actions fire in parallel |

**Key insight:** The Roslyn + Aspire ecosystems have invested heavily in the boring infrastructure. Every item in this list represents a case where the "obvious" custom solution breaks under some combination of parallel execution, .NET Framework/Core hosting differences, or batching requirements.

---

## Common Pitfalls

### Pitfall 1: Analyzer netstandard2.0 vs. net10.0 Confusion

**What goes wrong:** Placing analyzer classes in a `net10.0` project (e.g., `DocAgent.Core`). Build-time analysis succeeds locally (same runtime), but fails for developers using Visual Studio on .NET Framework or when run in a compilation server with a different runtime.

**Why it happens:** The Roslyn compiler host loads analyzer assemblies in-process. It may be running on .NET Framework (in VS) or .NET Core (dotnet CLI). `net10.0` assemblies cannot load in .NET Framework.

**How to avoid:** New `DocAgent.Analyzers` project explicitly set to `<TargetFramework>netstandard2.0</TargetFramework>`.

**Warning signs:** `CS8032: An instance of analyzer cannot be created` in build output.

### Pitfall 2: AnalyzerConfigOptions Key Format

**What goes wrong:** Reading MSBuild properties from analyzer config with the wrong key format.

**Why it happens:** MSBuild properties are exposed to analyzers via `build_property.` prefix (lowercase), not the MSBuild property name casing.

**How to avoid:** Key must be `"build_property.DocCoverageThreshold"` (all lowercase prefix, property name case preserved).

**Warning signs:** `TryGetValue` returns `false` even when property is set in `.csproj`.

### Pitfall 3: AppHost SDK Switch Breaking Existing Project References

**What goes wrong:** Switching AppHost `.csproj` to `Sdk="Aspire.AppHost.Sdk/13.1"` silently drops `<IsPackable>false</IsPackable>` or other properties defined through `Directory.Build.props` if the Aspire SDK has different implicit imports.

**Why it happens:** `Aspire.AppHost.Sdk` has its own implicit MSBuild targets that may interact with `Directory.Build.props`.

**How to avoid:** Build and run full test suite immediately after SDK switch. Verify the generated `Projects.*` class appears by checking build output.

**Warning signs:** `CS0246: The type or namespace name 'Projects' could not be found` after AppHost SDK change.

### Pitfall 4: stdio MCP Transport + HTTP Health Endpoint Port Conflict

**What goes wrong:** McpServer tries to bind an HTTP port for health check, but Aspire injects `ASPNETCORE_URLS` pointing to a port that conflicts with another resource.

**Why it happens:** Aspire injects environment variables for endpoints declared with `WithHttpEndpoint`. If the McpServer's `Program.cs` doesn't use Kestrel/ASP.NET Core (it currently uses `Host.CreateApplicationBuilder`, not `WebApplication.CreateBuilder`), the health endpoint needs a separate lightweight HTTP listener.

**How to avoid:** Add a minimal health check listener using `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Diagnostics.HealthChecks` with `IHostedService` approach (not full Kestrel web server). Alternatively, switch McpServer to `WebApplication.CreateBuilder` and expose health alongside stdio. Document the chosen approach clearly.

**Warning signs:** `SocketException: address already in use` or Aspire dashboard showing resource stuck in "Starting".

### Pitfall 5: Verbose Telemetry Always On in Production

**What goes wrong:** Verbose span attributes (full input parameters, output sizes) are logged in all environments, bloating telemetry and potentially leaking sensitive file paths.

**Why it happens:** Forgetting to scope verbose sampling to Development environment.

**How to avoid:** Gate verbose tag population on `IHostEnvironment.IsDevelopment()` or a dedicated env var (e.g., `DOCAGENT_TELEMETRY_VERBOSE=true`). The `ActivitySource.StartActivity` pattern allows conditional tag setting without touching sampling.

**Warning signs:** Aspire dashboard traces include full file path arguments from production runs.

---

## Code Examples

Verified patterns from official and cross-referenced sources:

### Analyzer Diagnostic Descriptor with Warning Default

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix
// Pattern consistent with CONTEXT.md decisions

private static readonly DiagnosticDescriptor DocParityRule = new(
    id: "DOCAGENT001",
    title: "Public API member lacks documentation",
    messageFormat: "'{0}' is public but has no XML <summary> documentation",
    category: "Documentation",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true,
    helpLinkUri: "https://github.com/your-org/DocAgentFramework/docs/analyzers.md"
);
```

### Checking Public Accessibility Including `protected internal`

```csharp
// The project already maps ProtectedOrInternal in Ingestion (decision 02-03).
// Analyzers should be consistent: include protected, protected internal.

private static bool IsPublicOrProtected(ISymbol symbol) =>
    symbol.DeclaredAccessibility is
        Accessibility.Public or
        Accessibility.Protected or
        Accessibility.ProtectedOrInternal;
```

### Reading EditorConfig Threshold in Analyzer

```csharp
// Source: Roslyn AnalyzerConfigOptions — build_property prefix (MEDIUM confidence — community verified)
// In CompilationEndAction:
const string ThresholdKey = "build_property.DocCoverageThreshold";

if (!endContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions
    .TryGetValue(ThresholdKey, out var rawThreshold)
    || !int.TryParse(rawThreshold, out var threshold))
{
    threshold = 80; // default from CONTEXT.md
}
```

### Aspire AppHost Program.cs (HOST-01)

```csharp
// Source: https://aspire.dev/get-started/app-host/
// AppHost/Program.cs — after csproj Sdk switch to Aspire.AppHost.Sdk/13.1

var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR",
        builder.Configuration["DocAgent:ArtifactsDir"] ?? "./artifacts")
    .WithEnvironment("DOCAGENT_ALLOWLIST_PATHS",
        builder.Configuration["DocAgent:AllowlistPaths"] ?? "")
    .WithHttpEndpoint(port: 8089, name: "health")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
```

### ActivitySource Span in Tool Method (HOST-02)

```csharp
// DocAgent.McpServer/Tools/DocTools.cs (addition to existing tool methods)
using var activity = DocAgentTelemetry.Source.StartActivity(
    "tool.search_symbols",
    ActivityKind.Internal);

activity?.SetTag("tool.name", "search_symbols");
activity?.SetTag("tool.query", request.Query);

try
{
    var results = await _queryService.SearchAsync(request.Query, ct);
    activity?.SetTag("tool.result_count", results.Count);
    activity?.SetStatus(ActivityStatusCode.Ok);
    return results;
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

### Verbose Telemetry Gated on Environment

```csharp
// DocAgent.McpServer/Telemetry/DocAgentActivitySource.cs
public static class DocAgentTelemetry
{
    public const string SourceName = "DocAgent.McpServer";
    public static readonly ActivitySource Source = new(SourceName);

    // Set during startup from IHostEnvironment
    public static bool VerboseMode { get; set; }
}

// In tool: conditionally add verbose tags
if (DocAgentTelemetry.VerboseMode)
{
    activity?.SetTag("tool.input.query", request.Query);
    activity?.SetTag("tool.input.limit", request.Limit);
    activity?.SetTag("tool.output.bytes", resultJson.Length);
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `IsAspireHost=true` MSBuild property | `Sdk="Aspire.AppHost.Sdk/13.1"` in project file | Aspire 9.2 (2025) | Simpler; SDK generates `Projects.*` types automatically |
| `WithHttpsHealthCheck()` | `WithHttpHealthCheck()` (HTTP only for local) | Aspire 9.3 | `WithHttpsHealthCheck` marked obsolete in 9.3 |
| Manual `TracerProvider.Create()` | `builder.Services.AddOpenTelemetry().WithTracing()` | OTel .NET SDK 1.x | Integrates with DI lifecycle, avoids static global state |
| `[DiagnosticAnalyzer]` on same project as app | Separate `netstandard2.0` analyzer project | Best practice always, enforced by tooling | Allows host-agnostic loading (VS + CLI + Rider) |

**Deprecated/outdated:**
- `IsAspireHost`: Moved to SDK, no longer a project property to set manually.
- `WithHttpsHealthCheck()`: Obsoleted in Aspire 9.3 — use `WithHttpHealthCheck()`.
- `OpenTelemetry.Extensions.Hosting` `AddOpenTelemetryTracing()` (old name): Now `AddOpenTelemetry().WithTracing()`.

---

## Open Questions

1. **Health endpoint implementation strategy for stdio-only McpServer**
   - What we know: Current `Program.cs` uses `Host.CreateApplicationBuilder` (not `WebApplication`). Aspire's `WithHttpHealthCheck("/health")` needs a real HTTP listener. `Microsoft.Extensions.Diagnostics.HealthChecks` alone doesn't create an HTTP listener without ASP.NET Core.
   - What's unclear: Whether to add a minimal Kestrel endpoint to McpServer (requires `Microsoft.AspNetCore.App` reference and `WebApplication.CreateBuilder`) or use a lightweight `IHostedService` with `HttpListener`. The `WebApplication` approach would require `ASPNETCORE_URLS` management so Kestrel port doesn't contaminate stdout.
   - Recommendation: Switch McpServer to `WebApplication.CreateBuilder(args)` with `builder.WebHost.UseUrls("http://localhost:8089")` for health, keeping stdio for MCP transport. This is the cleanest approach and avoids raw `HttpListener`. Verify that `WebApplication.CreateBuilder` with `UseStdioServerTransport()` is compatible — the McpServer must not write anything to stdout except JSON-RPC (already handled via `LogToStandardErrorThreshold`).

2. **Roslyn 4.12.0 vs. 5.0.0 for ANLY-02 (semantic contract detection)**
   - What we know: The project pins Roslyn 4.12.0. ANLY-02 needs nullability annotation change detection.
   - What's unclear: Whether `IMethodSymbol.IsNullableAnnotationsEnabled` and related APIs are available in 4.12.0 or only in 5.0.0. STATE.md flags `[Dependency] Roslyn version: current pin is 4.12.0, research recommends upgrade to 5.0.0 for C# 14 semantic APIs`.
   - Recommendation: Implement ANLY-02 using APIs confirmed in 4.12.0 (`IParameterSymbol.NullableAnnotation`, `ITypeSymbol.NullableAnnotation`, throw statement syntax detection via `RegisterSyntaxNodeAction` for `ThrowStatementSyntax`). Defer 5.0.0 upgrade unless specific required API is missing.

3. **`ExcludeFromDocCoverage` attribute discovery across assemblies**
   - What we know: Analyzer checks `a.AttributeClass?.Name == "ExcludeFromDocCoverageAttribute"`. This works for the same assembly but may not find the attribute if it's in a different assembly not referenced by the analyzer.
   - What's unclear: Whether a well-known string comparison by `Name` is sufficient vs. full qualified name match.
   - Recommendation: Use full namespace-qualified comparison: `a.AttributeClass?.ToDisplayString() == "DocAgent.Analyzers.ExcludeFromDocCoverageAttribute"`. Define the attribute in the analyzer project itself (it can be `[Conditional("NEVER")]` so it's stripped from output) or in `DocAgent.Core` (with analyzer referencing Core).

---

## Sources

### Primary (HIGH confidence)

- `/websites/learn_microsoft_en-us_dotnet_csharp` (Context7) — Roslyn SDK tutorial, `DiagnosticAnalyzer`, `RegisterSymbolAction`, `GetDocumentationCommentXml`
- `/microsoft/aspire.dev` (Context7) — ConfigureOpenTelemetry pattern, AddServiceDefaults, OTLP exporter
- `/dotnet/aspire` (Context7) — AddProject, WithHealthCheck, health probe patterns, WaitFor
- [Microsoft Learn: Write your first analyzer](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) — Full tutorial (fetched, HIGH)
- [Roslyn GitHub: Analyzer Actions Semantics](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md) — RegisterSymbolStartAction/End docs (HIGH)
- [aspire.dev: Service Defaults](https://aspire.dev/fundamentals/service-defaults/) — AddServiceDefaults, ConfigureOpenTelemetry (fetched, HIGH)
- [aspire.dev: AppHost overview](https://aspire.dev/get-started/app-host/) — AddProject, WithReference patterns (fetched, HIGH)
- [aspire.dev: Aspire SDK](https://aspire.dev/get-started/aspire-sdk/) — Aspire.AppHost.Sdk project file setup (fetched, HIGH)

### Secondary (MEDIUM confidence)

- [WebSearch: Roslyn DiagnosticAnalyzer 2025](https://devblogs.microsoft.com/dotnet/how-to-write-a-roslyn-analyzer/) — Multiple verified sources agreeing on pattern
- [WebSearch: Aspire health checks](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/health-checks) — WithHttpHealthCheck breaking change in 9.3 confirmed
- [WebSearch: Analyzer NuGet packaging](https://aaronstannard.com/roslyn-nuget/) — `analyzers/dotnet/cs/` folder convention

### Tertiary (LOW confidence)

- GitHub Issue #11925 — Console app `AddProject` not calling `Main()` in Aspire 9.5/net10 RC1. Flagged as potential concern; verify against Aspire 13.1 behavior before plan execution.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Roslyn and OTel packages are stable, verified via Context7 + official docs
- Architecture: HIGH — Analyzer project pattern is well-established; Aspire wiring is MEDIUM due to non-HTTP process health check open question
- Pitfalls: HIGH — Most come from official docs or well-established ecosystem experience

**Research date:** 2026-02-27
**Valid until:** 2026-03-28 (30 days; Aspire minor versions release frequently — verify SDK version before planning)
