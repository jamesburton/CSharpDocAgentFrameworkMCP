namespace DocAgent.Core;

// ── Enums ────────────────────────────────────────────────────────────────

public enum ChangeType { Added, Removed, Modified }

public enum ChangeCategory
{
    Signature,
    Nullability,
    Constraint,
    Accessibility,
    Dependency,
    DocComment,
}

public enum ChangeSeverity { Breaking, NonBreaking, Informational }

// ── Change Detail Records (concrete, no abstract base — MessagePack safe) ──

/// <summary>Detail for a parameter-level change within a signature change.</summary>
public sealed record ParameterChange(
    ChangeType ChangeType,
    string? ParameterName,
    string? OldType,
    string? NewType,
    string? OldDefault,
    string? NewDefault);

/// <summary>Detail for signature changes (return type, parameters).</summary>
public sealed record SignatureChangeDetail(
    string Description,
    IReadOnlyList<ParameterChange> ParameterChanges,
    string? OldReturnType,
    string? NewReturnType);

/// <summary>Detail for nullable annotation changes.</summary>
public sealed record NullabilityChangeDetail(
    string Description,
    string? OldAnnotation,
    string? NewAnnotation);

/// <summary>Detail for generic constraint changes.</summary>
public sealed record ConstraintChangeDetail(
    string Description,
    string TypeParameterName,
    IReadOnlyList<string> RemovedConstraints,
    IReadOnlyList<string> AddedConstraints);

/// <summary>Detail for accessibility modifier changes.</summary>
public sealed record AccessibilityChangeDetail(
    string Description,
    Accessibility OldAccessibility,
    Accessibility NewAccessibility);

/// <summary>Detail for dependency (edge) changes.</summary>
public sealed record DependencyChangeDetail(
    string Description,
    IReadOnlyList<SymbolEdge> RemovedEdges,
    IReadOnlyList<SymbolEdge> AddedEdges);

/// <summary>Detail for documentation comment changes.</summary>
public sealed record DocCommentChangeDetail(
    string Description,
    DocComment? OldDocs,
    DocComment? NewDocs);

// ── SymbolChange ─────────────────────────────────────────────────────────

/// <summary>
/// A single change entry in a symbol diff. Uses per-category nullable detail
/// fields instead of a polymorphic base to ensure MessagePack compatibility
/// with ContractlessStandardResolver. Exactly one detail field is non-null,
/// matching the Category value.
/// </summary>
public sealed record SymbolChange(
    SymbolId SymbolId,
    SymbolId? BeforeSnapshotSymbolId,
    SymbolId? AfterSnapshotSymbolId,
    SymbolId? ParentSymbolId,
    ChangeType ChangeType,
    ChangeCategory Category,
    ChangeSeverity Severity,
    string Description,
    SignatureChangeDetail? SignatureDetail,
    NullabilityChangeDetail? NullabilityDetail,
    ConstraintChangeDetail? ConstraintDetail,
    AccessibilityChangeDetail? AccessibilityDetail,
    DependencyChangeDetail? DependencyDetail,
    DocCommentChangeDetail? DocCommentDetail);

// ── DiffSummary ──────────────────────────────────────────────────────────

/// <summary>Summary statistics for quick triage of a diff result.</summary>
public sealed record DiffSummary(
    int TotalChanges,
    int Added,
    int Removed,
    int Modified,
    int Breaking,
    int NonBreaking,
    int Informational);

// ── SymbolDiff (top-level result) ────────────────────────────────────────

/// <summary>
/// The complete, immutable result of diffing two SymbolGraphSnapshots.
/// Changes are a flat list sorted deterministically by SymbolId then Category.
/// Consumers (Phase 11 MCP tools) filter as needed.
/// </summary>
public sealed record SymbolDiff(
    string BeforeSnapshotVersion,
    string AfterSnapshotVersion,
    string ProjectName,
    DiffSummary Summary,
    IReadOnlyList<SymbolChange> Changes);
