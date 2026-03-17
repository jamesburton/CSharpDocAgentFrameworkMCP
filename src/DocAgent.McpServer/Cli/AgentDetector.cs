namespace DocAgent.McpServer.Cli;

/// <summary>Identifies an installed AI agent tool and the config file to patch for MCP registration.</summary>
public sealed record AgentInfo(
    string AgentId,
    string DisplayName,
    string ConfigPath,
    string JsonKeyPath);

/// <summary>
/// Probes the local machine for installed AI agent tools using file-system checks only
/// (no registry, no process enumeration).
/// </summary>
public sealed class AgentDetector
{
    private readonly string _home;

    /// <summary>
    /// Initialises a new <see cref="AgentDetector"/>.
    /// </summary>
    /// <param name="homeDir">
    /// Override the user home directory. When <see langword="null"/> the real home folder is used.
    /// Pass a temp directory in tests to isolate file-system checks.
    /// </param>
    public AgentDetector(string? homeDir = null)
    {
        _home = homeDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Returns a list of all detected AI agent installations on this machine.</summary>
    public IReadOnlyList<AgentInfo> Detect()
    {
        var found = new List<AgentInfo>();

        TryDetectClaudeDesktop(found);
        TryDetectClaudeCodeCli(found);
        TryDetectCursor(found);
        TryDetectWindsurf(found);
        TryDetectOpenCode(found);
        TryDetectZed(found);

        return found;
    }

    // ── Claude Desktop ────────────────────────────────────────────────────────

    private void TryDetectClaudeDesktop(List<AgentInfo> found)
    {
        var configDir = GetClaudeDesktopConfigDir();
        if (configDir is null || !Directory.Exists(configDir))
            return;

        found.Add(new AgentInfo(
            AgentId: "claude-desktop",
            DisplayName: "Claude Desktop",
            ConfigPath: Path.Combine(configDir, "claude_desktop_config.json"),
            JsonKeyPath: "mcpServers"));
    }

    private string? GetClaudeDesktopConfigDir()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer the real APPDATA, but when a custom homeDir was injected (test mode)
            // we derive a test-local path so that real APPDATA doesn't leak into tests.
            var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (_home != realHome)
            {
                // Test-isolated path: <homeDir>\AppData\Roaming\Claude
                return Path.Combine(_home, "AppData", "Roaming", "Claude");
            }

            var appData = Environment.GetEnvironmentVariable("APPDATA");
            return appData is not null ? Path.Combine(appData, "Claude") : null;
        }

        if (OperatingSystem.IsMacOS())
            return Path.Combine(_home, "Library", "Application Support", "Claude");

        // Linux
        return Path.Combine(_home, ".config", "claude-desktop");
    }

    // ── Claude Code CLI ───────────────────────────────────────────────────────

    private void TryDetectClaudeCodeCli(List<AgentInfo> found)
    {
        var settingsPath = Path.Combine(_home, ".claude", "settings.json");
        if (!File.Exists(settingsPath))
            return;

        found.Add(new AgentInfo(
            AgentId: "claude-code-cli",
            DisplayName: "Claude Code CLI",
            ConfigPath: settingsPath,
            JsonKeyPath: "mcpServers"));
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    private void TryDetectCursor(List<AgentInfo> found)
    {
        var cursorHome = Path.Combine(_home, ".cursor");
        bool detected = Directory.Exists(cursorHome);

        if (!detected && OperatingSystem.IsWindows())
        {
            // Only check the real LOCALAPPDATA when we are NOT in test-isolation mode.
            // In test isolation mode (_home differs from the real user profile) we only
            // look for a cursor dir under the injected home so tests stay hermetic.
            var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (_home == realHome)
            {
                var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (localAppData is not null)
                    detected = Directory.Exists(Path.Combine(localAppData, "Programs", "cursor"));
            }
            else
            {
                // Test-isolated: treat <homeDir>\AppData\Local\Programs\cursor as the probe path
                detected = Directory.Exists(
                    Path.Combine(_home, "AppData", "Local", "Programs", "cursor"));
            }
        }

        if (!detected)
            return;

        found.Add(new AgentInfo(
            AgentId: "cursor",
            DisplayName: "Cursor",
            ConfigPath: Path.Combine(_home, ".cursor", "mcp.json"),
            JsonKeyPath: "mcpServers"));
    }

    // ── Windsurf ──────────────────────────────────────────────────────────────

    private void TryDetectWindsurf(List<AgentInfo> found)
    {
        var windsurfDir = Path.Combine(_home, ".codeium", "windsurf");
        if (!Directory.Exists(windsurfDir))
            return;

        found.Add(new AgentInfo(
            AgentId: "windsurf",
            DisplayName: "Windsurf",
            ConfigPath: Path.Combine(windsurfDir, "mcp_config.json"),
            JsonKeyPath: "mcpServers"));
    }

    // ── OpenCode ──────────────────────────────────────────────────────────────

    private void TryDetectOpenCode(List<AgentInfo> found)
    {
        var opencodeDir = Path.Combine(_home, ".config", "opencode");
        if (!Directory.Exists(opencodeDir))
            return;

        found.Add(new AgentInfo(
            AgentId: "opencode",
            DisplayName: "OpenCode",
            ConfigPath: Path.Combine(opencodeDir, "config.json"),
            JsonKeyPath: "mcp.servers"));
    }

    // ── Zed ───────────────────────────────────────────────────────────────────

    private void TryDetectZed(List<AgentInfo> found)
    {
        var zedDir = GetZedConfigDir();
        if (!Directory.Exists(zedDir))
            return;

        found.Add(new AgentInfo(
            AgentId: "zed",
            DisplayName: "Zed",
            ConfigPath: Path.Combine(zedDir, "settings.json"),
            JsonKeyPath: "context_servers"));
    }

    private string GetZedConfigDir()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(_home, "Library", "Application Support", "Zed");

        return Path.Combine(_home, ".config", "zed");
    }
}
