namespace DocAgent.Core;

public enum SymbolKind
{
    Namespace,
    Type,
    Method,
    Property,
    Field,
    Event,
    Parameter
}

public readonly record struct SymbolId(string Value);

public sealed record SourceSpan(string FilePath, int StartLine, int StartColumn, int EndLine, int EndColumn);

public sealed record DocComment(
    string? Summary,
    string? Remarks,
    IReadOnlyDictionary<string, string> Params,
    string? Returns,
    IReadOnlyList<string> Examples);

public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    DocComment? Docs,
    SourceSpan? Span);

public enum SymbolEdgeKind
{
    Contains,
    Inherits,
    Implements,
    Calls,
    References
}

public sealed record SymbolEdge(SymbolId From, SymbolId To, SymbolEdgeKind Kind);

public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string SourceFingerprint,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges);
