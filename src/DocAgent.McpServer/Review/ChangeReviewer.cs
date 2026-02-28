using DocAgent.Core;

namespace DocAgent.McpServer.Review;

/// <summary>
/// Pure-logic static service that inspects a <see cref="SymbolDiff"/> for unusual
/// change patterns and produces a structured <see cref="ChangeReviewReport"/>.
///
/// Detects four anomaly patterns:
/// <list type="bullet">
///   <item>Accessibility widening (e.g. Private → Public)</item>
///   <item>Nullability regression (non-nullable becoming nullable)</item>
///   <item>Mass signature changes (more than 5 signatures changed in one type)</item>
///   <item>Constraint removal (generic constraints removed)</item>
/// </list>
/// </summary>
public static class ChangeReviewer
{
    private const int MassChangeThreshold = 5;

    // Rank used to determine if accessibility is widened (higher = more visible)
    private static readonly Dictionary<Accessibility, int> AccessibilityRank = new()
    {
        [Accessibility.Private]           = 0,
        [Accessibility.PrivateProtected]  = 1,
        [Accessibility.Internal]          = 2,
        [Accessibility.Protected]         = 3,
        [Accessibility.ProtectedInternal] = 4,
        [Accessibility.Public]            = 5,
    };

    /// <summary>
    /// Analyzes the given <paramref name="diff"/> for unusual patterns and
    /// produces a <see cref="ChangeReviewReport"/> with severity-classified findings.
    /// </summary>
    /// <param name="diff">The symbol diff to analyze.</param>
    /// <param name="verbose">
    /// When <c>false</c> (default), trivial doc-comment + Informational changes are
    /// excluded from Findings but counted in <see cref="ReviewSummary.TrivialFiltered"/>.
    /// When <c>true</c>, all changes are included.
    /// </param>
    public static ChangeReviewReport Analyze(SymbolDiff diff, bool verbose = false)
    {
        // Step 1: Detect unusual patterns first (needed for severity escalation)
        var unusualFindings = DetectUnusualFindings(diff);
        var unusualSymbolIds = new HashSet<string>(unusualFindings.Select(u => u.SymbolId), StringComparer.Ordinal);

        // Step 2: Build findings from changes, grouped by SymbolId
        var groups = diff.Changes
            .GroupBy(c => c.SymbolId.Value, StringComparer.Ordinal);

        var allFindings = new List<ReviewFinding>();
        int trivialFiltered = 0;

        foreach (var group in groups)
        {
            foreach (var change in group)
            {
                // Determine if this change is trivial (doc-only + Informational)
                bool isTrivial = change.Severity == ChangeSeverity.Informational
                              && change.Category == ChangeCategory.DocComment;

                if (isTrivial && !verbose)
                {
                    trivialFiltered++;
                    continue;
                }

                // Map severity
                var reviewSeverity = MapSeverity(change, unusualSymbolIds);

                // Build before/after strings from detail fields
                var (before, after) = ExtractBeforeAfter(change);

                // Build remediation text
                var remediation = BuildRemediation(change);

                allFindings.Add(new ReviewFinding(
                    SymbolId: change.SymbolId.Value,
                    DisplayName: change.SymbolId.Value.Split('.').Last(),
                    Severity: reviewSeverity,
                    Category: change.Category.ToString(),
                    Description: change.Description,
                    Before: before,
                    After: after,
                    ImpactScope: [],
                    Remediation: remediation));
            }
        }

        // Step 3: Sort findings — Breaking first, then Warning, then Info; within each tier, by SymbolId
        allFindings.Sort((x, y) =>
        {
            int sevCmp = x.Severity.CompareTo(y.Severity);
            return sevCmp != 0 ? sevCmp : StringComparer.Ordinal.Compare(x.SymbolId, y.SymbolId);
        });

        // Step 4: Compute summary
        int breakingCount = allFindings.Count(f => f.Severity == ReviewSeverity.Breaking);
        int warningCount  = allFindings.Count(f => f.Severity == ReviewSeverity.Warning);
        int infoCount     = allFindings.Count(f => f.Severity == ReviewSeverity.Info);

        string overallRisk = breakingCount > 0 ? "high"
                           : warningCount  > 0 ? "medium"
                           : "low";

        var summary = new ReviewSummary(
            Breaking:        breakingCount,
            Warning:         warningCount,
            Info:            infoCount,
            TrivialFiltered: trivialFiltered,
            OverallRisk:     overallRisk);

        return new ChangeReviewReport(
            BeforeVersion:  diff.BeforeSnapshotVersion,
            AfterVersion:   diff.AfterSnapshotVersion,
            Summary:        summary,
            Findings:       allFindings,
            UnusualFindings: unusualFindings);
    }

    // ── Unusual Pattern Detection ─────────────────────────────────────────

