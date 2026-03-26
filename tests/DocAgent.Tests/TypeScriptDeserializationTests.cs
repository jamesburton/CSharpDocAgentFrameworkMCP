using System.Text.Json;
using System.Text.Json.Serialization;
using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Golden file deserialization tests that verify the JSON contract fixes from Plan 32-01
/// produce correct SymbolGraphSnapshot values end-to-end.
/// Covers: edge integrity (SIDE-03), doc comment preservation (EXTR-04), enum string
/// deserialization (EXTR-06), docComment→Docs mapping, and full snapshot structural equality.
/// </summary>
[Trait("Category", "Deserialization")]
public sealed class TypeScriptDeserializationTests
{
    // Mirror the private SidecarJsonOptions from TypeScriptIngestionService — must stay in sync.
    private static readonly JsonSerializerOptions SidecarJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false),
            new DocCommentConverter()
        }
    };

    // Golden file: real sidecar output captured from simple-project fixture.
    // 20 nodes, 20 edges, string enum values, docComment objects.
    private static readonly string GoldenFilePath =
        Path.Combine(AppContext.BaseDirectory, "golden-files", "sidecar-simple-project.json");

    private SymbolGraphSnapshot LoadGoldenSnapshot()
    {
        File.Exists(GoldenFilePath).Should().BeTrue($"golden file must exist at {GoldenFilePath}");
        var json = File.ReadAllText(GoldenFilePath);
        var snapshot = JsonSerializer.Deserialize<SymbolGraphSnapshot>(json, SidecarJsonOptions);
        snapshot.Should().NotBeNull("golden file must deserialize to a non-null snapshot");
        return snapshot!;
    }

    [Fact]
    public void GoldenFile_Deserializes_To_Valid_Snapshot()
    {
        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // Assert basic structure
        snapshot.Nodes.Should().NotBeEmpty("snapshot must contain at least one node");
        snapshot.Edges.Should().NotBeEmpty("snapshot must contain at least one edge");

        // Verify SymbolId record struct deserialization — Value must be non-empty
        snapshot.Nodes.Should().Contain(n => !string.IsNullOrEmpty(n.Id.Value),
            "at least one node must have a non-empty SymbolId value");
    }

    [Fact]
    public void GoldenFile_Edge_Integrity()
    {
        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // All edges must have valid From/To SymbolIds
        foreach (var edge in snapshot.Edges)
        {
            edge.From.Value.Should().NotBeNullOrEmpty("edge From must be a non-empty SymbolId");
            edge.To.Value.Should().NotBeNullOrEmpty("edge To must be a non-empty SymbolId");
        }

        // Contains edges must exist
        snapshot.Edges.Should().Contain(e => e.Kind == SymbolEdgeKind.Contains,
            "snapshot must have Contains edges from file node to top-level symbols");

        // Inherits edge: SpecialGreeter inherits from Greeter
        snapshot.Edges.Should().Contain(
            e => e.Kind == SymbolEdgeKind.Inherits
              && e.From.Value == "T:simple-project:src/index.ts:SpecialGreeter"
              && e.To.Value == "T:simple-project:src/index.ts:Greeter",
            "SpecialGreeter must have an Inherits edge to Greeter");

        // Implements edge: Greeter implements IGreeter
        snapshot.Edges.Should().Contain(
            e => e.Kind == SymbolEdgeKind.Implements
              && e.From.Value == "T:simple-project:src/index.ts:Greeter"
              && e.To.Value == "T:simple-project:src/index.ts:IGreeter",
            "Greeter must have an Implements edge to IGreeter");

        // No edge kind must be numeric fallback (0 == Contains which is valid, but
        // any kind NOT defined in the enum would throw with allowIntegerValues:false,
        // so we verify deserialization succeeded and all kinds are named members)
        var validKinds = Enum.GetValues<SymbolEdgeKind>().ToHashSet();
        foreach (var edge in snapshot.Edges)
        {
            validKinds.Should().Contain(edge.Kind,
                $"edge kind {(int)edge.Kind} must be a defined SymbolEdgeKind member");
        }
    }

    [Fact]
    public void GoldenFile_DocComment_Preservation()
    {
        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // At least one node must have non-null Docs
        snapshot.Nodes.Should().Contain(n => n.Docs != null,
            "at least one symbol must have documentation");

        // The hello() function has a known summary from the fixture
        var helloNode = snapshot.Nodes.FirstOrDefault(n => n.Id.Value == "M:simple-project:src/index.ts:hello");
        helloNode.Should().NotBeNull("hello function node must be present in snapshot");
        helloNode!.Docs.Should().NotBeNull("hello function must have doc comment");
        helloNode.Docs!.Summary.Should().Be("This is a sample function.",
            "hello function summary must match JSDoc content");

        // Params dictionary must be valid (populated from @param tags)
        helloNode.Docs.Params.Should().NotBeNull("Docs.Params must be a non-null dictionary");
        helloNode.Docs.Params.Should().ContainKey("name",
            "hello function @param 'name' must be preserved");
    }

    [Fact]
    public void GoldenFile_Enum_String_Deserialization()
    {
        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // All nodes must have valid SymbolKind values
        var validKinds = Enum.GetValues<SymbolKind>().ToHashSet();
        foreach (var node in snapshot.Nodes)
        {
            validKinds.Should().Contain(node.Kind,
                $"node {node.Id.Value} must have a defined SymbolKind");
        }

        // All nodes must have valid Accessibility values
        var validAccessibility = Enum.GetValues<Accessibility>().ToHashSet();
        foreach (var node in snapshot.Nodes)
        {
            validAccessibility.Should().Contain(node.Accessibility,
                $"node {node.Id.Value} must have a defined Accessibility value");
        }

        // Specifically verify a Type node and a Method node from the fixture
        var iGreeterNode = snapshot.Nodes.FirstOrDefault(n => n.DisplayName == "IGreeter");
        iGreeterNode.Should().NotBeNull("IGreeter type node must be present");
        iGreeterNode!.Kind.Should().Be(SymbolKind.Type,
            "IGreeter must deserialize with SymbolKind.Type (not numeric 1)");

        var helloNode = snapshot.Nodes.FirstOrDefault(n => n.DisplayName == "hello");
        helloNode.Should().NotBeNull("hello method node must be present");
        helloNode!.Kind.Should().Be(SymbolKind.Method,
            "hello must deserialize with SymbolKind.Method (not numeric 2)");
    }

    [Fact]
    public void GoldenFile_SymbolNode_Docs_From_DocComment_Field()
    {
        // Specifically tests the JsonPropertyName("docComment") → Docs property mapping.
        // If the mapping is broken, Docs would be null even for documented symbols.

        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // Greeter class has a docComment object in JSON → must map to non-null Docs
        var greeterNode = snapshot.Nodes.FirstOrDefault(n => n.Id.Value == "T:simple-project:src/index.ts:Greeter");
        greeterNode.Should().NotBeNull("Greeter class node must be present");
        greeterNode!.Docs.Should().NotBeNull(
            "Greeter.Docs must be non-null — docComment→Docs JsonPropertyName mapping must work");
        greeterNode.Docs!.Summary.Should().Be("A class that greets people.",
            "Greeter summary must round-trip through docComment→Docs mapping");

        // ConfiguredGreeter also has docs
        var cgNode = snapshot.Nodes.FirstOrDefault(n => n.Id.Value == "T:simple-project:src/index.ts:ConfiguredGreeter");
        cgNode.Should().NotBeNull("ConfiguredGreeter node must be present");
        cgNode!.Docs.Should().NotBeNull("ConfiguredGreeter.Docs must be non-null via docComment mapping");
    }

    [Fact]
    public void GoldenFile_Snapshot_Matches_Reference()
    {
        // Full structural equality test — exact counts are appropriate here because
        // we control the golden file (captured from simple-project fixture).
        // This catches: missing nodes, dropped edges, broken ID references.

        // Arrange + Act
        var snapshot = LoadGoldenSnapshot();

        // Exact node and edge counts from the golden file (simple-project fixture)
        // 18 nodes: 1 namespace + 5 types + 6 methods + 1 constructor + 1 property + 4 enum members
        // 20 edges: 17 Contains + 2 Inherits + 1 Implements
        const int ExpectedNodeCount = 18;
        const int ExpectedEdgeCount = 20;

        snapshot.Nodes.Count.Should().Be(ExpectedNodeCount,
            $"snapshot must have exactly {ExpectedNodeCount} nodes (per golden file)");
        snapshot.Edges.Count.Should().Be(ExpectedEdgeCount,
            $"snapshot must have exactly {ExpectedEdgeCount} edges (per golden file)");

        // Schema and project metadata
        snapshot.SchemaVersion.Should().NotBeNullOrEmpty("schemaVersion must be present");
        snapshot.ProjectName.Should().Be("simple-project", "projectName must match fixture");

        // Representative SymbolId values that must be present
        var nodeIds = snapshot.Nodes.Select(n => n.Id.Value).ToHashSet();
        nodeIds.Should().Contain("N:simple-project:src/index.ts:file",
            "file namespace node must be present");
        nodeIds.Should().Contain("T:simple-project:src/index.ts:Greeter",
            "Greeter class node must be present");
        nodeIds.Should().Contain("M:simple-project:src/index.ts:hello",
            "hello method node must be present");

        // Referential integrity: all edge From/To IDs must exist as node IDs
        foreach (var edge in snapshot.Edges)
        {
            nodeIds.Should().Contain(edge.From.Value,
                $"edge From='{edge.From.Value}' must reference an existing node");
            nodeIds.Should().Contain(edge.To.Value,
                $"edge To='{edge.To.Value}' must reference an existing node");
        }
    }

    // ── Contract alignment tests (Plan 35-01) ────────────────────────────────

    [Fact]
    public void GenericConstraint_TypeParameterName_Deserializes_Correctly()
    {
        // Verifies that GenericConstraint JSON with "typeParameterName" field deserializes
        // to the C# record's TypeParameterName property (JsonPropertyName match).
        const string json = """{"typeParameterName":"T","constraints":["object"]}""";

        var result = JsonSerializer.Deserialize<GenericConstraint>(json, SidecarJsonOptions);

        result.Should().NotBeNull();
        result!.TypeParameterName.Should().Be("T",
            "typeParameterName JSON field must map to C# TypeParameterName property");
        result.Constraints.Should().ContainSingle().Which.Should().Be("object");
    }

    [Fact]
    public void ParameterInfo_IsOptional_True_Deserializes_Correctly()
    {
        // Verifies that ParameterInfo JSON with "isOptional": true deserializes to
        // ParameterInfo.IsOptional == true.
        const string json = """{"name":"x","typeName":"string","isOptional":true,"defaultValue":null,"isParams":false,"isRef":false,"isOut":false,"isIn":false}""";

        var result = JsonSerializer.Deserialize<ParameterInfo>(json, SidecarJsonOptions);

        result.Should().NotBeNull();
        result!.IsOptional.Should().BeTrue(
            "isOptional=true in JSON must deserialize to ParameterInfo.IsOptional == true");
    }

    [Fact]
    public void ParameterInfo_Without_IsOptional_Defaults_To_False()
    {
        // Verifies backward compatibility: JSON without "isOptional" key deserializes
        // with IsOptional == false (the default).
        const string json = """{"name":"x","typeName":"string","defaultValue":null,"isParams":false,"isRef":false,"isOut":false,"isIn":false}""";

        var result = JsonSerializer.Deserialize<ParameterInfo>(json, SidecarJsonOptions);

        result.Should().NotBeNull();
        result!.IsOptional.Should().BeFalse(
            "missing isOptional field must default to false for backward compatibility");
    }

    [Fact]
    public void SymbolEdgeKind_InheritsFrom_Throws_JsonException()
    {
        // Verifies that TS-emitted "InheritsFrom" string throws JsonException on C# side
        // because there is no InheritsFrom member in the C# SymbolEdgeKind enum and
        // allowIntegerValues: false is set on the converter.
        // This is the guard against INT-04: latent deserialization throw from dormant TS enum values.
        const string json = "\"InheritsFrom\"";

        var act = () => JsonSerializer.Deserialize<SymbolEdgeKind>(json, SidecarJsonOptions);

        act.Should().Throw<JsonException>(
            "InheritsFrom has no C# SymbolEdgeKind counterpart and must be rejected by the strict converter");
    }

    [Fact]
    public void SymbolEdgeKind_Accepts_Throws_JsonException()
    {
        // Same pattern as InheritsFrom — verifies "Accepts" is also rejected.
        const string json = "\"Accepts\"";

        var act = () => JsonSerializer.Deserialize<SymbolEdgeKind>(json, SidecarJsonOptions);

        act.Should().Throw<JsonException>(
            "Accepts has no C# SymbolEdgeKind counterpart and must be rejected by the strict converter");
    }
}
