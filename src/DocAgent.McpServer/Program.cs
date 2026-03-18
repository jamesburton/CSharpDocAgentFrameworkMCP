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

var builder = WebApplication.CreateBuilder(args);

// ALL logs must go to stderr — never stdout (stdout is reserved for MCP JSON-RPC framing)
builder.Logging.AddConsole(o =>
{
    o.LogToStandardErrorThreshold = LogLevel.Trace; // MCPS-06: all log levels to stderr
});

// Inject custom env var into config system (CLI > DOCAGENT_ARTIFACTS_DIR > appsettings.json)
var artifactsDirFromEnv = Environment.GetEnvironmentVariable("DOCAGENT_ARTIFACTS_DIR");
if (!string.IsNullOrWhiteSpace(artifactsDirFromEnv))
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

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()    // Discovers [McpServerToolType] classes in this assembly
    .AddRateLimitFilter()       // Rate limit first — reject early before work
    .AddAuditFilter();          // Audit second — logs all calls including rate-limited

var app = builder.Build();

// Map health endpoint for Aspire dashboard probing
app.MapHealthChecks("/health");

// Startup: load existing index if snapshot exists on disk
var store = app.Services.GetRequiredService<SnapshotStore>();
var index = app.Services.GetRequiredService<ISearchIndex>();
var snapshots = await store.ListAsync();
if (snapshots.Count > 0)
{
    var latest = snapshots.OrderByDescending(s => s.IngestedAt).First();
    var snapshot = await store.LoadAsync(latest.ContentHash);
    if (snapshot is not null)
        await index.IndexAsync(snapshot, CancellationToken.None);
}

await app.RunAsync();
return 0;
