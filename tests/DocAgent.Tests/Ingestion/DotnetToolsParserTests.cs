using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.Ingestion;

public class DotnetToolsParserTests : IDisposable
{
    private readonly string _tempDir;

    public DotnetToolsParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dotnet-tools-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteToolsJson(string json)
    {
        var filePath = Path.Combine(_tempDir, "dotnet-tools.json");
        File.WriteAllText(filePath, json);
        return filePath;
    }

    [Fact]
    public void Parse_ValidManifest_ReturnsToolNodes()
    {
        var json = """
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "dotnet-ef": {
              "version": "8.0.1",
              "commands": ["dotnet-ef"],
              "rollForward": false
            },
            "csharpier": {
              "version": "0.28.0",
              "commands": ["dotnet-csharpier"]
            }
          }
        }
        """;
        var path = WriteToolsJson(json);

        var (nodes, edges) = DotnetToolsParser.Parse(path);

        nodes.Should().HaveCount(2);
        edges.Should().BeEmpty();

        var ef = nodes.First(n => n.DisplayName == "dotnet-ef");
        ef.Kind.Should().Be(SymbolKind.Tool);
        ef.Id.Value.Should().Be("T:dotnet-tool/dotnet-ef");
        ef.FullyQualifiedName.Should().Be("dotnet-tool/dotnet-ef@8.0.1");

        var csharpier = nodes.First(n => n.DisplayName == "csharpier");
        csharpier.Kind.Should().Be(SymbolKind.Tool);
        csharpier.Id.Value.Should().Be("T:dotnet-tool/csharpier");
        csharpier.FullyQualifiedName.Should().Be("dotnet-tool/csharpier@0.28.0");
    }

    [Fact]
    public void Parse_VersionAndCommandsCapturedInDocComment()
    {
        var json = """
        {
          "version": 1,
          "tools": {
            "my-tool": {
              "version": "1.2.3",
              "commands": ["cmd1", "cmd2"]
            }
          }
        }
        """;
        var path = WriteToolsJson(json);

        var (nodes, _) = DotnetToolsParser.Parse(path);

        nodes.Should().HaveCount(1);
        var node = nodes[0];
        node.Docs.Should().NotBeNull();
        node.Docs!.Summary.Should().Contain("1.2.3");
        node.Docs.Summary.Should().Contain("cmd1");
        node.Docs.Summary.Should().Contain("cmd2");
    }

    [Fact]
    public void Parse_SymbolIdFormat()
    {
        var json = """
        {
          "version": 1,
          "tools": {
            "some-tool": {
              "version": "0.1.0",
              "commands": ["some"]
            }
          }
        }
        """;
        var path = WriteToolsJson(json);

        var (nodes, _) = DotnetToolsParser.Parse(path);

        nodes.Should().ContainSingle();
        nodes[0].Id.Value.Should().Be("T:dotnet-tool/some-tool");
    }

    [Fact]
    public void Parse_MissingFile_ReturnsEmpty()
    {
        var (nodes, edges) = DotnetToolsParser.Parse(Path.Combine(_tempDir, "nonexistent.json"));

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsEmpty()
    {
        var path = WriteToolsJson("{ invalid json }}}}");

        var (nodes, edges) = DotnetToolsParser.Parse(path);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoToolsProperty_ReturnsEmpty()
    {
        var path = WriteToolsJson("""{ "version": 1 }""");

        var (nodes, edges) = DotnetToolsParser.Parse(path);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SourceSpanPointsToFile()
    {
        var json = """
        {
          "version": 1,
          "tools": {
            "my-tool": {
              "version": "1.0.0",
              "commands": ["my"]
            }
          }
        }
        """;
        var path = WriteToolsJson(json);

        var (nodes, _) = DotnetToolsParser.Parse(path);

        nodes.Should().ContainSingle();
        var span = nodes[0].Span;
        span.Should().NotBeNull();
        span!.FilePath.Should().Be(path);
        span.StartLine.Should().BeGreaterThan(0);
    }
}
