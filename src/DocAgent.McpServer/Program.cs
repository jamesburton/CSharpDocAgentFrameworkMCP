using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o =>
{
    // MCP servers often run as stdio helpers; keep logs on stderr
    o.LogToStandardErrorThreshold = LogLevel.Information;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// TODO: register DocAgent services (ingestion, indexing, query) here.

await builder.Build().RunAsync();
