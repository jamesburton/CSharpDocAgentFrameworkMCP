using DocAgent.Core;
using DocAgent.McpServer.Review;
using DocAgent.Tests.SemanticDiff;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.ChangeReview;

/// <summary>
/// Unit tests for ChangeReviewer.Analyze covering all four unusual patterns,
/// severity mapping, trivial filtering, sorting, and overall risk computation.
/// </summary>
public class ChangeReviewerTests
{
    // ─── Test 1: Accessibility widening ─────────────────────────────────

    [Fact]
    public void Accessibility_Widening_Is_Flagged()
    {
        // Arrange: method changes from Private to Public
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod", access: Accessibility.Private));

        var after = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod", access: Accessibility.Public));

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.UnusualFindings.Should().ContainSingle(u => u.Kind == UnusualKind.AccessibilityWidening);

        // The corresponding ReviewFinding should be escalated to Warning (NonBreaking + unusual)
        var finding = report.Findings.FirstOrDefault(f =>
            f.SymbolId == "MyType.MyMethod" && f.Category == "Accessibility");
        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(ReviewSeverity.Warning);
    }

    // ─── Test 2: Nullability regression ─────────────────────────────────

    [Fact]
    public void Nullability_Regression_Is_Flagged()
    {
        // Arrange: return type changes from "string" to "string?"
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.GetValue", returnType: "string"));

        var after = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.GetValue", returnType: "string?"));

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.UnusualFindings.Should().ContainSingle(u => u.Kind == UnusualKind.NullabilityRegression);
    }

    // ─── Test 3: Mass signature change ──────────────────────────────────

    [Fact]
    public void Mass_Signature_Change_Is_Flagged()
    {
        // Arrange: 6 signature changes under the same parent type, constructed manually
        var parentId = new SymbolId("MyType");

        var signatureChanges = Enumerable.Range(1, 6).Select(i =>
            new SymbolChange(
                SymbolId: new SymbolId($"MyType.Method{i}"),
                BeforeSnapshotSymbolId: new SymbolId($"MyType.Method{i}"),
                AfterSnapshotSymbolId: new SymbolId($"MyType.Method{i}"),
                ParentSymbolId: parentId,
                ChangeType: ChangeType.Modified,
                Category: ChangeCategory.Signature,
                Severity: ChangeSeverity.Breaking,
                Description: $"Signature changed for Method{i}.",
                SignatureDetail: new SignatureChangeDetail($"Signature changed for Method{i}.",
                    [], "string", "int"),
                NullabilityDetail: null,
                ConstraintDetail: null,
                AccessibilityDetail: null,
                DependencyDetail: null,
                DocCommentDetail: null)
        ).ToList();

        var summary = new DiffSummary(
            TotalChanges: 6,
            Added: 0,
            Removed: 0,
            Modified: 6,
            Breaking: 6,
            NonBreaking: 0,
            Informational: 0);

        var diff = new SymbolDiff("v1", "v2", "TestProject", summary, signatureChanges);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.UnusualFindings.Should().ContainSingle(u => u.Kind == UnusualKind.MassSignatureChange);
    }

    // ─── Test 4: Constraint removal ─────────────────────────────────────

    [Fact]
    public void Constraint_Removal_Is_Flagged()
    {
        // Arrange: manually build a SymbolDiff with a constraint-removal change
        var constraintChange = new SymbolChange(
            SymbolId: new SymbolId("MyType"),
            BeforeSnapshotSymbolId: new SymbolId("MyType"),
            AfterSnapshotSymbolId: new SymbolId("MyType"),
            ParentSymbolId: null,
            ChangeType: ChangeType.Modified,
            Category: ChangeCategory.Constraint,
            Severity: ChangeSeverity.Breaking,
            Description: "Generic constraints changed for type parameter 'T'.",
            SignatureDetail: null,
            NullabilityDetail: null,
            ConstraintDetail: new ConstraintChangeDetail(
                "Generic constraints changed for type parameter 'T'.",
                "T",
                RemovedConstraints: ["class"],
                AddedConstraints: []),
            AccessibilityDetail: null,
            DependencyDetail: null,
            DocCommentDetail: null);

        var summary = new DiffSummary(1, 0, 0, 1, 1, 0, 0);
        var diff = new SymbolDiff("v1", "v2", "TestProject", summary, [constraintChange]);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.UnusualFindings.Should().ContainSingle(u => u.Kind == UnusualKind.ConstraintRemoval);
    }

    // ─── Test 5: Trivial changes filtered when not verbose ───────────────

