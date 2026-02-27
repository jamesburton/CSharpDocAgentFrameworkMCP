using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Filters;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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

// Security services
builder.Services.AddSingleton<PathAllowlist>();
builder.Services.AddSingleton<AuditLogger>();

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

// Core DI: SnapshotStore (singleton), BM25SearchIndex as ISearchIndex (singleton), KnowledgeQueryService (scoped)
builder.Services.AddDocAgent();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()    // Discovers [McpServerToolType] classes in this assembly
    .AddAuditFilter();          // From Filters/AuditFilter.cs — wraps every tool call

var app = builder.Build();

// Map health endpoint for Aspire dashboard probing
app.MapHealthChecks("/health");

// Startup: load existing index if snapshot exists on disk
var store = app.Services.GetRequiredService<SnapshotStore>();
var index = (BM25SearchIndex)app.Services.GetRequiredService<ISearchIndex>();
var snapshots = await store.ListAsync();
if (snapshots.Count > 0)
{
    var latest = snapshots.OrderByDescending(s => s.IngestedAt).First();
    var snapshot = await store.LoadAsync(latest.ContentHash);
    if (snapshot is not null)
        await index.IndexAsync(snapshot, CancellationToken.None);
}

await app.RunAsync();
