using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

[McpTool]
public static class DocTools
{
    [McpToolMethod, Description("Search symbols and documentation by keyword.")]
    public static Task<string> SearchSymbols(string query)
    {
        // TODO: wire to IKnowledgeQueryService
        return Task.FromResult($"stub: search '{query}'");
    }

    [McpToolMethod, Description("Get a symbol by its stable SymbolId.")]
    public static Task<string> GetSymbol(string symbolId)
    {
        // TODO: wire to IKnowledgeQueryService
        return Task.FromResult($"stub: get '{symbolId}'");
    }
}
