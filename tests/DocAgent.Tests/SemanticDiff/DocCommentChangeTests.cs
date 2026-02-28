using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class DocCommentChangeTests
{
    [Fact]
    public void Diff_detects_doc_comment_added()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Greet", docs: null));
        var after  = BuildSnapshot(BuildMethod("TestProject.Greet", docs: BuildDoc("Returns a greeting.")));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.DocComment).Subject;
        change.Severity.Should().Be(ChangeSeverity.Informational);
        change.DocCommentDetail.Should().NotBeNull();
        change.DocCommentDetail!.OldDocs.Should().BeNull();
        change.DocCommentDetail.NewDocs.Should().NotBeNull();
    }

    [Fact]
    public void Diff_detects_doc_comment_changed()
    {
        var before = BuildSnapshot(BuildMethod("TestProject.Greet", docs: BuildDoc("Old summary.")));
        var after  = BuildSnapshot(BuildMethod("TestProject.Greet", docs: BuildDoc("New improved summary.")));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.DocComment).Subject;
        change.DocCommentDetail.Should().NotBeNull();
        change.DocCommentDetail!.OldDocs!.Summary.Should().Be("Old summary.");
        change.DocCommentDetail.NewDocs!.Summary.Should().Be("New improved summary.");
    }
}
