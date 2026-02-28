using DocAgent.Core;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;
using System.IO.Hashing;
using System.Text.Json;

namespace DocAgent.Tests;

public class SnapshotSerializationTests
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private static SymbolGraphSnapshot BuildSnapshot()
    {
        var docComment = new DocComment(
            Summary: "A summary of the symbol.",
            Remarks: "Detailed remarks about usage.",
            Params: new Dictionary<string, string>
            {
                ["value"] = "The input value.",
                ["other"] = "Another parameter."
            },
            TypeParams: new Dictionary<string, string>
            {
                ["T"] = "The type parameter."
            },
            Returns: "A result value.",
            Examples: ["Example 1", "Example 2"],
            Exceptions:
            [
                ("ArgumentNullException", "When value is null."),
                ("InvalidOperationException", "When state is invalid.")
            ],
            SeeAlso: ["OtherType", "SomeMethod"]);

        var node1 = new SymbolNode(
            Id: new SymbolId("DocAgent.Core.Symbols"),
            Kind: SymbolKind.Type,
            DisplayName: "Symbols",
            FullyQualifiedName: "DocAgent.Core.Symbols",
            PreviousIds: [new SymbolId("DocAgent.Core.OldSymbols")],
            Accessibility: Accessibility.Public,
            Docs: docComment,
            Span: new SourceSpan("src/DocAgent.Core/Symbols.cs", 1, 0, 76, 0),
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

        var node2 = new SymbolNode(
            Id: new SymbolId("DocAgent.Core.SymbolNode"),
            Kind: SymbolKind.Method,
            DisplayName: "SymbolNode",
            FullyQualifiedName: "DocAgent.Core.SymbolNode",
            PreviousIds: [],
            Accessibility: Accessibility.Internal,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

        var edge = new SymbolEdge(
            From: new SymbolId("DocAgent.Core.Symbols"),
            To: new SymbolId("DocAgent.Core.SymbolNode"),
            Kind: SymbolEdgeKind.Contains);

        return new SymbolGraphSnapshot(
            SchemaVersion: "1.0.0",
            ProjectName: "DocAgent.Core",
            SourceFingerprint: "abc123def456",
            ContentHash: null,
            CreatedAt: new DateTimeOffset(2026, 2, 26, 0, 0, 0, TimeSpan.Zero),
            Nodes: [node1, node2],
            Edges: [edge]);
    }

    [Fact]
    public void Roundtrip_MessagePack_produces_identical_snapshot()
    {
        var snapshot = BuildSnapshot();

        byte[] bytes = MessagePackSerializer.Serialize(snapshot, Options);
        var deserialized = MessagePackSerializer.Deserialize<SymbolGraphSnapshot>(bytes, Options);

        deserialized.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public void Serialization_is_deterministic()
    {
        var snapshot = BuildSnapshot();

        byte[] bytes1 = MessagePackSerializer.Serialize(snapshot, Options);
        byte[] bytes2 = MessagePackSerializer.Serialize(snapshot, Options);

        bytes1.SequenceEqual(bytes2).Should().BeTrue("serialization must produce identical bytes for the same input");
    }

    [Fact]
    public void ContentHash_is_stable_across_serializations()
    {
        var snapshot = BuildSnapshot();

        byte[] bytes1 = MessagePackSerializer.Serialize(snapshot, Options);
        ulong raw1 = XxHash64.HashToUInt64(bytes1);
        string hash1 = raw1.ToString("x16");

        byte[] bytes2 = MessagePackSerializer.Serialize(snapshot, Options);
        ulong raw2 = XxHash64.HashToUInt64(bytes2);
        string hash2 = raw2.ToString("x16");

        hash1.Should().Be(hash2, "content hash must be stable for identical snapshots");

        var withHash = snapshot with { ContentHash = hash1 };
        withHash.ContentHash.Should().Be(hash1);
    }

    [Fact]
    public void Json_roundtrip_produces_equivalent_snapshot()
    {
        var snapshot = BuildSnapshot();

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            IncludeFields = true
        };

        string json = JsonSerializer.Serialize(snapshot, jsonOptions);
        var deserialized = JsonSerializer.Deserialize<SymbolGraphSnapshot>(json, jsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Should().BeEquivalentTo(snapshot);
    }

    [Fact]
    public void ContentHash_differs_for_different_snapshots()
    {
        var snapshot1 = BuildSnapshot();
        var differentNode = new SymbolNode(
            Id: new SymbolId("Different.Symbol"),
            Kind: SymbolKind.Field,
            DisplayName: "DifferentSymbol",
            FullyQualifiedName: "Different.Symbol",
            PreviousIds: [],
            Accessibility: Accessibility.Private,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());
        var snapshot2 = snapshot1 with
        {
            ProjectName = "DifferentProject",
            Nodes = new List<SymbolNode> { differentNode }
        };

        byte[] bytes1 = MessagePackSerializer.Serialize(snapshot1, Options);
        ulong hash1 = XxHash64.HashToUInt64(bytes1);

        byte[] bytes2 = MessagePackSerializer.Serialize(snapshot2, Options);
        ulong hash2 = XxHash64.HashToUInt64(bytes2);

        hash1.Should().NotBe(hash2, "different snapshots must produce different content hashes");
    }
}
