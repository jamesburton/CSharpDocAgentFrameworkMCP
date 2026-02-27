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

// Strongly-typed configuration from appsettings.json section "DocAgent"
builder.Services.Configure<DocAgentServerOptions>(
    builder.Configuration.GetSection("DocAgent"));

// Security services
builder.Services.AddSingleton<PathAllowlist>();
builder.Services.AddSingleton<AuditLogger>();

// TODO: Register IKnowledgeQueryService, ISearchIndex, SnapshotStore from Phase 4 output.
// The MCP server will fail at runtime if IKnowledgeQueryService is not registered.
// Integration test wiring is handled in Plan 05-03.

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()    // Discovers [McpServerToolType] classes in this assembly
    .AddAuditFilter();          // From Filters/AuditFilter.cs — wraps every tool call

await builder.Build().RunAsync();
