using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class SignatureChangeTests
{
    [Fact]
    public void Diff_detects_return_type_change()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Calc", returnType: "string"));
        var after  = BuildSnapshot(BuildMethod("TestProject.Calc", returnType: "int"));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Signature).Subject;
        change.ChangeType.Should().Be(ChangeType.Modified);
        change.Severity.Should().Be(ChangeSeverity.Breaking);
        change.SignatureDetail.Should().NotBeNull();
        change.SignatureDetail!.OldReturnType.Should().Be("string");
        change.SignatureDetail.NewReturnType.Should().Be("int");
    }

    [Fact]
    public void Diff_detects_parameter_added()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Process", parameters: [Param("x", "int")]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Process", parameters: [Param("x", "int"), Param("y", "string")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Signature).Subject;
        change.SignatureDetail.Should().NotBeNull();
        change.SignatureDetail!.ParameterChanges.Should().ContainSingle(p => p.ChangeType == ChangeType.Added && p.ParameterName == "y");
    }

    [Fact]
    public void Diff_detects_parameter_type_change()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Convert", parameters: [Param("value", "int")]));
        var after  = BuildSnapshot(BuildMethod("TestProject.Convert", parameters: [Param("value", "long")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Signature).Subject;
        change.SignatureDetail.Should().NotBeNull();
        var paramChange = change.SignatureDetail!.ParameterChanges.Should().ContainSingle().Subject;
        paramChange.OldType.Should().Be("int");
        paramChange.NewType.Should().Be("long");
    }
}
