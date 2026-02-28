using DocAgent.Core;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Tests.IncrementalIngestion;

public class IngestionMetadataTests
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private static IngestionMetadata BuildMetadata() => new(
        RunId: "run-001",
        StartedAt: new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero),
        CompletedAt: new DateTimeOffset(2026, 2, 28, 10, 1, 0, TimeSpan.Zero),
        WasFullReingestion: false,
        FileChanges:
        [
            new FileChangeRecord(
                FilePath: "src/Foo.cs",
                ChangeKind: FileChangeKind.Modified,
                AffectedSymbolIds: ["Foo.Bar", "Foo.Baz"]),
            new FileChangeRecord(
                FilePath: "src/New.cs",
                ChangeKind: FileChangeKind.Added,
                AffectedSymbolIds: ["New.Type"])
        ]);

    [Fact]
    public void IngestionMetadata_roundtrips_via_MessagePack()
    {
        var metadata = BuildMetadata();

        var bytes = MessagePackSerializer.Serialize(metadata, Options);
        var deserialized = MessagePackSerializer.Deserialize<IngestionMetadata>(bytes, Options);

        deserialized.Should().BeEquivalentTo(metadata);
    }

    [Fact]
    public void SymbolGraphSnapshot_with_null_IngestionMetadata_roundtrips_via_MessagePack()
    {
        var snapshot = BuildSnapshot(ingestionMetadata: null);

        var bytes = MessagePackSerializer.Serialize(snapshot, Options);
        var deserialized = MessagePackSerializer.Deserialize<SymbolGraphSnapshot>(bytes, Options);

        deserialized.Should().BeEquivalentTo(snapshot);
        deserialized.IngestionMetadata.Should().BeNull();
    }

    [Fact]
    public void SymbolGraphSnapshot_with_IngestionMetadata_roundtrips_via_MessagePack()
    {
        var metadata = BuildMetadata();
        var snapshot = BuildSnapshot(ingestionMetadata: metadata);

        var bytes = MessagePackSerializer.Serialize(snapshot, Options);
        var deserialized = MessagePackSerializer.Deserialize<SymbolGraphSnapshot>(bytes, Options);

        deserialized.Should().BeEquivalentTo(snapshot);
        deserialized.IngestionMetadata.Should().NotBeNull();
        deserialized.IngestionMetadata!.RunId.Should().Be("run-001");
        deserialized.IngestionMetadata.FileChanges.Should().HaveCount(2);
    }

    private static SymbolGraphSnapshot BuildSnapshot(IngestionMetadata? ingestionMetadata) =>
        new(
            SchemaVersion: "1.0.0",
            ProjectName: "TestProject",
            SourceFingerprint: "fp123",
            ContentHash: null,
            CreatedAt: new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero),
            Nodes: Array.Empty<SymbolNode>(),
            Edges: Array.Empty<SymbolEdge>(),
            IngestionMetadata: ingestionMetadata);
}