    [Fact]
    public void Trivial_Changes_Filtered_When_Not_Verbose()
    {
        // Arrange: only doc-comment change (trivial)
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod",
                docs: DiffTestHelpers.BuildDoc("Old summary")));

        var after = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod",
                docs: DiffTestHelpers.BuildDoc("New summary")));

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff, verbose: false);

        // Assert
        report.Findings.Should().BeEmpty();
        report.Summary.TrivialFiltered.Should().BeGreaterThan(0);
    }

    // ─── Test 6: Trivial changes included when verbose ───────────────────

    [Fact]
    public void Trivial_Changes_Included_When_Verbose()
    {
        // Arrange: same doc-comment change
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod",
                docs: DiffTestHelpers.BuildDoc("Old summary")));

        var after = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.MyMethod",
                docs: DiffTestHelpers.BuildDoc("New summary")));

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff, verbose: true);

        // Assert
        report.Findings.Should().NotBeEmpty();
        report.Summary.TrivialFiltered.Should().Be(0);
    }

    // ─── Test 7: Breaking changes map to Breaking severity ────────────────

    [Fact]
    public void Breaking_Changes_Map_To_Breaking_Severity()
    {
        // Arrange: remove a public method → Breaking
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.PublicMethod", access: Accessibility.Public));

        var after = DiffTestHelpers.BuildSnapshot(); // empty — method removed

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.Findings.Should().ContainSingle(f => f.Severity == ReviewSeverity.Breaking);
    }

    // ─── Test 8: Overall risk is high when breaking present ──────────────

    [Fact]
    public void Overall_Risk_Is_High_When_Breaking_Present()
    {
        // Arrange: removed public method → Breaking
        var before = DiffTestHelpers.BuildSnapshot(
            DiffTestHelpers.BuildMethod("MyType.PublicMethod", access: Accessibility.Public));

        var after = DiffTestHelpers.BuildSnapshot();

        var diff = SymbolGraphDiffer.Diff(before, after);

        // Act
        var report = ChangeReviewer.Analyze(diff);

        // Assert
        report.Summary.OverallRisk.Should().Be("high");
    }

    // ─── Test 9: Findings sorted by severity then SymbolId ───────────────

    [Fact]
    public void Findings_Sorted_By_Severity_Then_SymbolId()
    {
        // Arrange: manually build a mix of Breaking and Info findings
        var changes = new List<SymbolChange>
        {
            // Info (doc-comment, verbose mode)
            new(
                SymbolId: new SymbolId("Bravo.Method"),
                BeforeSnapshotSymbolId: new SymbolId("Bravo.Method"),
                AfterSnapshotSymbolId: new SymbolId("Bravo.Method"),
                ParentSymbolId: null,
                ChangeType: ChangeType.Modified,
                Category: ChangeCategory.DocComment,
                Severity: ChangeSeverity.Informational,
                Description: "Doc changed.",
                SignatureDetail: null, NullabilityDetail: null, ConstraintDetail: null,
                AccessibilityDetail: null, DependencyDetail: null,
                DocCommentDetail: new DocCommentChangeDetail("Doc changed.", null, null)),

            // Breaking
            new(
                SymbolId: new SymbolId("Alpha.Method"),
                BeforeSnapshotSymbolId: new SymbolId("Alpha.Method"),
                AfterSnapshotSymbolId: null,
                ParentSymbolId: null,
                ChangeType: ChangeType.Removed,
                Category: ChangeCategory.Signature,
                Severity: ChangeSeverity.Breaking,
                Description: "Removed.",
                SignatureDetail: null, NullabilityDetail: null, ConstraintDetail: null,
                AccessibilityDetail: null, DependencyDetail: null, DocCommentDetail: null),

            // Breaking (second, should come after Alpha alphabetically)
            new(
                SymbolId: new SymbolId("Charlie.Method"),
                BeforeSnapshotSymbolId: new SymbolId("Charlie.Method"),
                AfterSnapshotSymbolId: null,
                ParentSymbolId: null,
                ChangeType: ChangeType.Removed,
                Category: ChangeCategory.Signature,
                Severity: ChangeSeverity.Breaking,
                Description: "Removed.",
                SignatureDetail: null, NullabilityDetail: null, ConstraintDetail: null,
                AccessibilityDetail: null, DependencyDetail: null, DocCommentDetail: null),
        };

        var summary = new DiffSummary(3, 0, 2, 1, 2, 0, 1);
        var diff = new SymbolDiff("v1", "v2", "TestProject", summary, changes);

        // Act
        var report = ChangeReviewer.Analyze(diff, verbose: true);

        // Assert — Breaking findings come first, sorted alpha within tier; Info last
        var findingsList = report.Findings.ToList();
        findingsList.Should().HaveCountGreaterThanOrEqualTo(2);

        var breakingFindings = findingsList.Where(f => f.Severity == ReviewSeverity.Breaking).ToList();
        breakingFindings.Should().HaveCountGreaterThanOrEqualTo(2);

        // First breaking should be Alpha, second Charlie
        breakingFindings[0].SymbolId.Should().Be("Alpha.Method");
        breakingFindings[1].SymbolId.Should().Be("Charlie.Method");

        // All Breaking findings come before any Info findings
        int lastBreakingIdx = findingsList.FindLastIndex(f => f.Severity == ReviewSeverity.Breaking);
        int firstInfoIdx = findingsList.FindIndex(f => f.Severity == ReviewSeverity.Info);
        if (firstInfoIdx >= 0)
        {
            lastBreakingIdx.Should().BeLessThan(firstInfoIdx);
        }
    }
}
