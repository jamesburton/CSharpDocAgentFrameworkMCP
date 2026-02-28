using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class AccessibilityChangeTests
{
    [Fact]
    public void Diff_detects_public_to_internal()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Publish", access: Accessibility.Public));
        var after  = BuildSnapshot(BuildMethod("TestProject.Publish", access: Accessibility.Internal));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Accessibility).Subject;
        change.Severity.Should().Be(ChangeSeverity.Breaking);
        change.AccessibilityDetail.Should().NotBeNull();
        change.AccessibilityDetail!.OldAccessibility.Should().Be(Accessibility.Public);
        change.AccessibilityDetail.NewAccessibility.Should().Be(Accessibility.Internal);
    }

    [Fact]
    public void Diff_detects_internal_to_public()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Helper", access: Accessibility.Internal));
        var after  = BuildSnapshot(BuildMethod("TestProject.Helper", access: Accessibility.Public));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Accessibility).Subject;
        change.Severity.Should().Be(ChangeSeverity.NonBreaking);
        change.AccessibilityDetail.Should().NotBeNull();
        change.AccessibilityDetail!.OldAccessibility.Should().Be(Accessibility.Internal);
        change.AccessibilityDetail.NewAccessibility.Should().Be(Accessibility.Public);
    }

    [Fact]
    public void Diff_internal_method_signature_change_is_non_breaking()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Internal.Helper",
            access: Accessibility.Internal, parameters: [Param("x", "int")]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Internal.Helper",
            access: Accessibility.Internal, parameters: [Param("x", "long")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var sigChange = diff.Changes.FirstOrDefault(c => c.Category == ChangeCategory.Signature);
        sigChange.Should().NotBeNull();
        sigChange!.Severity.Should().Be(ChangeSeverity.NonBreaking);
    }
}
