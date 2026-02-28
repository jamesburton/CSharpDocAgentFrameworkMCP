using DocAgent.Core;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class DiffDeterminismTests
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    [Fact]
    public void Diff_produces_identical_result_on_repeated_calls()
    {
        var method1 = BuildMethod("TestProject.Alpha", returnType: "string");
        var method2 = BuildMethod("TestProject.Beta",  returnType: "int");
        var before  = BuildSnapshot(method1);
        var after   = BuildSnapshot(method2);

        var diff1 = SymbolGraphDiffer.Diff(before, after);
        var diff2 = SymbolGraphDiffer.Diff(before, after);

        diff1.Should().BeEquivalentTo(diff2);
    }

    [Fact]
    public void SymbolDiff_MessagePack_roundtrip_is_byte_identical()
    {
        var method = BuildMethod("TestProject.Process", returnType: "string",
            parameters: [Param("x", "int")]);
        var before = BuildSnapshot(method);
        var after  = BuildSnapshot(BuildMethod("TestProject.Process", returnType: "bool",
            parameters: [Param("x", "int")]));

        var diff = SymbolGraphDiffer.Diff(before, after);

        var bytes1 = MessagePackSerializer.Serialize(diff, Options);
        var deserialized = MessagePackSerializer.Deserialize<SymbolDiff>(bytes1, Options);
        var bytes2 = MessagePackSerializer.Serialize(deserialized, Options);

        bytes1.SequenceEqual(bytes2).Should().BeTrue(
            "MessagePack roundtrip must produce byte-identical output");
    }

    [Fact]
    public void Diff_changes_order_is_deterministic_across_runs()
    {
        // Build snapshots with many symbols that will have changes
        var beforeNodes = Enumerable.Range(1, 10)
            .Select(i => BuildMethod($"TestProject.Method{i:D2}", returnType: "string"))
            .ToArray();
        var afterNodes = Enumerable.Range(1, 10)
            .Select(i => BuildMethod($"TestProject.Method{i:D2}", returnType: i % 2 == 0 ? "int" : "string"))
            .ToArray();

        var before = BuildSnapshot(beforeNodes);
        var after  = BuildSnapshot(afterNodes);

        var diff1 = SymbolGraphDiffer.Diff(before, after);
        var diff2 = SymbolGraphDiffer.Diff(before, after);

        diff1.Changes.Count.Should().Be(diff2.Changes.Count);
        for (int i = 0; i < diff1.Changes.Count; i++)
        {
            diff1.Changes[i].SymbolId.Value.Should().Be(diff2.Changes[i].SymbolId.Value);
            diff1.Changes[i].Category.Should().Be(diff2.Changes[i].Category);
        }
    }
}
