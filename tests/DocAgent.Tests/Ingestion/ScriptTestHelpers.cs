using DocAgent.Core;

namespace DocAgent.Tests.Ingestion;

/// <summary>
/// Shared helpers for building mixed C#/script test snapshots.
/// </summary>
public static class ScriptTestHelpers
{
    private static readonly DocComment EmptyDoc = new(
        Summary: null,
        Remarks: null,
        Params: new Dictionary<string, string>(),
        TypeParams: new Dictionary<string, string>(),
        Returns: null,
        Examples: [],
        Exceptions: [],
        SeeAlso: []);

    public static SymbolNode BuildScriptNode(
        string id,
        SymbolKind kind,
        string? projectOrigin = null,
        DocComment? docs = null) =>
        new(
            Id: new SymbolId(id),
            Kind: kind,
            DisplayName: id,
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: docs,
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: [],
            ProjectOrigin: projectOrigin);

    public static SymbolNode BuildCSharpTypeNode(string fqn, string projectOrigin) =>
        new(
            Id: new SymbolId($"T:{fqn}"),
            Kind: SymbolKind.Type,
            DisplayName: fqn.Split('.')[^1],
            FullyQualifiedName: fqn,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: EmptyDoc,
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: [],
            ProjectOrigin: projectOrigin);

    public static SymbolNode BuildCSharpNamespaceNode(string ns, string projectOrigin) =>
        new(
            Id: new SymbolId($"N:{ns}"),
            Kind: SymbolKind.Namespace,
            DisplayName: ns,
            FullyQualifiedName: ns,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: EmptyDoc,
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: [],
            ProjectOrigin: projectOrigin);

    public static SymbolEdge BuildEdge(string from, string to, SymbolEdgeKind kind, EdgeScope scope = EdgeScope.IntraProject) =>
        new(new SymbolId(from), new SymbolId(to), kind, scope);

    public static SymbolGraphSnapshot BuildMixedSnapshot(
        IEnumerable<SymbolNode> nodes,
        IEnumerable<SymbolEdge> edges) =>
        new(
            SchemaVersion: "1.0",
            ProjectName: "TestProject",
            SourceFingerprint: "test-fingerprint",
            ContentHash: "test-hash",
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes.ToList(),
            Edges: edges.ToList());

    public static DocComment DocWithSummary(string summary) =>
        new(
            Summary: summary,
            Remarks: null,
            Params: new Dictionary<string, string>(),
            TypeParams: new Dictionary<string, string>(),
            Returns: null,
            Examples: [],
            Exceptions: [],
            SeeAlso: []);
}
