using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Filters;
using DocAgent.McpServer.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

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

// Core DI: SnapshotStore (singleton), BM25SearchIndex as ISearchIndex (singleton), KnowledgeQueryService (scoped)
builder.Services.AddDocAgent();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()    // Discovers [McpServerToolType] classes in this assembly
    .AddAuditFilter();          // From Filters/AuditFilter.cs — wraps every tool call

var app = builder.Build();

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
