namespace DocAgent.McpServer.Cli;

public static class InstallCommand
{
    /// <summary>
    /// Runs the <c>install</c> command: detects installed agents, confirms with the user,
    /// writes MCP configs for each detected agent, saves user config, and installs Claude Code skill files.
    /// </summary>
    /// <param name="args">Command-line arguments (e.g. <c>--yes</c>, <c>--mode A|B|C</c>).</param>
    /// <param name="homeDir">Override home directory (for testing).</param>
    /// <param name="userConfigPath">Override user config path (for testing).</param>
    /// <param name="claudePluginsDir">Override Claude plugins directory (for testing).</param>
    /// <param name="writeOutput">Override output writer (for testing). Defaults to <see cref="Console.WriteLine"/>.</param>
    public static async Task<int> RunAsync(
        string[] args,
        string? homeDir = null,
        string? userConfigPath = null,
        string? claudePluginsDir = null,
        Action<string>? writeOutput = null)
    {
        var output = writeOutput ?? Console.WriteLine;

        // ── 1. Parse flags ────────────────────────────────────────────────────
        var yesFlag = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);
        var mode = ParseMode(args);

        // ── 2. Detect agents ──────────────────────────────────────────────────
        var detector = new AgentDetector(homeDir);
        var agents = detector.Detect();

        // ── 3. No agents found ────────────────────────────────────────────────
        if (agents.Count == 0)
        {
            output("No supported agents detected. See docs/Agents.md for supported agents.");
            await SaveUserConfigAsync(mode, userConfigPath);
            await WriteSkillFilesAsync(claudePluginsDir, homeDir, output);
            return 0;
        }

        // ── 4. Show detection summary ─────────────────────────────────────────
        output($"Detected {agents.Count} agent(s):");
        foreach (var agent in agents)
        {
            output($"  \u2713 {agent.DisplayName} ({agent.ConfigPath})");
        }

        // ── 5. Prompt for confirmation unless --yes ───────────────────────────
        if (!yesFlag)
        {
            Console.Write("Proceed? [Y/n] ");
            var answer = Console.ReadLine();
            if (string.Equals(answer?.Trim(), "n", StringComparison.OrdinalIgnoreCase))
            {
                output("Aborted.");
                return 0;
            }
        }

        // ── 6. Build MCP entry ────────────────────────────────────────────────
        var mcpEntry = ConfigMerger.BuildMcpEntry(mode);

        // ── 7. Merge into each agent config ───────────────────────────────────
        foreach (var agent in agents)
        {
            var conflict = await ConfigMerger.MergeAsync(
                agent.ConfigPath,
                agent.JsonKeyPath,
                mcpEntry,
                nonInteractive: yesFlag,
                yesFlag: yesFlag);

            var status = conflict switch
            {
                MergeConflict.None => "written",
                MergeConflict.Overwritten => "overwritten",
                MergeConflict.Skipped => "skipped (conflict)",
                _ => "unknown"
            };
            output($"  {agent.DisplayName}: {status}");
        }

        // ── 8. Save UserConfig ────────────────────────────────────────────────
        await SaveUserConfigAsync(mode, userConfigPath);

        // ── 9. Write skill files ──────────────────────────────────────────────
        await WriteSkillFilesAsync(claudePluginsDir, homeDir, output);

        // ── 10. Print summary ─────────────────────────────────────────────────
        output($"DocAgent installed for {agents.Count} agent(s). Mode: {mode}.");

        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HostingMode ParseMode(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].ToUpperInvariant() switch
                {
                    "A" => HostingMode.A,
                    "B" => HostingMode.B,
                    "C" => HostingMode.C,
                    _ => HostingMode.A
                };
            }
        }

        return HostingMode.A;
    }

    private static async Task SaveUserConfigAsync(HostingMode mode, string? userConfigPath)
    {
        var existing = await UserConfig.LoadAsync(userConfigPath);
        var config = existing ?? new UserConfig();
        config.HostingMode = mode;
        config.InstalledAt = DateTimeOffset.UtcNow;
        await UserConfig.SaveAsync(config, userConfigPath);
    }

    private static async Task WriteSkillFilesAsync(
        string? claudePluginsDir,
        string? homeDir,
        Action<string> output)
    {
        var resolvedHome = homeDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var pluginsDir = claudePluginsDir
            ?? Path.Combine(resolvedHome, ".claude", "plugins", "docagent");

        Directory.CreateDirectory(pluginsDir);

        var setupPath = Path.Combine(pluginsDir, "setup-project.md");
        var updatePath = Path.Combine(pluginsDir, "update.md");

        await File.WriteAllTextAsync(setupPath, SkillContent.SetupProject);
        await File.WriteAllTextAsync(updatePath, SkillContent.Update);

        output($"Skill files written to {pluginsDir}");
    }
}
