using System.Text.Json;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Parses <c>.config/dotnet-tools.json</c> manifest files and produces
/// <see cref="SymbolNode"/>s of kind <see cref="SymbolKind.Tool"/>.
/// </summary>
public static class DotnetToolsParser
{
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(string filePath)
    {
        if (!File.Exists(filePath))
            return ([], []);

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch
        {
            return ([], []);
        }

        return ParseContent(json, filePath);
    }

    internal static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) ParseContent(
        string json, string filePath)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return ([], []);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("tools", out var toolsElement)
                || toolsElement.ValueKind != JsonValueKind.Object)
            {
                return ([], []);
            }

            var nodes = new List<SymbolNode>();
            // Read the file lines for line-number mapping
            var lines = File.Exists(filePath) ? File.ReadAllLines(filePath) : [];

            foreach (var toolProp in toolsElement.EnumerateObject())
            {
                var toolName = toolProp.Name;
                var toolObj = toolProp.Value;

                var version = toolObj.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String
                    ? vProp.GetString() ?? "unknown"
                    : "unknown";

                var commands = new List<string>();
                if (toolObj.TryGetProperty("commands", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cmd in cmdProp.EnumerateArray())
                    {
                        if (cmd.ValueKind == JsonValueKind.String)
                            commands.Add(cmd.GetString()!);
                    }
                }

                var commandsStr = commands.Count > 0
                    ? string.Join(", ", commands)
                    : "(none)";

                var lineNumber = FindLineNumber(lines, $"\"{toolName}\"");

                var symbolId = new SymbolId($"T:dotnet-tool/{toolName}");
                var fqn = $"dotnet-tool/{toolName}@{version}";
                var summary = $"dotnet tool v{version}; commands: {commandsStr}";

                var docComment = new DocComment(
                    Summary: summary,
                    Remarks: null,
                    Params: new Dictionary<string, string>(),
                    TypeParams: new Dictionary<string, string>(),
                    Returns: null,
                    Examples: [],
                    Exceptions: [],
                    SeeAlso: []);

                var span = new SourceSpan(filePath, lineNumber, 0, lineNumber, 0);

                var node = new SymbolNode(
                    Id: symbolId,
                    Kind: SymbolKind.Tool,
                    DisplayName: toolName,
                    FullyQualifiedName: fqn,
                    PreviousIds: [],
                    Accessibility: Accessibility.Public,
                    Docs: docComment,
                    Span: span,
                    ReturnType: null,
                    Parameters: [],
                    GenericConstraints: []);

                nodes.Add(node);
            }

            return (nodes, []);
        }
    }

    private static int FindLineNumber(string[] lines, string needle)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.Ordinal))
                return i + 1; // 1-based
        }
        return 1;
    }
}
