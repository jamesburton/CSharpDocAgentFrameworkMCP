namespace DocAgent.McpServer.Review;

// ── Severity ──────────────────────────────────────────────────────────────

/// <summary>Three-tier output severity mapped from Phase 9 ChangeSeverity.</summary>
public enum ReviewSeverity { Breaking, Warning, Info }

// ── UnusualKind ───────────────────────────────────────────────────────────

/// <summary>Category of unusual change pattern detected by ChangeReviewer.</summary>
public enum UnusualKind
{
    AccessibilityWidening,
    NullabilityRegression,
    MassSignatureChange,
    ConstraintRemoval,
}

// ── Findings ──────────────────────────────────────────────────────────────

/// <summary>
/// A single review finding for a symbol, representing a notable change grouped
/// from one or more SymbolChanges with before/after context and remediation text.
/// </summary>
public sealed record ReviewFinding(
    string SymbolId,
    string DisplayName,
    ReviewSeverity Severity,
    string Category,
    string Description,
    string? Before,
    string? After,
    IReadOnlyList<string> ImpactScope,
    string? Remediation);

/// <summary>
/// An unusual pattern finding produced by proactive anomaly detection in ChangeReviewer.
/// </summary>
public sealed record UnusualFinding(
    UnusualKind Kind,
    string SymbolId,
    string Description,
    string Remediation);

// ── Summary and Report ────────────────────────────────────────────────────

/// <summary>Aggregate statistics for a change review report.</summary>
public sealed record ReviewSummary(
    int Breaking,
    int Warning,
    int Info,
    int TrivialFiltered,
    string OverallRisk);

/// <summary>
/// The complete, immutable review report produced by ChangeReviewer.Analyze.
/// </summary>
public sealed record ChangeReviewReport(
    string BeforeVersion,
    string AfterVersion,
    ReviewSummary Summary,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<UnusualFinding> UnusualFindings);
