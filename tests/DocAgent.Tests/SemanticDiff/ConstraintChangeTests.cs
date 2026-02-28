using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class ConstraintChangeTests
{
    [Fact]
    public void Diff_detects_constraint_added()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Create",
            constraints: [new GenericConstraint("T", [])]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Create",
            constraints: [new GenericConstraint("T", ["class"])]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Constraint).Subject;
        change.ConstraintDetail.Should().NotBeNull();
        change.ConstraintDetail!.AddedConstraints.Should().Contain("class");
        change.ConstraintDetail.TypeParameterName.Should().Be("T");
    }

    [Fact]
    public void Diff_detects_constraint_removed()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Create",
            constraints: [new GenericConstraint("T", ["class"])]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Create",
            constraints: [new GenericConstraint("T", [])]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Constraint).Subject;
        change.ConstraintDetail.Should().NotBeNull();
        change.ConstraintDetail!.RemovedConstraints.Should().Contain("class");
    }
}
