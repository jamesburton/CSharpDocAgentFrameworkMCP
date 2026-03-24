using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Ingestion;

public sealed class DockerfileParserTests : IDisposable
{
    private readonly string _tempDir;

    public DockerfileParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"docker-parser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteDockerfile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SimpleSingleStage_ProducesCorrectNodes()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            WORKDIR /app
            COPY . .
            RUN dotnet build
            ENTRYPOINT ["dotnet", "run"]
            """);

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        // One stage + instructions for WORKDIR, COPY, RUN, ENTRYPOINT
        var stage = nodes.Should().Contain(n => n.Kind == SymbolKind.DockerStage).Which;
        stage.DisplayName.Should().Contain("Stage");

        var instructions = nodes.Where(n => n.Kind == SymbolKind.DockerInstruction).ToList();
        instructions.Should().HaveCount(4);
        instructions.Should().Contain(n => n.DisplayName.StartsWith("WORKDIR"));
        instructions.Should().Contain(n => n.DisplayName.StartsWith("COPY"));
        instructions.Should().Contain(n => n.DisplayName.StartsWith("RUN"));
        instructions.Should().Contain(n => n.DisplayName.StartsWith("ENTRYPOINT"));
    }

    [Fact]
    public void MultiStageBuild_CopyFrom_ProducesDependsOnEdge()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /src
            COPY . .
            RUN dotnet publish -o /app

            FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
            WORKDIR /app
            COPY --from=build /app .
            ENTRYPOINT ["dotnet", "MyApp.dll"]
            """);

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        var stages = nodes.Where(n => n.Kind == SymbolKind.DockerStage).ToList();
        stages.Should().HaveCount(2);

        // DependsOn edge from runtime stage to build stage
        var dependsOn = edges.Where(e => e.Kind == SymbolEdgeKind.DependsOn).ToList();
        dependsOn.Should().ContainSingle();
        dependsOn[0].From.Value.Should().Contain("runtime");
        dependsOn[0].To.Value.Should().Contain("build");
    }

    [Fact]
    public void NamedStages_StageNameInSymbolId()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM node:18 AS frontend
            RUN npm install

            FROM dotnet/sdk:10.0 AS backend
            RUN dotnet build
            """);

        var (nodes, _) = DockerfileParser.Parse(path, _tempDir);

        var stages = nodes.Where(n => n.Kind == SymbolKind.DockerStage).ToList();
        stages.Should().HaveCount(2);
        stages.Should().Contain(n => n.Id.Value.Contains("Stage/frontend"));
        stages.Should().Contain(n => n.Id.Value.Contains("Stage/backend"));
    }

    [Fact]
    public void UnnamedStages_NumericIndexUsed()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM node:18
            RUN npm install

            FROM dotnet/sdk:10.0
            RUN dotnet build
            """);

        var (nodes, _) = DockerfileParser.Parse(path, _tempDir);

        var stages = nodes.Where(n => n.Kind == SymbolKind.DockerStage).ToList();
        stages.Should().HaveCount(2);
        stages.Should().Contain(n => n.Id.Value.Contains("Stage/0"));
        stages.Should().Contain(n => n.Id.Value.Contains("Stage/1"));
    }

    [Fact]
    public void RunDotnet_ProducesInvokesEdge()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            RUN dotnet publish -c Release -o /app
            """);

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        var invokes = edges.Where(e => e.Kind == SymbolEdgeKind.Invokes).ToList();
        invokes.Should().ContainSingle();
        invokes[0].To.Value.Should().Be("T:tool/dotnet/publish");
    }

    [Fact]
    public void Expose_PortCapturedInDisplayName()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/aspnet:10.0
            EXPOSE 8080
            ENTRYPOINT ["dotnet", "app.dll"]
            """);

        var (nodes, _) = DockerfileParser.Parse(path, _tempDir);

        var expose = nodes.FirstOrDefault(n =>
            n.Kind == SymbolKind.DockerInstruction && n.DisplayName.Contains("EXPOSE"));
        expose.Should().NotBeNull();
        expose!.DisplayName.Should().Contain("8080");
    }

    [Fact]
    public void LineContinuations_MultiLineRunHandledAsSingleInstruction()
    {
        var path = WriteDockerfile("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            RUN dotnet restore \
                && dotnet build \
                && dotnet test
            """);

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        // Should be one RUN instruction (merged), not three
        var runInstructions = nodes.Where(n =>
            n.Kind == SymbolKind.DockerInstruction &&
            n.DisplayName.StartsWith("RUN")).ToList();
        runInstructions.Should().ContainSingle();

        // Should detect all dotnet commands within the merged line
        var invokes = edges.Where(e => e.Kind == SymbolEdgeKind.Invokes).ToList();
        invokes.Should().HaveCount(3);
        invokes.Should().Contain(e => e.To.Value == "T:tool/dotnet/restore");
        invokes.Should().Contain(e => e.To.Value == "T:tool/dotnet/build");
        invokes.Should().Contain(e => e.To.Value == "T:tool/dotnet/test");
    }

    [Fact]
    public void EmptyFile_ReturnsEmptyLists()
    {
        var path = WriteDockerfile("Dockerfile.empty", "");

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void MissingFile_ReturnsEmptyLists()
    {
        var path = Path.Combine(_tempDir, "Dockerfile.missing");

        var (nodes, edges) = DockerfileParser.Parse(path, _tempDir);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }
}
