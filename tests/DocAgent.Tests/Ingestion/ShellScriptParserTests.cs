using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Ingestion;

public sealed class ShellScriptParserTests : IDisposable
{
    private readonly string _tempDir;

    public ShellScriptParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"shell-parser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteScript(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SimpleScript_ShebangAndComments_ProducesScriptNodeWithDocComment()
    {
        var path = WriteScript("build.sh", """
            #!/bin/bash
            # This script builds the project
            # and runs tests afterward
            echo "hello"
            """);

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.Script);
        var script = nodes.First(n => n.Kind == SymbolKind.Script);
        script.Id.Value.Should().Be("T:script/sh/build.sh");
        script.DisplayName.Should().Be("build.sh");
        script.Docs.Should().NotBeNull();
        script.Docs!.Remarks.Should().Be("#!/bin/bash");
        script.Docs.Summary.Should().Contain("builds the project");
    }

    [Fact]
    public void FunctionExtraction_KeywordAndShortSyntax_ProducesScriptFunctionNodes()
    {
        var path = WriteScript("funcs.sh", """
            #!/bin/bash
            function foo() {
                echo "foo"
            }

            bar() {
                echo "bar"
            }
            """);

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        var functions = nodes.Where(n => n.Kind == SymbolKind.ScriptFunction).ToList();
        functions.Should().HaveCount(2);
        functions.Should().Contain(n => n.DisplayName == "foo()");
        functions.Should().Contain(n => n.DisplayName == "bar()");

        // Contains edges from script to functions
        var scriptId = nodes.First(n => n.Kind == SymbolKind.Script).Id;
        var containsEdges = edges.Where(e => e.Kind == SymbolEdgeKind.Contains).ToList();
        containsEdges.Should().HaveCount(2);
        containsEdges.Should().AllSatisfy(e => e.From.Should().Be(scriptId));
    }

    [Fact]
    public void SourceImport_SourceAndDotSyntax_ProducesImportsEdges()
    {
        var path = WriteScript("main.sh", """
            #!/bin/bash
            source ./lib.sh
            . ./utils.sh
            echo "done"
            """);

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        var imports = edges.Where(e => e.Kind == SymbolEdgeKind.Imports).ToList();
        imports.Should().HaveCount(2);
        imports.Should().Contain(e => e.To.Value == "T:script/sh/lib.sh");
        imports.Should().Contain(e => e.To.Value == "T:script/sh/utils.sh");
    }

    [Fact]
    public void DotnetInvocations_BuildAndTest_ProducesInvokesEdges()
    {
        var path = WriteScript("ci.sh", """
            #!/bin/bash
            dotnet build ./src/App.csproj
            dotnet test
            """);

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        var invokes = edges.Where(e => e.Kind == SymbolEdgeKind.Invokes).ToList();
        invokes.Should().HaveCount(2);
        invokes.Should().Contain(e => e.To.Value == "T:tool/dotnet/build");
        invokes.Should().Contain(e => e.To.Value == "T:tool/dotnet/test");
    }

    [Fact]
    public void ScriptInvocations_RelativeScript_ProducesInvokesEdge()
    {
        var path = WriteScript("deploy.sh", """
            #!/bin/bash
            ./setup.sh
            echo "deploying"
            """);

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        var invokes = edges.Where(e => e.Kind == SymbolEdgeKind.Invokes).ToList();
        invokes.Should().ContainSingle();
        invokes[0].To.Value.Should().Be("T:script/sh/setup.sh");
    }

    [Fact]
    public void FunctionComments_CommentBlockAboveFunction_CapturedAsDocComment()
    {
        var path = WriteScript("commented.sh", """
            #!/bin/bash

            # Deploys the application
            # to the production environment
            deploy() {
                echo "deploying"
            }
            """);

        var (nodes, _) = ShellScriptParser.Parse(path, _tempDir);

        var func = nodes.First(n => n.Kind == SymbolKind.ScriptFunction);
        func.Docs.Should().NotBeNull();
        func.Docs!.Summary.Should().Contain("Deploys the application");
        func.Docs.Summary.Should().Contain("production environment");
    }

    [Fact]
    public void EmptyFile_ReturnsEmptyLists()
    {
        var path = WriteScript("empty.sh", "");

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void MissingFile_ReturnsEmptyLists()
    {
        var path = Path.Combine(_tempDir, "nonexistent.sh");

        var (nodes, edges) = ShellScriptParser.Parse(path, _tempDir);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }
}
