using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

public sealed class CIWorkflowParserTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ci-parser-{Guid.NewGuid():N}");

    public CIWorkflowParserTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    private string WriteGitHubWorkflow(string fileName, string content)
    {
        var dir = Path.Combine(_tempDir, ".github", "workflows");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteAzurePipeline(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  1. GitHub Actions basic
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubActions_Basic_ParsesWorkflowJobAndSteps()
    {
        var path = WriteGitHubWorkflow("ci.yml", """
            name: CI
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout
                    uses: actions/checkout@v4
                  - name: Build
                    run: dotnet build
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        // 1 workflow + 1 job + 2 steps = 4 nodes
        nodes.Should().HaveCount(4);
        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.CIWorkflow);
        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.CIJob);
        nodes.Where(n => n.Kind == SymbolKind.CIStep).Should().HaveCount(2);

        // Workflow Contains Job, Job Contains 2 Steps = 3 Contains edges
        edges.Where(e => e.Kind == SymbolEdgeKind.Contains).Should().HaveCount(3);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  2. GitHub Actions needs → DependsOn
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubActions_Needs_CreatesDependsOnEdge()
    {
        var path = WriteGitHubWorkflow("ci.yml", """
            name: CI
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Build
                    run: echo build
              deploy:
                needs: build
                runs-on: ubuntu-latest
                steps:
                  - name: Deploy
                    run: echo deploy
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        edges.Should().Contain(e =>
            e.Kind == SymbolEdgeKind.DependsOn &&
            e.From == new SymbolId("T:ci/github/ci.yml::Job/deploy") &&
            e.To == new SymbolId("T:ci/github/ci.yml::Job/build"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  3. GitHub Actions uses → Invokes
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubActions_Uses_CreatesInvokesEdge()
    {
        var path = WriteGitHubWorkflow("ci.yml", """
            name: CI
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        edges.Should().Contain(e =>
            e.Kind == SymbolEdgeKind.Invokes &&
            e.To == new SymbolId("T:ci/action/actions/checkout@v4"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  4. GitHub Actions run dotnet → Invokes
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubActions_RunDotnet_CreatesInvokesEdge()
    {
        var path = WriteGitHubWorkflow("ci.yml", """
            name: CI
            on: push
            jobs:
              test:
                runs-on: ubuntu-latest
                steps:
                  - name: Test
                    run: dotnet test
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        edges.Should().Contain(e =>
            e.Kind == SymbolEdgeKind.Invokes &&
            e.To == new SymbolId("T:ci/cmd/dotnet test"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  5. GitHub Actions triggers
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubActions_Triggers_ParsedIntoDocComment()
    {
        var path = WriteGitHubWorkflow("ci.yml", """
            name: CI
            on: [push, pull_request]
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - run: echo hi
            """);

        var (nodes, _) = CIWorkflowParser.Parse(path);

        var workflow = nodes.Single(n => n.Kind == SymbolKind.CIWorkflow);
        workflow.Docs.Should().NotBeNull();
        workflow.Docs!.Summary.Should().Contain("push");
        workflow.Docs!.Summary.Should().Contain("pull_request");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  6. Azure Pipelines basic
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AzurePipelines_Basic_ParsesWorkflowJobAndSteps()
    {
        var path = WriteAzurePipeline("azure-pipelines.yml", """
            trigger:
              - main
            pool:
              vmImage: ubuntu-latest
            jobs:
              - job: Build
                steps:
                  - script: dotnet build
                    displayName: Build project
                  - script: dotnet test
                    displayName: Run tests
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        // 1 workflow + 1 job + 2 steps = 4 nodes
        nodes.Should().HaveCount(4);
        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.CIWorkflow);
        nodes.Should().ContainSingle(n => n.Kind == SymbolKind.CIJob);
        nodes.Where(n => n.Kind == SymbolKind.CIStep).Should().HaveCount(2);

        // Contains: workflow→job, job→step1, job→step2
        edges.Where(e => e.Kind == SymbolEdgeKind.Contains).Should().HaveCount(3);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  7. Azure Pipelines dependsOn
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AzurePipelines_DependsOn_CreatesDependsOnEdges()
    {
        var path = WriteAzurePipeline("azure-pipelines.yml", """
            trigger:
              - main
            pool:
              vmImage: ubuntu-latest
            stages:
              - stage: Build
                jobs:
                  - job: Compile
                    steps:
                      - script: dotnet build
              - stage: Deploy
                dependsOn: Build
                jobs:
                  - job: Release
                    steps:
                      - script: echo deploy
            """);

        var (_, edges) = CIWorkflowParser.Parse(path);

        edges.Should().Contain(e =>
            e.Kind == SymbolEdgeKind.DependsOn &&
            e.From == new SymbolId("T:ci/azure/azure-pipelines.yml::Job/Deploy") &&
            e.To == new SymbolId("T:ci/azure/azure-pipelines.yml::Job/Build"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  8. Azure Pipelines task → Invokes
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AzurePipelines_Task_CreatesInvokesEdge()
    {
        var path = WriteAzurePipeline("azure-pipelines.yml", """
            trigger:
              - main
            pool:
              vmImage: ubuntu-latest
            jobs:
              - job: Build
                steps:
                  - task: DotNetCoreCLI@2
                    inputs:
                      command: build
            """);

        var (_, edges) = CIWorkflowParser.Parse(path);

        edges.Should().Contain(e =>
            e.Kind == SymbolEdgeKind.Invokes &&
            e.To == new SymbolId("T:ci/task/DotNetCoreCLI@2"));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  9. Empty/malformed YAML → empty lists
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void MalformedYaml_ReturnsEmptyLists()
    {
        var path = WriteGitHubWorkflow("bad.yml", """
            this: is: not: valid: yaml: {{{{
            """);

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void EmptyFile_ReturnsEmptyLists()
    {
        var path = WriteGitHubWorkflow("empty.yml", "");

        var (nodes, edges) = CIWorkflowParser.Parse(path);

        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    //  10. Workflow type detection
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/repo/.github/workflows/ci.yml", "", "GitHubActions")]
    [InlineData("/repo/.github/workflows/deploy.yaml", "", "GitHubActions")]
    [InlineData("/repo/azure-pipelines.yml", "", "AzurePipelines")]
    [InlineData("/repo/azure-pipelines.yaml", "", "AzurePipelines")]
    [InlineData("/repo/pipeline.yml", "trigger:\n  - main\npool:\n  vmImage: ubuntu", "AzurePipelines")]
    [InlineData("/repo/random.yml", "", "Unknown")]
    public void DetectWorkflowType_CorrectlyIdentifiesType(
        string filePath, string content, string expectedName)
    {
        var expected = Enum.Parse<CIWorkflowParser.WorkflowType>(expectedName);
        CIWorkflowParser.DetectWorkflowType(filePath, content).Should().Be(expected);
    }
}
