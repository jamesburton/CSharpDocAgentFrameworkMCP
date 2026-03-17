using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class AgentDetectorTests : IDisposable
{
    private readonly string _homeDir;

    public AgentDetectorTests()
    {
        _homeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_homeDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_homeDir))
            Directory.Delete(_homeDir, recursive: true);
    }

    // ── Test 1: no dirs exist → empty list ──────────────────────────────────

    [Fact]
    public void Detect_NoDirsExist_ReturnsEmpty()
    {
        var detector = new AgentDetector(homeDir: _homeDir);

        var results = detector.Detect();

        results.Should().BeEmpty();
    }

    // ── Test 2: cursor dir exists → finds cursor ─────────────────────────────

    [Fact]
    public void Detect_CursorDirExists_FindsCursor()
    {
        var cursorDir = Path.Combine(_homeDir, ".cursor");
        Directory.CreateDirectory(cursorDir);

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().ContainSingle(a => a.AgentId == "cursor");
        var cursor = results.First(a => a.AgentId == "cursor");
        cursor.DisplayName.Should().Be("Cursor");
        cursor.ConfigPath.Should().Be(Path.Combine(_homeDir, ".cursor", "mcp.json"));
        cursor.JsonKeyPath.Should().Be("mcpServers");
    }

    // ── Test 3: windsurf dir exists → finds windsurf ─────────────────────────

    [Fact]
    public void Detect_WindsurfDirExists_FindsWindsurf()
    {
        var windsurfDir = Path.Combine(_homeDir, ".codeium", "windsurf");
        Directory.CreateDirectory(windsurfDir);

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().ContainSingle(a => a.AgentId == "windsurf");
        var windsurf = results.First(a => a.AgentId == "windsurf");
        windsurf.DisplayName.Should().Be("Windsurf");
        windsurf.ConfigPath.Should().Be(Path.Combine(_homeDir, ".codeium", "windsurf", "mcp_config.json"));
        windsurf.JsonKeyPath.Should().Be("mcpServers");
    }

    // ── Test 4: claude settings.json exists → finds claude-code-cli ──────────

    [Fact]
    public void Detect_ClaudeSettingsExists_FindsClaudeCodeCli()
    {
        var claudeDir = Path.Combine(_homeDir, ".claude");
        Directory.CreateDirectory(claudeDir);
        var settingsPath = Path.Combine(claudeDir, "settings.json");
        File.WriteAllText(settingsPath, "{}");

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().ContainSingle(a => a.AgentId == "claude-code-cli");
        var cli = results.First(a => a.AgentId == "claude-code-cli");
        cli.DisplayName.Should().Be("Claude Code CLI");
        cli.ConfigPath.Should().Be(settingsPath);
        cli.JsonKeyPath.Should().Be("mcpServers");
    }

    // ── Test 5: AgentInfo has all required fields populated ──────────────────

    [Fact]
    public void AgentInfo_HasAllFieldsPopulated()
    {
        // Create opencode dir to get a detectable agent
        var opencodeDir = Path.Combine(_homeDir, ".config", "opencode");
        Directory.CreateDirectory(opencodeDir);

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        var opencode = results.Should().ContainSingle(a => a.AgentId == "opencode").Which;
        opencode.AgentId.Should().NotBeNullOrWhiteSpace();
        opencode.DisplayName.Should().NotBeNullOrWhiteSpace();
        opencode.ConfigPath.Should().NotBeNullOrWhiteSpace();
        opencode.JsonKeyPath.Should().NotBeNullOrWhiteSpace();
    }

    // ── Additional coverage tests ─────────────────────────────────────────────

    [Fact]
    public void Detect_OpencodeDirExists_FindsOpencode()
    {
        var opencodeDir = Path.Combine(_homeDir, ".config", "opencode");
        Directory.CreateDirectory(opencodeDir);

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().ContainSingle(a => a.AgentId == "opencode");
        var opencode = results.First(a => a.AgentId == "opencode");
        opencode.ConfigPath.Should().Be(Path.Combine(_homeDir, ".config", "opencode", "config.json"));
        opencode.JsonKeyPath.Should().Be("mcp.servers");
    }

    [Fact]
    public void Detect_MultipleDirsExist_FindsMultipleAgents()
    {
        Directory.CreateDirectory(Path.Combine(_homeDir, ".cursor"));
        Directory.CreateDirectory(Path.Combine(_homeDir, ".codeium", "windsurf"));

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().Contain(a => a.AgentId == "cursor");
        results.Should().Contain(a => a.AgentId == "windsurf");
        results.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void AgentInfo_IsRecord_WithValueEquality()
    {
        var a = new AgentInfo("cursor", "Cursor", "/path/mcp.json", "mcpServers");
        var b = new AgentInfo("cursor", "Cursor", "/path/mcp.json", "mcpServers");

        a.Should().Be(b);
    }

    [Fact]
    public void Detect_ZedConfigDir_FindsZed()
    {
        // On non-Mac platforms use ~/.config/zed/
        var zedDir = Path.Combine(_homeDir, ".config", "zed");
        Directory.CreateDirectory(zedDir);

        var detector = new AgentDetector(homeDir: _homeDir);
        var results = detector.Detect();

        results.Should().Contain(a => a.AgentId == "zed");
        var zed = results.First(a => a.AgentId == "zed");
        zed.ConfigPath.Should().Be(Path.Combine(zedDir, "settings.json"));
        zed.JsonKeyPath.Should().Be("context_servers");
    }
}
