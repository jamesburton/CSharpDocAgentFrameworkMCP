using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class NullabilityChangeTests
{
    [Fact]
    public void Diff_detects_return_type_nullability_change()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.GetName", returnType: "string"));
        var after  = BuildSnapshot(BuildMethod("TestProject.GetName", returnType: "string?"));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Nullability).Subject;
        change.ChangeType.Should().Be(ChangeType.Modified);
        change.NullabilityDetail.Should().NotBeNull();
        change.NullabilityDetail!.OldAnnotation.Should().Be("string");
        change.NullabilityDetail.NewAnnotation.Should().Be("string?");
    }

    [Fact]
    public void Diff_detects_parameter_nullability_change()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Parse", parameters: [Param("input", "string")]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Parse", parameters: [Param("input", "string?")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Nullability).Subject;
        change.ChangeType.Should().Be(ChangeType.Modified);
        change.NullabilityDetail.Should().NotBeNull();
    }
}
