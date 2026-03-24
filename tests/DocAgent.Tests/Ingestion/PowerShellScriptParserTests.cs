using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Ingestion;

public class PowerShellScriptParserTests : IDisposable
{
    private readonly string _tempDir;

    public PowerShellScriptParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ps1-parser-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteScript(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SimpleScript_WithBlockCommentHelp_ProducesScriptNodeWithDocComment()
    {
        var path = WriteScript("deploy.ps1", """
            <#
            .SYNOPSIS
            Deploys the application to production.
            #>
            Write-Host "Deploying..."
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.Script);
        var scriptNode = nodes.First(n => n.Kind == SymbolKind.Script);
        scriptNode.Id.Value.Should().Be("T:script/ps1/deploy.ps1");
        scriptNode.Docs.Should().NotBeNull();
        scriptNode.Docs!.Summary.Should().Contain("Deploys the application to production");
    }

    [Fact]
    public void FunctionExtraction_TwoFunctions_ProducesFunctionNodesAndContainsEdges()
    {
        var path = WriteScript("utils.ps1", """
            function Get-Config {
                return @{}
            }

            function Set-Config {
                param($Value)
            }
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var funcNodes = nodes.Where(n => n.Kind == SymbolKind.ScriptFunction).ToList();
        funcNodes.Should().HaveCount(2);
        funcNodes.Select(n => n.DisplayName).Should().Contain("Get-Config").And.Contain("Set-Config");

        var containsEdges = edges.Where(e => e.Kind == SymbolEdgeKind.Contains
            && e.To.Value.Contains("::Function/")
            && !e.To.Value.Contains("::Param/")).ToList();
        containsEdges.Should().HaveCount(2);
    }

    [Fact]
    public void ParameterExtraction_FunctionWithTypedParams_ProducesParameterNodes()
    {
        var path = WriteScript("params.ps1", """
            function Copy-Item2 {
                param(
                    [string]$Name,
                    [int]$Count
                )
            }
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var paramNodes = nodes.Where(n => n.Kind == SymbolKind.ScriptParameter).ToList();
        paramNodes.Should().HaveCount(2);

        var nameParam = paramNodes.First(n => n.DisplayName == "Name");
        nameParam.ReturnType.Should().Be("string");
        nameParam.Id.Value.Should().EndWith("::Param/Name");

        var countParam = paramNodes.First(n => n.DisplayName == "Count");
        countParam.ReturnType.Should().Be("int");
    }

    [Fact]
    public void CmdletBindingParams_MandatoryTrue_CapturesMandatoryFlag()
    {
        var path = WriteScript("mandatory.ps1", """
            function Install-App {
                [CmdletBinding()]
                param(
                    [Parameter(Mandatory=$true)][string]$Path,
                    [string]$Optional
                )
            }
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var paramNodes = nodes.Where(n => n.Kind == SymbolKind.ScriptParameter).ToList();
        paramNodes.Should().HaveCount(2);

        var pathParam = paramNodes.First(n => n.DisplayName == "Path");
        pathParam.Docs.Should().NotBeNull();
        pathParam.Docs!.Summary.Should().Be("Mandatory parameter");

        var optionalParam = paramNodes.First(n => n.DisplayName == "Optional");
        optionalParam.Docs.Should().BeNull();
    }

    [Fact]
    public void DotSourcing_ParsesDotSourcedScript_ProducesImportsEdge()
    {
        var path = WriteScript("main.ps1", """
            . .\helpers.ps1
            . .\lib\utils.ps1
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var importEdges = edges.Where(e => e.Kind == SymbolEdgeKind.Imports).ToList();
        importEdges.Should().HaveCount(2);
        importEdges.Select(e => e.To.Value).Should().Contain("T:script/ps1/./helpers.ps1");
        importEdges.Select(e => e.To.Value).Should().Contain("T:script/ps1/./lib/utils.ps1");
    }

    [Fact]
    public void RequiresModules_ParsesRequiresDirective_ProducesImportsEdge()
    {
        var path = WriteScript("azure.ps1", """
            #Requires -Modules Az.Accounts
            #Requires -Modules Az.Storage
            Write-Host "hello"
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var importEdges = edges.Where(e => e.Kind == SymbolEdgeKind.Imports).ToList();
        importEdges.Should().HaveCount(2);
        importEdges.Should().Contain(e => e.To.Value == "T:module/Az.Accounts");
        importEdges.Should().Contain(e => e.To.Value == "T:module/Az.Storage");
    }

    [Fact]
    public void DotnetInvocations_ParsesDotnetBuild_ProducesInvokesEdge()
    {
        var path = WriteScript("build.ps1", """
            dotnet build .\src\MyApp.csproj
            dotnet test .\tests\MyTests.csproj
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var invokeEdges = edges.Where(e => e.Kind == SymbolEdgeKind.Invokes).ToList();
        invokeEdges.Should().HaveCount(2);
        invokeEdges.Should().Contain(e => e.To.Value == "T:tool/dotnet/build");
        invokeEdges.Should().Contain(e => e.To.Value == "T:tool/dotnet/test");
    }

    [Fact]
    public void DotNetTypeUsage_ParsesSystemTypeReference_ProducesReferencesEdge()
    {
        var path = WriteScript("types.ps1", """
            $combined = [System.IO.Path]::Combine("a", "b")
            $list = [System.Collections.Generic.List[string]]::new()
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var refEdges = edges.Where(e => e.Kind == SymbolEdgeKind.References).ToList();
        refEdges.Should().HaveCountGreaterOrEqualTo(2);
        refEdges.Should().Contain(e => e.To.Value == "T:System.IO.Path");
        refEdges.Should().Contain(e => e.To.Value == "T:System.Collections.Generic.List");
    }

    [Fact]
    public void EmptyOrMissingFile_ReturnsEmptyLists()
    {
        // Non-existent file
        var (nodes1, edges1) = PowerShellScriptParser.Parse(
            Path.Combine(_tempDir, "nonexistent.ps1"), _tempDir);
        nodes1.Should().BeEmpty();
        edges1.Should().BeEmpty();

        // Empty file
        var emptyPath = WriteScript("empty.ps1", "");
        var (nodes2, edges2) = PowerShellScriptParser.Parse(emptyPath, _tempDir);
        nodes2.Should().BeEmpty();
        edges2.Should().BeEmpty();
    }

    [Fact]
    public void NestedFunctions_CapturesBothOuterAndInner()
    {
        var path = WriteScript("nested.ps1", """
            function Outer-Func {
                function Inner-Func {
                    Write-Host "inner"
                }
                Inner-Func
            }
            """);

        var (nodes, edges) = PowerShellScriptParser.Parse(path, _tempDir);

        var funcNodes = nodes.Where(n => n.Kind == SymbolKind.ScriptFunction).ToList();
        funcNodes.Should().HaveCount(2);
        funcNodes.Should().Contain(n => n.DisplayName == "Outer-Func");
        funcNodes.Should().Contain(n => n.DisplayName == "Inner-Func");
    }
}
