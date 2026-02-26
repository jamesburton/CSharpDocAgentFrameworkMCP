using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests;

public class SymbolIdTests
{
    [Fact]
    public void SymbolId_value_equality_holds()
    {
        var id1 = new SymbolId("T:MyNamespace.MyClass");
        var id2 = new SymbolId("T:MyNamespace.MyClass");

        (id1 == id2).Should().BeTrue();
        id1.Equals(id2).Should().BeTrue();
    }

    [Fact]
    public void SymbolId_inequality_for_different_values()
    {
        var id1 = new SymbolId("T:MyNamespace.MyClass");
        var id2 = new SymbolId("T:MyNamespace.OtherClass");

        (id1 != id2).Should().BeTrue();
        id1.Equals(id2).Should().BeFalse();
    }

    [Fact]
    public void SymbolId_works_as_dictionary_key()
    {
        var id1 = new SymbolId("T:MyNamespace.MyClass");
        var id2 = new SymbolId("T:MyNamespace.MyClass");

        var dict = new Dictionary<SymbolId, string> { [id1] = "found" };

        dict[id2].Should().Be("found");
    }

    [Fact]
    public Task PreviousIds_tracks_rename()
    {
        var oldId = new SymbolId("T:MyNamespace.OldName");
        var newId = new SymbolId("T:MyNamespace.NewName");

        var node = new SymbolNode(
            Id: newId,
            Kind: SymbolKind.Type,
            DisplayName: "NewName",
            FullyQualifiedName: "MyNamespace.NewName",
            PreviousIds: [oldId],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null);

        return Verify(node);
    }

    [Fact]
    public void PreviousIds_empty_for_no_renames()
    {
        var id = new SymbolId("T:MyNamespace.MyClass");

        var node = new SymbolNode(
            Id: id,
            Kind: SymbolKind.Type,
            DisplayName: "MyClass",
            FullyQualifiedName: "MyNamespace.MyClass",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null);

        node.PreviousIds.Should().BeEmpty();
    }
}
