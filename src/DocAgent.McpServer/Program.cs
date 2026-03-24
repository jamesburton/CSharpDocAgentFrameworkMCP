using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Filters;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Telemetry;
using DocAgent.McpServer.RateLimiting;
using DocAgent.McpServer.Validation;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// CLI subcommand routing — must happen before WebApplication.CreateBuilder
if (DocAgent.McpServer.Cli.CliRunner.IsCliCommand(args))
{
    var exitCode = await DocAgent.McpServer.Cli.CliRunner.RunAsync(args);
    return exitCode;
}

// Transport selection: --stdio uses stdio, otherwise HTTP on configurable port
var useStdio = args.Contains("--stdio", StringComparer.OrdinalIgnoreCase);

// Strip --stdio and --port from args before passing to WebApplicationBuilder
// (they are not recognized by the ASP.NET configuration system)
var filteredArgs = FilterTransportArgs(args);

var builder = WebApplication.CreateBuilder(filteredArgs);

// ALL logs must go to stderr — never stdout (stdout is reserved for MCP JSON-RPC framing in stdio mode)
builder.Logging.AddConsole(o =>
{
    o.LogToStandardErrorThreshold = LogLevel.Trace; // MCPS-06: all log levels to stderr
});

// Inject custom env var into config system (CLI > DOCAGENT_ARTIFACTS_DIR > appsettings.json)
// Expand env vars, tilde, and relative paths so %USERPROFILE%, $HOME, ~, and ..\relative all resolve.
var artifactsDirFromEnv = DocAgent.McpServer.Config.PathExpander.Expand(
    Environment.GetEnvironmentVariable("DOCAGENT_ARTIFACTS_DIR"));
if (artifactsDirFromEnv is not null)
    builder.Configuration["DocAgent:ArtifactsDir"] = artifactsDirFromEnv;

// Strongly-typed configuration from appsettings.json section "DocAgent"
builder.Services.Configure<DocAgentServerOptions>(
    builder.Configuration.GetSection("DocAgent"));

// Startup validation — fails fast on invalid config before accepting tool calls
builder.Services.AddHostedService<StartupValidator>();

// Security services
builder.Services.AddSingleton<PathAllowlist>();
builder.Services.AddSingleton<AuditLogger>();

// Rate limiting — separate token buckets for query and ingestion tools
builder.Services.AddSingleton<ToolRateLimiter>();

// Health checks for Aspire probing
builder.Services.AddHealthChecks();

// OpenTelemetry tracing + metrics + logging
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(DocAgentTelemetry.SourceName);
        tracing.AddOtlpExporter(); // reads OTEL_EXPORTER_OTLP_ENDPOINT env var
    })
    .WithMetrics(metrics =>
    {
        metrics.AddRuntimeInstrumentation();
        metrics.AddMeter("DocAgent.Ingestion");
        metrics.AddOtlpExporter();
    });

// Wire logs through OTel for Aspire dashboard visibility
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

// Set verbose mode from environment (default on in Development)
DocAgentTelemetry.VerboseMode =
    builder.Environment.IsDevelopment() ||
    Environment.GetEnvironmentVariable("DOCAGENT_TELEMETRY_VERBOSE") == "true";

// Register MSBuild components before any Workspaces are created
MSBuildLocator.RegisterDefaults();

// Core DI: SnapshotStore (singleton), BM25SearchIndex as ISearchIndex (singleton), KnowledgeQueryService (scoped)
builder.Services.AddDocAgent();

// Configure MCP transport
var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithToolsFromAssembly()    // Discovers [McpServerToolType] classes in this assembly
    .AddRateLimitFilter()       // Rate limit first — reject early before work
    .AddAuditFilter();          // Audit second — logs all calls including rate-limited

if (useStdio)
{
    mcpBuilder.WithStdioServerTransport();

    // In stdio mode, disable Kestrel HTTP listener to avoid port conflicts
    builder.WebHost.UseUrls(); // no URLs = no HTTP listener
}
else
{
    mcpBuilder.WithHttpTransport();

    // Port resolution: --port N > DOCAGENT_PORT env var > default 11877
    var port = ResolvePort(args);
    builder.WebHost.UseUrls($"http://localhost:{port}");
}

var app = builder.Build();

// Map health endpoint for Aspire dashboard probing (HTTP mode only)
if (!useStdio)
{
    app.MapHealthChecks("/health");
    app.MapMcp();
}

// Startup: load existing index if snapshot exists on disk
var store = app.Services.GetRequiredService<SnapshotStore>();
var index = app.Services.GetRequiredService<ISearchIndex>();
var latestSnapshot = await store.LoadLatestAsync();
if (latestSnapshot is not null)
    await index.IndexAsync(latestSnapshot, CancellationToken.None);

await app.RunAsync();
return 0;

// --- Helper methods ---

static int ResolvePort(string[] args)
{
    // Check --port N argument
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[i + 1], out var argPort) && argPort > 0)
            return argPort;
    }

    // Check DOCAGENT_PORT environment variable
    var envPort = Environment.GetEnvironmentVariable("DOCAGENT_PORT");
    if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out var port) && port > 0)
        return port;

    return 11877;
}

static string[] FilterTransportArgs(string[] args)
{
    var filtered = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--stdio", StringComparison.OrdinalIgnoreCase))
            continue;
        if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            i++; // skip the port value too
            continue;
        }
        filtered.Add(args[i]);
    }
    return filtered.ToArray();
}
