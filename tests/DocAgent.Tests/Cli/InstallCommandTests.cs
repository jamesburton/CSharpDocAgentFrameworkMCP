using System.Text.Json;
using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class InstallCommandTests : IDisposable
{
    private readonly string _homeDir;
    private readonly string _userConfigPath;
    private readonly string _claudePluginsDir;

    public InstallCommandTests()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        _homeDir = Path.Combine(root, "home");
        Directory.CreateDirectory(_homeDir);

        _userConfigPath = Path.Combine(root, "config", "docagent.json");
        _claudePluginsDir = Path.Combine(root, "plugins", "docagent");
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_homeDir)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    // ── Test 1: --yes flag + cursor dir exists → writes ~/.cursor/mcp.json ───

    [Fact]
    public async Task RunAsync_YesFlagCursorDirExists_WritesCursorMcpJson()
    {
        // Create cursor dir to simulate Cursor installation
        var cursorDir = Path.Combine(_homeDir, ".cursor");
        Directory.CreateDirectory(cursorDir);

        var output = new List<string>();
        var exitCode = await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: s => output.Add(s));

        exitCode.Should().Be(0);

        var mcpJsonPath = Path.Combine(_homeDir, ".cursor", "mcp.json");
        File.Exists(mcpJsonPath).Should().BeTrue("--yes should write the cursor mcp.json without prompting");

        var json = await File.ReadAllTextAsync(mcpJsonPath);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement
            .GetProperty("mcpServers")
            .TryGetProperty("docagent", out _)
            .Should().BeTrue("docagent entry should be present in cursor mcp.json");
    }

    // ── Test 2: No agents detected → exits 0 with appropriate message ────────

    [Fact]
    public async Task RunAsync_NoAgentsDetected_ExitsZeroWithMessage()
    {
        // _homeDir has no known agent directories — detection returns empty

        var output = new List<string>();
        var exitCode = await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: s => output.Add(s));

        exitCode.Should().Be(0, "no agents is a valid no-op, not an error");

        var allOutput = string.Join("\n", output);
        allOutput.Should().ContainEquivalentOf("No supported agents detected",
            "a message about no agents should be printed");
    }

    // ── Test 3: Saves UserConfig to the specified path with correct mode ──────

    [Fact]
    public async Task RunAsync_SavesUserConfigWithCorrectHostingMode()
    {
        var exitCode = await InstallCommand.RunAsync(
            args: ["--yes", "--mode", "B"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        exitCode.Should().Be(0);

        File.Exists(_userConfigPath).Should().BeTrue("UserConfig should be written to the specified path");

        var loaded = await UserConfig.LoadAsync(_userConfigPath);
        loaded.Should().NotBeNull();
        loaded!.HostingMode.Should().Be(HostingMode.B, "mode B was specified via --mode flag");
        loaded.InstalledAt.Should().NotBe(default, "InstalledAt should be set");
    }

    // ── Test 4: Installs Claude skill files to the specified plugins dir ──────

    [Fact]
    public async Task RunAsync_InstallsSkillFilesToPluginsDir()
    {
        var exitCode = await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        exitCode.Should().Be(0);

        Directory.Exists(_claudePluginsDir).Should().BeTrue("plugins dir should be created");

        var files = Directory.GetFiles(_claudePluginsDir, "*.md", SearchOption.AllDirectories);
        files.Should().HaveCountGreaterThanOrEqualTo(2, "at least setup-project and update skill files should be written");

        var fileNames = files.Select(Path.GetFileName).ToList();
        fileNames.Should().Contain(f => f!.Contains("setup") || f!.Contains("setup-project"),
            "setup-project skill file should exist");
        fileNames.Should().Contain(f => f!.Contains("update"),
            "update skill file should exist");
    }

    // ── Test 5: Default mode is A when --mode not specified ──────────────────

    [Fact]
    public async Task RunAsync_DefaultMode_IsA()
    {
        await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        var loaded = await UserConfig.LoadAsync(_userConfigPath);
        loaded.Should().NotBeNull();
        loaded!.HostingMode.Should().Be(HostingMode.A, "default mode should be A");
    }

    // ── Test 6: Mode C parses correctly ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_ModeC_ParsedCorrectly()
    {
        await InstallCommand.RunAsync(
            args: ["--yes", "--mode", "C"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        var loaded = await UserConfig.LoadAsync(_userConfigPath);
        loaded.Should().NotBeNull();
        loaded!.HostingMode.Should().Be(HostingMode.C);
    }

    // ── Test 7: Detection summary printed with checkmarks ────────────────────

    [Fact]
    public async Task RunAsync_AgentDetected_PrintsSummaryWithCheckmark()
    {
        var cursorDir = Path.Combine(_homeDir, ".cursor");
        Directory.CreateDirectory(cursorDir);

        var output = new List<string>();
        await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: s => output.Add(s));

        var allOutput = string.Join("\n", output);
        allOutput.Should().Contain("Cursor", "detected agent name should appear in output");
    }

    // ── Test 8: Skill files contain correct frontmatter names ────────────────

    [Fact]
    public async Task RunAsync_SkillFiles_ContainCorrectNames()
    {
        await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        var files = Directory.GetFiles(_claudePluginsDir, "*.md", SearchOption.AllDirectories);
        files.Should().HaveCountGreaterThanOrEqualTo(2);

        var allContent = await Task.WhenAll(files.Select(f => File.ReadAllTextAsync(f)));
        var combined = string.Join("\n", allContent);

        combined.Should().Contain("docagent-setup-project", "setup skill should have correct name");
        combined.Should().Contain("docagent-update", "update skill should have correct name");
    }

    // ── Test 9: UserConfig InstalledAt is recent ──────────────────────────────

    [Fact]
    public async Task RunAsync_UserConfig_InstalledAtIsRecent()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: _ => { });

        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        var loaded = await UserConfig.LoadAsync(_userConfigPath);
        loaded.Should().NotBeNull();
        loaded!.InstalledAt.Should().BeAfter(before).And.BeBefore(after);
    }

    // ── Test 10: Multiple agents detected, --yes merges all ──────────────────

    [Fact]
    public async Task RunAsync_MultipleAgentsDetected_MergesAll()
    {
        Directory.CreateDirectory(Path.Combine(_homeDir, ".cursor"));
        Directory.CreateDirectory(Path.Combine(_homeDir, ".codeium", "windsurf"));

        var output = new List<string>();
        var exitCode = await InstallCommand.RunAsync(
            args: ["--yes"],
            homeDir: _homeDir,
            userConfigPath: _userConfigPath,
            claudePluginsDir: _claudePluginsDir,
            writeOutput: s => output.Add(s));

        exitCode.Should().Be(0);

        // Both config files should exist
        var cursorMcp = Path.Combine(_homeDir, ".cursor", "mcp.json");
        var windsurfMcp = Path.Combine(_homeDir, ".codeium", "windsurf", "mcp_config.json");

        File.Exists(cursorMcp).Should().BeTrue("cursor mcp.json should be written");
        File.Exists(windsurfMcp).Should().BeTrue("windsurf mcp_config.json should be written");
    }
}
