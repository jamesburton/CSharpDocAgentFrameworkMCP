namespace DocAgent.Core;

public enum SymbolKind
{
    Namespace,
    Type,
    Method,
    Property,
    Field,
    Event,
    Parameter,
    Constructor,
    Delegate,
    Indexer,
    Operator,
    Destructor,
    EnumMember,
    TypeParameter
}

public enum Accessibility
{
    Public,
    Internal,
    Protected,
    Private,
    ProtectedInternal,
    PrivateProtected
}

public readonly record struct SymbolId(string Value);

public sealed record SourceSpan(string FilePath, int StartLine, int StartColumn, int EndLine, int EndColumn);

public sealed record DocComment(
    string? Summary,
    string? Remarks,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyDictionary<string, string> TypeParams,
    string? Returns,
    IReadOnlyList<string> Examples,
    IReadOnlyList<(string Type, string Description)> Exceptions,
    IReadOnlyList<string> SeeAlso);

public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span);

public enum SymbolEdgeKind
{
    Contains,
    Inherits,
    Implements,
    Calls,
    References,
    Overrides,
    Returns
}

public sealed record SymbolEdge(SymbolId From, SymbolId To, SymbolEdgeKind Kind);

public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string ProjectName,
    string SourceFingerprint,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges);
