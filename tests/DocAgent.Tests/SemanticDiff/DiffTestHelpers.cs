using DocAgent.Core;

namespace DocAgent.Tests.SemanticDiff;

internal static class DiffTestHelpers
{
    internal static SymbolGraphSnapshot BuildSnapshot(params SymbolNode[] nodes)
        => BuildSnapshot(nodes, Array.Empty<SymbolEdge>());

    internal static SymbolGraphSnapshot BuildSnapshot(SymbolNode[] nodes, SymbolEdge[] edges)
        => new SymbolGraphSnapshot(
            SchemaVersion: "1.0",
            ProjectName: "TestProject",
            SourceFingerprint: Guid.NewGuid().ToString("N"),
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList(),
            Edges: edges.OrderBy(e => e.From.Value).ThenBy(e => e.To.Value).ThenBy(e => e.Kind).ToList());

    internal static SymbolGraphSnapshot BuildSnapshot(string projectName, SymbolNode[] nodes, SymbolEdge[] edges)
        => new SymbolGraphSnapshot(
            SchemaVersion: "1.0",
            ProjectName: projectName,
            SourceFingerprint: Guid.NewGuid().ToString("N"),
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList(),
            Edges: edges.OrderBy(e => e.From.Value).ThenBy(e => e.To.Value).ThenBy(e => e.Kind).ToList());

    internal static SymbolNode BuildMethod(
        string id,
        Accessibility access = Accessibility.Public,
        string? returnType = "string",
        List<ParameterInfo>? parameters = null,
        List<GenericConstraint>? constraints = null,
        DocComment? docs = null)
        => new SymbolNode(
            Id: new SymbolId(id),
            Kind: SymbolKind.Method,
            DisplayName: id.Split('.').Last(),
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: access,
            Docs: docs,
            Span: null,
            ReturnType: returnType,
            Parameters: parameters ?? [],
            GenericConstraints: constraints ?? []);

    internal static SymbolNode BuildType(
        string id,
        Accessibility access = Accessibility.Public,
        List<GenericConstraint>? constraints = null,
        DocComment? docs = null)
        => new SymbolNode(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: id.Split('.').Last(),
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: access,
            Docs: docs,
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: constraints ?? []);

    internal static DocComment BuildDoc(string summary)
        => new DocComment(
            Summary: summary,
            Remarks: null,
            Params: new Dictionary<string, string>(),
            TypeParams: new Dictionary<string, string>(),
            Returns: null,
            Examples: [],
            Exceptions: [],
            SeeAlso: []);

    internal static ParameterInfo Param(string name, string typeName, string? defaultValue = null)
        => new ParameterInfo(name, typeName, defaultValue, false, false, false, false);
}
