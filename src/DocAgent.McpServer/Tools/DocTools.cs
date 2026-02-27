using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

// Placeholder stub using 1.0.0 API — full implementation in Task 2
[McpServerToolType]
public sealed class DocTools
{
    [McpServerTool(Name = "search_symbols"), Description("Search symbols and documentation by keyword.")]
    public Task<string> SearchSymbols([Description("Search query")] string query)
    {
        // TODO: wire to IKnowledgeQueryService (Task 2)
        return Task.FromResult($"stub: search '{query}'");
    }
}
