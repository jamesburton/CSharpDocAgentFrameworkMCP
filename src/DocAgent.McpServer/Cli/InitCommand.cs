namespace DocAgent.McpServer.Cli;

/// <summary>
/// Implements the <c>docagent init</c> command: sets up <c>docagent.project.json</c>,
/// updates <c>.gitignore</c>, upserts the DocAgent block in <c>CLAUDE.md</c>, and
/// optionally triggers ingestion and git hook installation.
/// </summary>
public static class InitCommand
{
    private const string GitignoreEntry = ".docagent/artifacts/";
    private const string SentinelOpen   = "<!-- docagent -->";
    private const string SentinelClose  = "<!-- /docagent -->";

    private static readonly string ClaudeBlock =
        $"""
        {SentinelOpen}
        ## DocAgent (code documentation MCP)
        This project uses DocAgent to serve symbol graph and documentation queries.
        - Re-ingest after significant code changes: run `/docagent:update` or `docagent update`
        - Git hooks for automatic re-ingest: see `docs/GitHooks.md`
        - MCP tools available: search_symbols, get_symbol, find_implementations, explain_project, and more
        {SentinelClose}
        """;

    /// <summary>
    /// Runs the init command.
    /// </summary>
    /// <param name="args">Command-line arguments after the <c>init</c> verb.</param>
    /// <param name="workingDir">
    /// Directory to initialise. Defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <returns>0 on success, 1 on validation or fatal error.</returns>
    public static async Task<int> RunAsync(string[] args, string? workingDir = null)
    {
        var dir = workingDir ?? Directory.GetCurrentDirectory();

        // ── 1. Parse flags ────────────────────────────────────────────────────
        var (primary, secondaries, yes, noHooks, ingest) = ParseArgs(args);

        // ── 2. Auto-detect primary if not provided ────────────────────────────
        if (string.IsNullOrEmpty(primary))
        {
            var detected = AutoDetectPrimary(dir);
            if (detected is null)
            {
                Console.Error.WriteLine(
                    "[docagent init] Could not auto-detect a .sln or .csproj in the current directory. " +
                    "Use --primary <path> to specify one.");
                return 1;
            }
            primary = detected;
        }

        // ── 3. Validate primary source ────────────────────────────────────────
        if (!File.Exists(primary))
        {
            Console.Error.WriteLine(
                $"[docagent init] Primary source not found: '{primary}'");
            return 1;
        }

        // ── 4. Write docagent.project.json ────────────────────────────────────
        var config = new ProjectConfig
        {
            PrimarySource    = primary,
            SecondarySources = secondaries
        };

        var configPath = Path.Combine(dir, ProjectConfig.DefaultFileName);
        await ProjectConfig.SaveAsync(config, configPath);

        // ── 5. Append .gitignore entry (idempotent) ───────────────────────────
        await UpdateGitignoreAsync(dir);

        // ── 6. Upsert CLAUDE.md block (idempotent) ────────────────────────────
        await UpsertClaudeMdAsync(dir);

        // ── 7. Optionally trigger ingest ──────────────────────────────────────
        if (ingest)
        {
            var updateCode = await UpdateCommand.RunAsync([], workingDir: dir);
            if (updateCode != 0)
                Console.Error.WriteLine("[docagent init] Ingestion step returned a non-zero exit code.");
        }

        // ── 8. Optionally enable hooks ────────────────────────────────────────
        if (!noHooks && yes)
        {
            var hooksCode = await HooksCommand.RunAsync(["enable"], workingDir: dir);
            if (hooksCode != 0)
                Console.Error.WriteLine("[docagent init] Hooks step returned a non-zero exit code (hooks not yet implemented).");
        }

        Console.Error.WriteLine($"[docagent init] Initialised successfully in '{dir}'.");
        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string? primary, List<SecondarySource> secondaries, bool yes, bool noHooks, bool ingest)
        ParseArgs(string[] args)
    {
        string?                  primary     = null;
        var                      secondaries = new List<SecondarySource>();
        var                      yes         = false;
        var                      noHooks     = false;
        var                      ingest      = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--primary" when i + 1 < args.Length:
                    primary = args[++i];
                    break;

                case "--secondary" when i + 1 < args.Length:
                    var raw = args[++i];
                    var colon = raw.IndexOf(':', StringComparison.Ordinal);
                    if (colon > 0)
                    {
                        secondaries.Add(new SecondarySource
                        {
                            Type = raw[..colon],
                            Path = raw[(colon + 1)..]
                        });
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"[docagent init] --secondary value must be in 'type:path' format, got: '{raw}'");
                    }
                    break;

                case "--yes":
                    yes = true;
                    break;

                case "--no-hooks":
                    noHooks = true;
                    break;

                case "--ingest":
                    ingest = true;
                    break;
            }
        }

        return (primary, secondaries, yes, noHooks, ingest);
    }

    /// <summary>
    /// Returns the single <c>.sln</c> file if exactly one exists, otherwise the single
    /// <c>.csproj</c> if exactly one exists; otherwise <see langword="null"/>.
    /// </summary>
    private static string? AutoDetectPrimary(string dir)
    {
        var slns = Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly);
        if (slns.Length == 1)
            return slns[0];

        // Also check .slnx
        var slnxs = Directory.GetFiles(dir, "*.slnx", SearchOption.TopDirectoryOnly);
        if (slnxs.Length == 1)
            return slnxs[0];

        var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
        if (csproj.Length == 1)
            return csproj[0];

        return null;
    }

    /// <summary>
    /// Appends <c>.docagent/artifacts/</c> to <c>.gitignore</c> if not already present.
    /// </summary>
    private static async Task UpdateGitignoreAsync(string dir)
    {
        var gitignorePath = Path.Combine(dir, ".gitignore");

        if (File.Exists(gitignorePath))
        {
            var lines = await File.ReadAllLinesAsync(gitignorePath);
            if (lines.Any(l => l.Trim() == GitignoreEntry))
                return; // already present — nothing to do
        }

        // Append the entry (with a trailing newline for clean diffs)
        await File.AppendAllTextAsync(gitignorePath, $"{GitignoreEntry}{Environment.NewLine}");
    }

    /// <summary>
    /// Creates or updates the DocAgent sentinel block inside <c>CLAUDE.md</c>.
    /// </summary>
    private static async Task UpsertClaudeMdAsync(string dir)
    {
        var claudeMdPath = Path.Combine(dir, "CLAUDE.md");

        if (!File.Exists(claudeMdPath))
        {
            // Create a brand-new file containing only our block
            await File.WriteAllTextAsync(claudeMdPath, ClaudeBlock + Environment.NewLine);
            return;
        }

        var content = await File.ReadAllTextAsync(claudeMdPath);

        var openIdx  = content.IndexOf(SentinelOpen,  StringComparison.Ordinal);
        var closeIdx = content.IndexOf(SentinelClose, StringComparison.Ordinal);

        if (openIdx >= 0 && closeIdx > openIdx)
        {
            // Block already exists — replace it in-place
            var before = content[..openIdx];
            var after  = content[(closeIdx + SentinelClose.Length)..];
            var updated = before + ClaudeBlock + after;
            await File.WriteAllTextAsync(claudeMdPath, updated);
        }
        else
        {
            // No existing block — append to the file
            var separator = content.EndsWith('\n') ? string.Empty : Environment.NewLine;
            await File.AppendAllTextAsync(claudeMdPath, separator + ClaudeBlock + Environment.NewLine);
        }
    }
}
