using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.Ingestion;

public class MSBuildFileParserTests : IDisposable
{
    private readonly string _tempDir;

    public MSBuildFileParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"msbuild-parser-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteFile(string fileName, string content)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    [Fact]
    public void Parse_TargetElement_CreatesBuildTargetNode()
    {
        var xml = """
        <Project>
          <Target Name="Build">
            <Message Text="Building..." />
          </Target>
        </Project>
        """;
        var path = WriteFile("test.targets", xml);

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().ContainSingle();
        var node = nodes[0];
        node.Kind.Should().Be(SymbolKind.BuildTarget);
        node.DisplayName.Should().Be("Build");
        node.Id.Value.Should().Be("T:msbuild-target/test.targets/Build");
        node.FullyQualifiedName.Should().Be("msbuild-target/test.targets/Build");
    }

    [Fact]
    public void Parse_DependsOnTargets_CreatesEdges()
    {
        var xml = """
        <Project>
          <Target Name="Test" DependsOnTargets="Build;Restore">
            <Message Text="Testing..." />
          </Target>
        </Project>
        """;
        var path = WriteFile("test.targets", xml);

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().ContainSingle();
        edges.Should().HaveCount(2);

        edges.Should().Contain(e =>
            e.From.Value == "T:msbuild-target/test.targets/Test" &&
            e.To.Value == "T:msbuild-target/test.targets/Build" &&
            e.Kind == SymbolEdgeKind.DependsOn);

        edges.Should().Contain(e =>
            e.From.Value == "T:msbuild-target/test.targets/Test" &&
            e.To.Value == "T:msbuild-target/test.targets/Restore" &&
            e.Kind == SymbolEdgeKind.DependsOn);
    }

    [Fact]
    public void Parse_BeforeAfterTargets_CreateDependsOnEdges()
    {
        var xml = """
        <Project>
          <Target Name="MyTarget" BeforeTargets="Build" AfterTargets="Restore">
            <Message Text="Custom..." />
          </Target>
        </Project>
        """;
        var path = WriteFile("custom.targets", xml);

        var (_, edges) = MSBuildFileParser.Parse(path);

        edges.Should().HaveCount(2);
        edges.Should().OnlyContain(e => e.Kind == SymbolEdgeKind.DependsOn);
    }

    [Fact]
    public void Parse_PropertyGroup_CreatesBuildPropertyNodes()
    {
        var xml = """
        <Project>
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;
        var path = WriteFile("test.props", xml);

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().HaveCount(2);
        edges.Should().BeEmpty();

        var outputType = nodes.First(n => n.DisplayName == "OutputType");
        outputType.Kind.Should().Be(SymbolKind.BuildProperty);
        outputType.Id.Value.Should().Be("T:msbuild-prop/test.props/OutputType");
        outputType.Docs!.Summary.Should().Contain("Exe");

        var tf = nodes.First(n => n.DisplayName == "TargetFramework");
        tf.Kind.Should().Be(SymbolKind.BuildProperty);
    }

    [Fact]
    public void Parse_UsingTask_CreatesBuildTaskNode()
    {
        var xml = """
        <Project>
          <UsingTask TaskName="MyTask" AssemblyFile="tasks.dll" />
        </Project>
        """;
        var path = WriteFile("test.targets", xml);

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().ContainSingle();
        var node = nodes[0];
        node.Kind.Should().Be(SymbolKind.BuildTask);
        node.DisplayName.Should().Be("MyTask");
        node.Id.Value.Should().Be("T:msbuild-task/test.targets/MyTask");
        node.Docs!.Summary.Should().Contain("tasks.dll");
    }

    [Fact]
    public void Parse_UsingTaskWithAssemblyFile_CreatesReferencesEdge()
    {
        var xml = """
        <Project>
          <UsingTask TaskName="MyTask" AssemblyFile="bin/MyTasks.dll" />
        </Project>
        """;
        var path = WriteFile("test.targets", xml);

        var (_, edges) = MSBuildFileParser.Parse(path);

        edges.Should().ContainSingle();
        edges[0].Kind.Should().Be(SymbolEdgeKind.References);
        edges[0].From.Value.Should().Be("T:msbuild-task/test.targets/MyTask");
        edges[0].To.Value.Should().Be("T:assembly/bin/MyTasks.dll");
    }

    [Fact]
    public void Parse_ConditionAttribute_StoredInRemarks()
    {
        var xml = """
        <Project>
          <Target Name="Conditional" Condition="'$(CI)' == 'true'">
            <Message Text="CI only" />
          </Target>
        </Project>
        """;
        var path = WriteFile("test.targets", xml);

        var (nodes, _) = MSBuildFileParser.Parse(path);

        nodes.Should().ContainSingle();
        nodes[0].Docs!.Remarks.Should().Contain("$(CI)");
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmpty()
    {
        var path = WriteFile("bad.targets", "<Project><Target Name=broken</Project>");

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingFile_ReturnsEmpty()
    {
        var (nodes, edges) = MSBuildFileParser.Parse(Path.Combine(_tempDir, "nonexistent.targets"));

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ComplexFile_ExtractsAllElementTypes()
    {
        var xml = """
        <Project>
          <PropertyGroup>
            <MyProp>value</MyProp>
          </PropertyGroup>
          <UsingTask TaskName="CustomTask" AssemblyName="MyAssembly" />
          <Target Name="DoStuff" DependsOnTargets="Prep">
            <CustomTask />
          </Target>
          <Target Name="Prep">
            <Message Text="Prepping..." />
          </Target>
        </Project>
        """;
        var path = WriteFile("complex.targets", xml);

        var (nodes, edges) = MSBuildFileParser.Parse(path);

        nodes.Should().HaveCount(4); // 1 prop + 1 task + 2 targets
        nodes.Should().Contain(n => n.Kind == SymbolKind.BuildProperty);
        nodes.Should().Contain(n => n.Kind == SymbolKind.BuildTask);
        nodes.Where(n => n.Kind == SymbolKind.BuildTarget).Should().HaveCount(2);

        // 1 DependsOn + 1 References
        edges.Should().HaveCount(2);
    }
}