    private static IReadOnlyList<UnusualFinding> DetectUnusualFindings(SymbolDiff diff)
    {
        var findings = new List<UnusualFinding>();

        // Mass signature change: group Signature changes by ParentSymbolId
        var sigByParent = diff.Changes
            .Where(c => c.Category == ChangeCategory.Signature && c.ParentSymbolId != null)
            .GroupBy(c => c.ParentSymbolId!.Value.Value, StringComparer.Ordinal);

        foreach (var group in sigByParent)
        {
            if (group.Count() > MassChangeThreshold)
            {
                findings.Add(new UnusualFinding(
                    Kind: UnusualKind.MassSignatureChange,
                    SymbolId: group.Key,
                    Description: $"Type '{group.Key}' has {group.Count()} signature changes, exceeding the threshold of {MassChangeThreshold}.",
                    Remediation: "Review whether this is a planned API redesign. Consider a compatibility shim or versioned overloads to avoid breaking callers."));
            }
        }

        foreach (var change in diff.Changes)
        {
            switch (change.Category)
            {
                case ChangeCategory.Accessibility when change.AccessibilityDetail is { } accDetail:
                    if (IsAccessibilityWidening(accDetail.OldAccessibility, accDetail.NewAccessibility))
                    {
                        findings.Add(new UnusualFinding(
                            Kind: UnusualKind.AccessibilityWidening,
                            SymbolId: change.SymbolId.Value,
                            Description: $"Accessibility of '{change.SymbolId.Value}' widened from {accDetail.OldAccessibility} to {accDetail.NewAccessibility}.",
                            Remediation: "Verify intent: widening accessibility increases public API surface. Ensure this symbol is ready for external consumption."));
                    }
                    break;

                case ChangeCategory.Nullability when change.NullabilityDetail is { } nullDetail:
                    if (IsNullabilityRegression(nullDetail.OldAnnotation, nullDetail.NewAnnotation))
                    {
                        findings.Add(new UnusualFinding(
                            Kind: UnusualKind.NullabilityRegression,
                            SymbolId: change.SymbolId.Value,
                            Description: $"Return type of '{change.SymbolId.Value}' became nullable ('{nullDetail.OldAnnotation}' → '{nullDetail.NewAnnotation}'). Callers must handle null.",
                            Remediation: "Ensure all callers handle the nullable return. Consider whether null is intentional or an error path should be thrown instead."));
                    }
                    break;

                case ChangeCategory.Constraint when change.ConstraintDetail is { } constraintDetail:
                    if (constraintDetail.RemovedConstraints.Count > 0)
                    {
                        findings.Add(new UnusualFinding(
                            Kind: UnusualKind.ConstraintRemoval,
                            SymbolId: change.SymbolId.Value,
                            Description: $"Generic constraints removed from '{change.SymbolId.Value}' for type parameter '{constraintDetail.TypeParameterName}': [{string.Join(", ", constraintDetail.RemovedConstraints)}].",
                            Remediation: "Verify that removing constraints does not break callers that relied on them. The type parameter is now less restricted."));
                    }
                    break;
            }
        }

        return findings;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsAccessibilityWidening(Accessibility oldAcc, Accessibility newAcc)
        => AccessibilityRank.TryGetValue(oldAcc, out int oldRank)
        && AccessibilityRank.TryGetValue(newAcc, out int newRank)
        && newRank > oldRank;

    private static bool IsNullabilityRegression(string? oldAnnotation, string? newAnnotation)
    {
        if (oldAnnotation is null || newAnnotation is null)
            return false;

        // Regression: old did NOT end with '?', new DOES end with '?'
        bool oldNullable = oldAnnotation.TrimEnd().EndsWith('?');
        bool newNullable = newAnnotation.TrimEnd().EndsWith('?');
        return !oldNullable && newNullable;
    }

    private static ReviewSeverity MapSeverity(SymbolChange change, HashSet<string> unusualSymbolIds)
    {
        return change.Severity switch
        {
            ChangeSeverity.Breaking      => ReviewSeverity.Breaking,
            ChangeSeverity.NonBreaking   => unusualSymbolIds.Contains(change.SymbolId.Value)
                                               ? ReviewSeverity.Warning
                                               : ReviewSeverity.Info,
            ChangeSeverity.Informational => ReviewSeverity.Info,
            _                            => ReviewSeverity.Info,
        };
    }

    private static (string? before, string? after) ExtractBeforeAfter(SymbolChange change)
    {
        return change.Category switch
        {
            ChangeCategory.Signature     => (change.SignatureDetail?.OldReturnType,
                                             change.SignatureDetail?.NewReturnType),
            ChangeCategory.Nullability   => (change.NullabilityDetail?.OldAnnotation,
                                             change.NullabilityDetail?.NewAnnotation),
            ChangeCategory.Constraint    => (change.ConstraintDetail is { } cd
                                                ? $"where {cd.TypeParameterName} : [{string.Join(", ", cd.RemovedConstraints.Concat(cd.AddedConstraints.Select(c => "+" + c)))}]"
                                                : null, null),
            ChangeCategory.Accessibility => (change.AccessibilityDetail?.OldAccessibility.ToString(),
                                             change.AccessibilityDetail?.NewAccessibility.ToString()),
            ChangeCategory.Dependency    => (change.DependencyDetail is { } dd
                                                ? $"{dd.RemovedEdges.Count} edges removed"
                                                : null,
                                             change.DependencyDetail is { } dda
                                                ? $"{dda.AddedEdges.Count} edges added"
                                                : null),
            ChangeCategory.DocComment    => (change.DocCommentDetail?.OldDocs?.Summary,
                                             change.DocCommentDetail?.NewDocs?.Summary),
            _                            => (null, null),
        };
    }

    private static string? BuildRemediation(SymbolChange change)
    {
        return change.Category switch
        {
            ChangeCategory.Signature when change.Severity == ChangeSeverity.Breaking
                => "Update all callers to match the new signature.",
            ChangeCategory.Signature
                => "Review call sites — signature changed but impact may be limited.",
            ChangeCategory.Nullability
                => "Ensure all callers handle the updated nullability annotation.",
            ChangeCategory.Constraint
                => "Verify that removing constraints does not break callers that relied on them.",
            ChangeCategory.Accessibility
                => "Verify intent: widening accessibility increases public API surface.",
            ChangeCategory.Dependency
                => "Review dependency edge changes for unintended coupling.",
            ChangeCategory.DocComment
                => null,
            _ => null,
        };
    }
}
