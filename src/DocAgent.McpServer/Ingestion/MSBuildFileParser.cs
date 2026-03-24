using System.Xml.Linq;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Lightweight parser for MSBuild <c>.targets</c> and <c>.props</c> files.
/// Uses <see cref="XDocument"/> (not Microsoft.Build evaluation) to extract
/// targets, properties, and task registrations as <see cref="SymbolNode"/>s.
/// </summary>
public static class MSBuildFileParser
{
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(string filePath)
    {
        if (!File.Exists(filePath))
            return ([], []);

        string xml;
        try
        {
            xml = File.ReadAllText(filePath);
        }
        catch
        {
            return ([], []);
        }

        return ParseContent(xml, filePath);
    }

    internal static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) ParseContent(
        string xml, string filePath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch
        {
            return ([], []);
        }

        var fileName = Path.GetFileName(filePath);
        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        var root = doc.Root;
        if (root is null)
            return ([], []);

        // Strip namespace for element matching
        var ns = root.Name.Namespace;

        // Parse <Target> elements
        foreach (var target in root.Descendants(ns + "Target"))
        {
            var name = target.Attribute("Name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var lineInfo = (System.Xml.IXmlLineInfo)target;
            var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;

            var symbolId = new SymbolId($"T:msbuild-target/{fileName}/{name}");

            var condition = target.Attribute("Condition")?.Value;
            var remarks = condition != null ? $"Condition: {condition}" : null;

            var docComment = new DocComment(
                Summary: $"MSBuild target '{name}'",
                Remarks: remarks,
                Params: new Dictionary<string, string>(),
                TypeParams: new Dictionary<string, string>(),
                Returns: null,
                Examples: [],
                Exceptions: [],
                SeeAlso: []);

            var span = new SourceSpan(filePath, line, 0, line, 0);

            var node = new SymbolNode(
                Id: symbolId,
                Kind: SymbolKind.BuildTarget,
                DisplayName: name,
                FullyQualifiedName: $"msbuild-target/{fileName}/{name}",
                PreviousIds: [],
                Accessibility: Accessibility.Public,
                Docs: docComment,
                Span: span,
                ReturnType: null,
                Parameters: [],
                GenericConstraints: []);

            nodes.Add(node);

            // DependsOnTargets
            var dependsOn = target.Attribute("DependsOnTargets")?.Value;
            if (!string.IsNullOrEmpty(dependsOn))
            {
                foreach (var dep in dependsOn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var depId = new SymbolId($"T:msbuild-target/{fileName}/{dep}");
                    edges.Add(new SymbolEdge(symbolId, depId, SymbolEdgeKind.DependsOn));
                }
            }

            // BeforeTargets
            var beforeTargets = target.Attribute("BeforeTargets")?.Value;
            if (!string.IsNullOrEmpty(beforeTargets))
            {
                foreach (var bt in beforeTargets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var btId = new SymbolId($"T:msbuild-target/{fileName}/{bt}");
                    edges.Add(new SymbolEdge(symbolId, btId, SymbolEdgeKind.DependsOn));
                }
            }

            // AfterTargets
            var afterTargets = target.Attribute("AfterTargets")?.Value;
            if (!string.IsNullOrEmpty(afterTargets))
            {
                foreach (var at in afterTargets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var atId = new SymbolId($"T:msbuild-target/{fileName}/{at}");
                    edges.Add(new SymbolEdge(symbolId, atId, SymbolEdgeKind.DependsOn));
                }
            }
        }

        // Parse <PropertyGroup> property definitions
        foreach (var propGroup in root.Descendants(ns + "PropertyGroup"))
        {
            foreach (var prop in propGroup.Elements())
            {
                var propName = prop.Name.LocalName;
                var lineInfo = (System.Xml.IXmlLineInfo)prop;
                var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;

                var symbolId = new SymbolId($"T:msbuild-prop/{fileName}/{propName}");

                var condition = prop.Attribute("Condition")?.Value;
                var propValue = prop.Value;
                var remarks = condition != null ? $"Condition: {condition}" : null;
                var summary = string.IsNullOrWhiteSpace(propValue)
                    ? $"MSBuild property '{propName}'"
                    : $"MSBuild property '{propName}' = {propValue}";

                var docComment = new DocComment(
                    Summary: summary,
                    Remarks: remarks,
                    Params: new Dictionary<string, string>(),
                    TypeParams: new Dictionary<string, string>(),
                    Returns: null,
                    Examples: [],
                    Exceptions: [],
                    SeeAlso: []);

                var span = new SourceSpan(filePath, line, 0, line, 0);

                var node = new SymbolNode(
                    Id: symbolId,
                    Kind: SymbolKind.BuildProperty,
                    DisplayName: propName,
                    FullyQualifiedName: $"msbuild-prop/{fileName}/{propName}",
                    PreviousIds: [],
                    Accessibility: Accessibility.Public,
                    Docs: docComment,
                    Span: span,
                    ReturnType: null,
                    Parameters: [],
                    GenericConstraints: []);

                nodes.Add(node);
            }
        }

        // Parse <UsingTask> elements
        foreach (var usingTask in root.Descendants(ns + "UsingTask"))
        {
            var taskName = usingTask.Attribute("TaskName")?.Value;
            if (string.IsNullOrEmpty(taskName))
                continue;

            var lineInfo = (System.Xml.IXmlLineInfo)usingTask;
            var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;

            var symbolId = new SymbolId($"T:msbuild-task/{fileName}/{taskName}");

            var assemblyFile = usingTask.Attribute("AssemblyFile")?.Value;
            var assemblyName = usingTask.Attribute("AssemblyName")?.Value;
            var assemblyRef = assemblyFile ?? assemblyName;

            var summary = assemblyRef != null
                ? $"MSBuild task '{taskName}' from {assemblyRef}"
                : $"MSBuild task '{taskName}'";

            var docComment = new DocComment(
                Summary: summary,
                Remarks: null,
                Params: new Dictionary<string, string>(),
                TypeParams: new Dictionary<string, string>(),
                Returns: null,
                Examples: [],
                Exceptions: [],
                SeeAlso: []);

            var span = new SourceSpan(filePath, line, 0, line, 0);

            var node = new SymbolNode(
                Id: symbolId,
                Kind: SymbolKind.BuildTask,
                DisplayName: taskName,
                FullyQualifiedName: $"msbuild-task/{fileName}/{taskName}",
                PreviousIds: [],
                Accessibility: Accessibility.Public,
                Docs: docComment,
                Span: span,
                ReturnType: null,
                Parameters: [],
                GenericConstraints: []);

            nodes.Add(node);

            // Create References edge for AssemblyFile/AssemblyName
            if (assemblyRef != null)
            {
                var assemblyId = new SymbolId($"T:assembly/{assemblyRef}");
                edges.Add(new SymbolEdge(symbolId, assemblyId, SymbolEdgeKind.References));
            }
        }

        return (nodes, edges);
    }
}
