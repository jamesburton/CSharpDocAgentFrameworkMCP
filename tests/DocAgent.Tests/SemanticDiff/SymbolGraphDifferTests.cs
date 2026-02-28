using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class SymbolGraphDifferTests
{
    [Fact]
    public void Diff_throws_for_different_project_names()
    {
        var before = BuildSnapshot("ProjectA", [], []);
        var after  = BuildSnapshot("ProjectB", [], []);

        var act = () => SymbolGraphDiffer.Diff(before, after);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ProjectA*ProjectB*");
    }

    [Fact]
    public void Diff_detects_added_symbol()
    {
        var before = BuildSnapshot();
        var method = BuildMethod("TestProject.MyMethod");
        var after  = BuildSnapshot(method);

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().HaveCount(1);
        diff.Changes[0].ChangeType.Should().Be(ChangeType.Added);
        diff.Changes[0].SymbolId.Value.Should().Be("TestProject.MyMethod");
    }

    [Fact]
    public void Diff_detects_removed_symbol()
    {
        var method = BuildMethod("TestProject.MyMethod");
        var before = BuildSnapshot(method);
        var after  = BuildSnapshot();

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().HaveCount(1);
        diff.Changes[0].ChangeType.Should().Be(ChangeType.Removed);
        diff.Changes[0].SymbolId.Value.Should().Be("TestProject.MyMethod");
    }

    [Fact]
    public void Diff_empty_snapshots_produces_empty_diff()
    {
        var before = BuildSnapshot();
        var after  = BuildSnapshot();

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().BeEmpty();
        diff.Summary.TotalChanges.Should().Be(0);
        diff.Summary.Added.Should().Be(0);
        diff.Summary.Removed.Should().Be(0);
        diff.Summary.Modified.Should().Be(0);
        diff.Summary.Breaking.Should().Be(0);
        diff.Summary.NonBreaking.Should().Be(0);
        diff.Summary.Informational.Should().Be(0);
    }

    [Fact]
    public void Diff_identical_snapshots_produces_no_changes()
    {
        var method = BuildMethod("TestProject.MyMethod", parameters: [Param("x", "int")]);
        var before = BuildSnapshot(method);

        // Rebuild with same content (different SourceFingerprint is expected)
        var after = BuildSnapshot(BuildMethod("TestProject.MyMethod", parameters: [Param("x", "int")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Diff_summary_counts_match_changes()
    {
        // Before: 2 methods
        var m1 = BuildMethod("TestProject.Method1");
        var m2 = BuildMethod("TestProject.Method2");
        var before = BuildSnapshot(m1, m2);

        // After: Method1 removed, Method2 modified (return type change), Method3 added
        var m2Modified = BuildMethod("TestProject.Method2", returnType: "int");
        var m3 = BuildMethod("TestProject.Method3");
        var after = BuildSnapshot(m2Modified, m3);

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Summary.TotalChanges.Should().Be(diff.Changes.Count);
        diff.Summary.Added.Should().Be(diff.Changes.Count(c => c.ChangeType == ChangeType.Added));
        diff.Summary.Removed.Should().Be(diff.Changes.Count(c => c.ChangeType == ChangeType.Removed));
        diff.Summary.Modified.Should().Be(diff.Changes.Count(c => c.ChangeType == ChangeType.Modified));
        diff.Summary.Breaking.Should().Be(diff.Changes.Count(c => c.Severity == ChangeSeverity.Breaking));
        diff.Summary.NonBreaking.Should().Be(diff.Changes.Count(c => c.Severity == ChangeSeverity.NonBreaking));
        diff.Summary.Informational.Should().Be(diff.Changes.Count(c => c.Severity == ChangeSeverity.Informational));
        diff.Summary.TotalChanges.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Diff_changes_sorted_by_symbol_id_then_category()
    {
        // Two symbols each with signature and accessibility changes
        var bA = BuildMethod("TestProject.AAA", returnType: "string");
        var bZ = BuildMethod("TestProject.ZZZ", returnType: "string");
        var before = BuildSnapshot(bA, bZ);

        var aA = BuildMethod("TestProject.AAA", returnType: "int", access: Accessibility.Internal);
        var aZ = BuildMethod("TestProject.ZZZ", returnType: "int", access: Accessibility.Internal);
        var after = BuildSnapshot(aA, aZ);

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().NotBeEmpty();

        // Verify changes are sorted by SymbolId.Value then Category
        for (int i = 1; i < diff.Changes.Count; i++)
        {
            var prev = diff.Changes[i - 1];
            var curr = diff.Changes[i];
            int cmp = StringComparer.Ordinal.Compare(prev.SymbolId.Value, curr.SymbolId.Value);
            if (cmp == 0)
                ((int)prev.Category).Should().BeLessThanOrEqualTo((int)curr.Category);
            else
                cmp.Should().BeNegative();
        }
    }
}
