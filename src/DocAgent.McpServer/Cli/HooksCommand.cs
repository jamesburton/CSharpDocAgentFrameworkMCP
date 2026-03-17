namespace DocAgent.McpServer.Cli;

/// <summary>CLI command that installs or removes DocAgent git hooks (post-commit, post-merge).</summary>
public static class HooksCommand
{
    private const string SentinelBegin = "# BEGIN docagent-mcp";
    private const string SentinelEnd = "# END docagent-mcp";
    private static readonly string[] HookNames = ["post-commit", "post-merge"];

    /// <summary>
    /// Runs the hooks command.
    /// </summary>
    /// <param name="args">Command-line arguments. First element should be "enable" or "disable".</param>
    /// <param name="workingDir">Directory to treat as the working directory (defaults to current directory).</param>
    /// <param name="userConfigPath">Optional path to the user config file (defaults to <see cref="UserConfig.DefaultConfigPath"/>).</param>
    public static async Task<int> RunAsync(
        string[] args,
        string? workingDir = null,
        string? userConfigPath = null)
    {
        var cwd = workingDir ?? Directory.GetCurrentDirectory();
        var subcommand = args.Length > 0 ? args[0] : string.Empty;

        if (subcommand is not ("enable" or "disable"))
        {
            PrintUsage();
            return 1;
        }

        var hooksDir = Path.Combine(cwd, ".git", "hooks");
        if (!Directory.Exists(hooksDir))
        {
            Console.Error.WriteLine($"error: .git/hooks directory not found in '{cwd}'");
            Console.Error.WriteLine("Make sure you are running this command from a git repository root.");
            return 1;
        }

        if (subcommand == "enable")
            return await EnableAsync(hooksDir, userConfigPath);

        // subcommand == "disable"
        return await DisableAsync(hooksDir);
    }

    private static async Task<int> EnableAsync(string hooksDir, string? userConfigPath)
    {
        var userConfig = await UserConfig.LoadAsync(userConfigPath);
        var hookBody = BuildHookBody(userConfig?.HostingMode ?? HostingMode.A);

        foreach (var hookName in HookNames)
        {
            var hookPath = Path.Combine(hooksDir, hookName);
            string existingContent;

            if (File.Exists(hookPath))
            {
                existingContent = await File.ReadAllTextAsync(hookPath);
            }
            else
            {
                existingContent = "#!/bin/sh\n";
            }

            var updatedContent = ApplySentinelBlock(existingContent, hookBody);
            await File.WriteAllTextAsync(hookPath, updatedContent);

            TryChmodExecutable(hookPath);
        }

        return 0;
    }

    private static async Task<int> DisableAsync(string hooksDir)
    {
        foreach (var hookName in HookNames)
        {
            var hookPath = Path.Combine(hooksDir, hookName);
            if (!File.Exists(hookPath))
                continue;

            var content = await File.ReadAllTextAsync(hookPath);
            var stripped = RemoveSentinelBlock(content);

            // If remaining content is empty or just a shebang, delete the file
            if (IsEffectivelyEmpty(stripped))
            {
                File.Delete(hookPath);
            }
            else
            {
                await File.WriteAllTextAsync(hookPath, stripped);
            }
        }

        return 0;
    }

    /// <summary>
    /// Builds the full sentinel block including the command line for the given hosting mode.
    /// </summary>
    private static string BuildHookBody(HostingMode mode)
    {
        var command = mode switch
        {
            HostingMode.B => "\"$HOME/.docagent/bin/docagent\" update --quiet || true",
            _ => "docagent update --quiet || true"
        };

        return $"{SentinelBegin}\n{command}\n{SentinelEnd}";
    }

    /// <summary>
    /// Applies the sentinel block to the existing hook content.
    /// If a sentinel block already exists, replaces it in-place (idempotent).
    /// Otherwise appends the block.
    /// </summary>
    private static string ApplySentinelBlock(string existingContent, string hookBody)
    {
        var beginIndex = existingContent.IndexOf(SentinelBegin, StringComparison.Ordinal);

        if (beginIndex >= 0)
        {
            // Sentinel already exists — replace in-place
            var endIndex = existingContent.IndexOf(SentinelEnd, beginIndex, StringComparison.Ordinal);
            if (endIndex >= 0)
            {
                var afterEnd = endIndex + SentinelEnd.Length;
                // Consume trailing newline if present
                if (afterEnd < existingContent.Length && existingContent[afterEnd] == '\n')
                    afterEnd++;

                var before = existingContent[..beginIndex];
                var after = existingContent[afterEnd..];
                return before + hookBody + "\n" + after;
            }
        }

        // No sentinel found — append
        var content = existingContent;
        if (!content.EndsWith('\n'))
            content += "\n";

        return content + hookBody + "\n";
    }

    /// <summary>
    /// Removes lines from SentinelBegin through SentinelEnd (inclusive).
    /// </summary>
    private static string RemoveSentinelBlock(string content)
    {
        var lines = content.Split('\n');
        var result = new List<string>();
        var inBlock = false;

        foreach (var line in lines)
        {
            if (line.TrimEnd() == SentinelBegin)
            {
                inBlock = true;
                continue;
            }

            if (line.TrimEnd() == SentinelEnd)
            {
                inBlock = false;
                continue;
            }

            if (!inBlock)
                result.Add(line);
        }

        return string.Join("\n", result);
    }

    /// <summary>
    /// Returns true if the content is blank or contains only a shebang line.
    /// </summary>
    private static bool IsEffectivelyEmpty(string content)
    {
        var trimmed = content.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed == "#!/bin/sh";
    }

    /// <summary>
    /// Best-effort chmod +x on Unix systems. Silently ignored on Windows.
    /// </summary>
    private static void TryChmodExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{path}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(info);
                proc?.WaitForExit();
            }
            catch
            {
                // Best-effort; ignore failures
            }
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: docagent hooks <subcommand>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Subcommands:");
        Console.Error.WriteLine("  enable   Install docagent git hooks (post-commit, post-merge)");
        Console.Error.WriteLine("  disable  Remove docagent git hooks");
    }
}
