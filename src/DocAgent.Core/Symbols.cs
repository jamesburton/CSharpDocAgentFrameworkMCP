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

/// <summary>Structured parameter information for methods, indexers, and delegates.</summary>
public sealed record ParameterInfo(
    string Name,
    string TypeName,
    string? DefaultValue,
    bool IsParams,
    bool IsRef,
    bool IsOut,
    bool IsIn);

/// <summary>A generic type parameter constraint (e.g., "where T : class, IDisposable").</summary>
public sealed record GenericConstraint(
    string TypeParameterName,
    IReadOnlyList<string> Constraints);

public sealed record DocComment(
    string? Summary,
    string? Remarks,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyDictionary<string, string> TypeParams,
    string? Returns,
    IReadOnlyList<string> Examples,
    IReadOnlyList<(string Type, string Description)> Exceptions,
    IReadOnlyList<string> SeeAlso);

/// <summary>Classifies whether a symbol node was discovered in source or synthesized as a stub for an external reference.</summary>
public enum NodeKind
{
    /// <summary>A real symbol discovered from project source. Default value for backward compatibility.</summary>
    Real = 0,
    /// <summary>A stub node synthesized from a NuGet package or external assembly reference.</summary>
    Stub = 1
}

/// <summary>Classifies the scope of a symbol edge with respect to project boundaries.</summary>
public enum EdgeScope
{
    /// <summary>Both endpoints belong to the same project. Default value for backward compatibility.</summary>
    IntraProject = 0,
    /// <summary>Endpoints span two different projects within the same solution.</summary>
    CrossProject = 1,
    /// <summary>One endpoint is a stub node from an external assembly.</summary>
    External = 2
}

public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<GenericConstraint> GenericConstraints,
    string? ProjectOrigin = null,
    NodeKind NodeKind = NodeKind.Real);

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

public sealed record SymbolEdge(SymbolId From, SymbolId To, SymbolEdgeKind Kind, EdgeScope Scope = EdgeScope.IntraProject);

public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string ProjectName,
    string SourceFingerprint,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges,
    IngestionMetadata? IngestionMetadata = null,
    string? SolutionName = null);

public enum SerializationFormat
{
    MessagePack,
    Json,
    Tron
}
