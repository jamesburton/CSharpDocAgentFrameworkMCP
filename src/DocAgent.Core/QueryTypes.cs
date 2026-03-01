namespace DocAgent.Core;

/// <summary>Indicates the category of failure for a query operation.</summary>
public enum QueryErrorKind
{
    NotFound,
    SnapshotMissing,
    StaleIndex,
    InvalidInput,
}

/// <summary>Wraps a query outcome as either a successful value or a typed error.</summary>
public sealed record QueryResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public QueryErrorKind? Error { get; init; }
    public string? ErrorMessage { get; init; }

    private QueryResult() { }

    public static QueryResult<T> Ok(T value)
        => new() { Success = true, Value = value };

    public static QueryResult<T> Fail(QueryErrorKind error, string? message = null)
        => new() { Success = false, Error = error, ErrorMessage = message };
}

/// <summary>Metadata envelope wrapping every query response payload.</summary>
public sealed record ResponseEnvelope<T>(
    T Payload,
    string SnapshotVersion,
    DateTimeOffset Timestamp,
    bool IsStale,
    TimeSpan QueryDuration);

/// <summary>A ranked search result item.</summary>
public sealed record SearchResultItem(
    SymbolId Id,
    double Score,
    string Snippet,
    SymbolKind Kind,
    string DisplayName,
    string? ProjectName = null);

/// <summary>A symbol with navigation hints for parent, children, and related symbols.</summary>
public sealed record SymbolDetail(
    SymbolNode Node,
    SymbolId? ParentId,
    IReadOnlyList<SymbolId> ChildIds,
    IReadOnlyList<SymbolId> RelatedIds);

/// <summary>Indicates how a symbol changed between two snapshots.</summary>
public enum DiffChangeKind
{
    Added,
    Removed,
    Modified,
}

/// <summary>A single entry in a snapshot diff result.</summary>
public sealed record DiffEntry(
    SymbolId Id,
    DiffChangeKind ChangeKind,
    string Summary);

/// <summary>The result of comparing two snapshots.</summary>
public sealed record GraphDiff(IReadOnlyList<DiffEntry> Entries);
