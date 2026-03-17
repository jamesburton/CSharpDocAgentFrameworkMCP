using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class InitCommandTests : IDisposable
{
    private readonly string _workingDir;

    public InitCommandTests()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDir))
            Directory.Delete(_workingDir, recursive: true);
    }

    // ── Helper: create a fake .sln file ──────────────────────────────────────

    private string CreateFakeSln(string name = "App.sln")
    {
        var path = Path.Combine(_workingDir, name);
        File.WriteAllText(path, "Microsoft Visual Studio Solution File");
        return path;
    }

    private string CreateFakeCsproj(string name = "App.csproj")
    {
        var path = Path.Combine(_workingDir, name);
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return path;
    }

    // ── Test 1: --primary writes docagent.project.json ───────────────────────

    [Fact]
    public async Task RunAsync_WithPrimaryFlag_WritesProjectJson()
    {
        var slnPath = CreateFakeSln();

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", slnPath, "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        File.Exists(configPath).Should().BeTrue("docagent.project.json should be written");

        var loaded = await ProjectConfig.LoadAsync(configPath);
        loaded.Should().NotBeNull();
        loaded!.PrimarySource.Should().Be(slnPath);
    }

    // ── Test 2: Writes .gitignore entry ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_WithPrimaryFlag_WritesGitignoreEntry()
    {
        var slnPath = CreateFakeSln();

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", slnPath, "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var gitignorePath = Path.Combine(_workingDir, ".gitignore");
        File.Exists(gitignorePath).Should().BeTrue(".gitignore should be created");

        var content = await File.ReadAllTextAsync(gitignorePath);
        content.Should().Contain(".docagent/artifacts/", "gitignore should include artifacts directory");
    }

    // ── Test 3: .gitignore is idempotent ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_CalledTwice_GitignoreHasNoDuplicates()
    {
        var slnPath = CreateFakeSln();
        var args = new[] { "--primary", slnPath, "--yes", "--no-hooks" };

        await InitCommand.RunAsync(args, workingDir: _workingDir);
        await InitCommand.RunAsync(args, workingDir: _workingDir);

        var gitignorePath = Path.Combine(_workingDir, ".gitignore");
        var content = await File.ReadAllTextAsync(gitignorePath);

        var occurrences = content.Split('\n')
            .Count(line => line.Trim() == ".docagent/artifacts/");

        occurrences.Should().Be(1, "gitignore entry should appear exactly once after double run");
    }

    // ── Test 4: Writes CLAUDE.md block with sentinel ──────────────────────────

    [Fact]
    public async Task RunAsync_WithPrimaryFlag_WritesClamdMdBlock()
    {
        var slnPath = CreateFakeSln();

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", slnPath, "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var claudeMdPath = Path.Combine(_workingDir, "CLAUDE.md");
        File.Exists(claudeMdPath).Should().BeTrue("CLAUDE.md should be created");

        var content = await File.ReadAllTextAsync(claudeMdPath);
        content.Should().Contain("<!-- docagent -->", "opening sentinel should be present");
        content.Should().Contain("<!-- /docagent -->", "closing sentinel should be present");
        content.Should().Contain("search_symbols", "MCP tool list should be present in block");
    }

    // ── Test 5: CLAUDE.md is idempotent ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_CalledTwice_ClaudeMdSentinelAppearsExactlyOnce()
    {
        var slnPath = CreateFakeSln();
        var args = new[] { "--primary", slnPath, "--yes", "--no-hooks" };

        await InitCommand.RunAsync(args, workingDir: _workingDir);
        await InitCommand.RunAsync(args, workingDir: _workingDir);

        var claudeMdPath = Path.Combine(_workingDir, "CLAUDE.md");
        var content = await File.ReadAllTextAsync(claudeMdPath);

        var openCount = CountOccurrences(content, "<!-- docagent -->");
        var closeCount = CountOccurrences(content, "<!-- /docagent -->");

        openCount.Should().Be(1, "opening sentinel should appear exactly once after double run");
        closeCount.Should().Be(1, "closing sentinel should appear exactly once after double run");
    }

    // ── Test 6: Missing primary → non-zero exit, no files written ────────────

    [Fact]
    public async Task RunAsync_MissingPrimary_ReturnsNonZeroAndWritesNoFiles()
    {
        var nonexistentPath = Path.Combine(_workingDir, "DoesNotExist.sln");

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", nonexistentPath, "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().NotBe(0, "missing primary source should result in non-zero exit");

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        File.Exists(configPath).Should().BeFalse("no files should be written when primary is invalid");

        var gitignorePath = Path.Combine(_workingDir, ".gitignore");
        File.Exists(gitignorePath).Should().BeFalse("gitignore should not be written when primary is invalid");

        var claudeMdPath = Path.Combine(_workingDir, "CLAUDE.md");
        File.Exists(claudeMdPath).Should().BeFalse("CLAUDE.md should not be written when primary is invalid");
    }

    // ── Test 7: --secondary dotnet:<path> → included in project JSON ─────────

    [Fact]
    public async Task RunAsync_WithSecondaryFlag_IncludedInProjectJson()
    {
        var slnPath = CreateFakeSln();
        var secondaryPath = Path.Combine(_workingDir, "Secondary.csproj");
        File.WriteAllText(secondaryPath, "<Project />");

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", slnPath, "--secondary", $"dotnet:{secondaryPath}", "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        var loaded = await ProjectConfig.LoadAsync(configPath);

        loaded.Should().NotBeNull();
        loaded!.SecondarySources.Should().HaveCount(1, "one secondary source should be recorded");
        loaded.SecondarySources[0].Type.Should().Be("dotnet");
        loaded.SecondarySources[0].Path.Should().Be(secondaryPath);
    }

    // ── Test 8: No --primary, single .sln in cwd → auto-detect ──────────────

    [Fact]
    public async Task RunAsync_NoPrimaryFlag_AutoDetectsSingleSln()
    {
        CreateFakeSln("MySolution.sln");

        var exitCode = await InitCommand.RunAsync(
            args: ["--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0, "auto-detection of single .sln should succeed");

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        File.Exists(configPath).Should().BeTrue();

        var loaded = await ProjectConfig.LoadAsync(configPath);
        loaded!.PrimarySource.Should().Contain("MySolution.sln");
    }

    // ── Test 9: No --primary, single .csproj in cwd → auto-detect ────────────

    [Fact]
    public async Task RunAsync_NoPrimaryFlag_AutoDetectsSingleCsproj()
    {
        CreateFakeCsproj("MyProject.csproj");

        var exitCode = await InitCommand.RunAsync(
            args: ["--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0, "auto-detection of single .csproj should succeed");

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        File.Exists(configPath).Should().BeTrue();

        var loaded = await ProjectConfig.LoadAsync(configPath);
        loaded!.PrimarySource.Should().Contain("MyProject.csproj");
    }

    // ── Test 10: No --primary, no .sln/.csproj → non-zero ───────────────────

    [Fact]
    public async Task RunAsync_NoPrimaryFlag_NoSlnOrCsproj_ReturnsNonZero()
    {
        // Empty working dir — no auto-detectable files
        var exitCode = await InitCommand.RunAsync(
            args: ["--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().NotBe(0, "no auto-detectable project should result in non-zero exit");
    }

    // ── Test 11: CLAUDE.md block replaces existing block in-place ────────────

    [Fact]
    public async Task RunAsync_ExistingClamdMdWithBlock_ReplacesBlockInPlace()
    {
        var slnPath = CreateFakeSln();
        var claudeMdPath = Path.Combine(_workingDir, "CLAUDE.md");

        // Write an existing CLAUDE.md with a stale docagent block
        await File.WriteAllTextAsync(claudeMdPath,
            """
            # My Project

            Some existing content.

            <!-- docagent -->
            ## Old DocAgent Block
            Old content that should be replaced.
            <!-- /docagent -->

            More content after the block.
            """);

        var exitCode = await InitCommand.RunAsync(
            args: ["--primary", slnPath, "--yes", "--no-hooks"],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var content = await File.ReadAllTextAsync(claudeMdPath);

        // Old content should be gone
        content.Should().NotContain("Old content that should be replaced");

        // New content should be present
        content.Should().Contain("search_symbols");

        // Surrounding content should be preserved
        content.Should().Contain("# My Project");
        content.Should().Contain("Some existing content.");
        content.Should().Contain("More content after the block.");

        // Only one block should exist
        CountOccurrences(content, "<!-- docagent -->").Should().Be(1);
    }

    // ── Test 12: Multiple --secondary flags → all included ───────────────────

    [Fact]
    public async Task RunAsync_MultipleSecondaryFlags_AllIncluded()
    {
        var slnPath = CreateFakeSln();

        var exitCode = await InitCommand.RunAsync(
            args:
            [
                "--primary", slnPath,
                "--secondary", "dotnet:Secondary1.csproj",
                "--secondary", "typescript:frontend",
                "--yes",
                "--no-hooks"
            ],
            workingDir: _workingDir);

        exitCode.Should().Be(0);

        var configPath = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        var loaded = await ProjectConfig.LoadAsync(configPath);

        loaded.Should().NotBeNull();
        loaded!.SecondarySources.Should().HaveCount(2, "both secondary sources should be recorded");
        loaded.SecondarySources.Should().Contain(s => s.Type == "dotnet" && s.Path == "Secondary1.csproj");
        loaded.SecondarySources.Should().Contain(s => s.Type == "typescript" && s.Path == "frontend");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
