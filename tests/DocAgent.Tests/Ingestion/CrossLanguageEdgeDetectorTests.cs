using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using static DocAgent.Tests.Ingestion.ScriptTestHelpers;

namespace DocAgent.Tests.Ingestion;

public class CrossLanguageEdgeDetectorTests
{
    [Fact]
    public void ProjectPathMatching_CreatesInvokesEdge_WhenScriptInvokesProject()
    {
        // Script has an Invokes edge to synthetic target "dotnet-build:./src/MyApp.csproj"
        // Snapshot has C# namespace node from project "MyApp"
        var scriptNode = BuildScriptNode("script:build.ps1", SymbolKind.Script);
        var nsNode = BuildCSharpNamespaceNode("MyApp", "MyApp");

        var edges = new[]
        {
            BuildEdge("script:build.ps1", "dotnet-build:./src/MyApp.csproj", SymbolEdgeKind.Invokes)
        };

        var snapshot = BuildMixedSnapshot([scriptNode, nsNode], edges);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SymbolEdge(
                new SymbolId("script:build.ps1"),
                new SymbolId("N:MyApp"),
                SymbolEdgeKind.Invokes,
                EdgeScope.CrossProject));
    }

    [Fact]
    public void TypeNameMatching_CreatesReferencesEdge_WhenScriptReferencesType()
    {
        // Script References "System.IO.Path", snapshot has a Type node with FQN "System.IO.Path"
        var scriptNode = BuildScriptNode("script:deploy.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");

        var edges = new[]
        {
            BuildEdge("script:deploy.ps1", "System.IO.Path", SymbolEdgeKind.References)
        };

        var snapshot = BuildMixedSnapshot([scriptNode, typeNode], edges);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SymbolEdge(
                new SymbolId("script:deploy.ps1"),
                new SymbolId("T:System.IO.Path"),
                SymbolEdgeKind.References,
                EdgeScope.CrossProject));
    }

    [Fact]
    public void NoCSsharpSymbols_ReturnsEmptyList()
    {
        // Snapshot with only script nodes → returns empty list (no crashes)
        var scriptNode = BuildScriptNode("script:test.sh", SymbolKind.Script);
        var snapshot = BuildMixedSnapshot([scriptNode], []);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().BeEmpty();
    }

    [Fact]
    public void NoScriptSymbols_ReturnsEmptyList()
    {
        // Snapshot with only C# nodes → returns empty list
        var typeNode = BuildCSharpTypeNode("MyApp.MyClass", "MyApp");
        var snapshot = BuildMixedSnapshot([typeNode], []);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateEdgePrevention_DoesNotDuplicateExistingEdge()
    {
        // Same cross-language edge already exists → not duplicated
        var scriptNode = BuildScriptNode("script:build.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");

        // Existing edge already in snapshot + a references edge that would trigger detection
        var existingCrossEdge = new SymbolEdge(
            new SymbolId("script:build.ps1"),
            new SymbolId("T:System.IO.Path"),
            SymbolEdgeKind.References,
            EdgeScope.CrossProject);

        var triggerEdge = BuildEdge("script:build.ps1", "System.IO.Path", SymbolEdgeKind.References);

        var snapshot = BuildMixedSnapshot(
            [scriptNode, typeNode],
            [existingCrossEdge, triggerEdge]);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().BeEmpty("edge already exists in the snapshot");
    }

    [Fact]
    public void MultipleScripts_ReferencingSameType_EachGetsOwnEdge()
    {
        var script1 = BuildScriptNode("script:a.ps1", SymbolKind.Script);
        var script2 = BuildScriptNode("script:b.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");

        var edges = new[]
        {
            BuildEdge("script:a.ps1", "System.IO.Path", SymbolEdgeKind.References),
            BuildEdge("script:b.ps1", "System.IO.Path", SymbolEdgeKind.References)
        };

        var snapshot = BuildMixedSnapshot([script1, script2, typeNode], edges);

        var result = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        result.Should().HaveCount(2);
        result.Should().Contain(e =>
            e.From.Value == "script:a.ps1" && e.To.Value == "T:System.IO.Path");
        result.Should().Contain(e =>
            e.From.Value == "script:b.ps1" && e.To.Value == "T:System.IO.Path");
    }

    [Fact]
    public void DotnetCommandParser_ParsesTestFilterCorrectly()
    {
        var result = DotnetCommandParser.ParseDotnetCommand(
            "dotnet test --filter \"FullyQualifiedName~Foo\"");

        result.Should().NotBeNull();
        result!.Verb.Should().Be("test");
        result.FilterExpression.Should().Be("FullyQualifiedName~Foo");
    }

    [Fact]
    public void DotnetCommandParser_ParsesBuildWithProject()
    {
        var result = DotnetCommandParser.ParseDotnetCommand(
            "dotnet build ./src/App.csproj");

        result.Should().NotBeNull();
        result!.Verb.Should().Be("build");
        result.ProjectPath.Should().Be("./src/App.csproj");
    }

    [Fact]
    public void DotnetCommandParser_ReturnsNull_ForNonDotnetCommand()
    {
        var result = DotnetCommandParser.ParseDotnetCommand("npm install");

        result.Should().BeNull();
    }

    [Fact]
    public void DotnetCommandParser_ParsesRunWithProject()
    {
        var result = DotnetCommandParser.ParseDotnetCommand(
            "dotnet run --project src/Server");

        result.Should().NotBeNull();
        result!.Verb.Should().Be("run");
        result.ProjectPath.Should().Be("src/Server");
    }
}
