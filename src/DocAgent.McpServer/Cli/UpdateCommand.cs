using System.Text.Json;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.McpServer.Cli;

/// <summary>
/// Implements the <c>docagent update</c> command: reads <c>docagent.project.json</c>,
/// ingests the primary source and any supported secondary sources, then writes a JSON
/// summary to stdout.
/// </summary>
public static class UpdateCommand
{
    /// <summary>
    /// Runs the update command.
    /// </summary>
    /// <param name="args">Command-line arguments after the <c>update</c> verb (e.g. <c>--quiet</c>).</param>
    /// <param name="workingDir">
    /// Directory to look for <c>docagent.project.json</c>. Defaults to <see cref="Directory.GetCurrentDirectory"/>.
    /// </param>
    /// <param name="ingestionService">
    /// Pre-built ingestion service for testing. When <see langword="null"/> the command
    /// registers MSBuild and builds a <see cref="CliServiceProvider"/> to obtain one.
    /// </param>
    /// <param name="writeOutput">
    /// Sink for the JSON summary line written to stdout. Defaults to <see cref="Console.WriteLine"/>.
    /// </param>
    /// <returns>0 on success, 1 on configuration or fatal ingestion error.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        string? workingDir = null,
        IIngestionService? ingestionService = null,
        Action<string>? writeOutput = null)
    {
        var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
        var dir   = workingDir ?? Directory.GetCurrentDirectory();
        var emit  = writeOutput ?? Console.WriteLine;

        // ── 1. Load project config ───────────────────────────────────────────
        var config = await ProjectConfig.LoadFromDirAsync(dir);
        if (config is null)
        {
            Console.Error.WriteLine(
                $"[docagent update] No '{ProjectConfig.DefaultFileName}' found in '{dir}'. " +
                "Run 'docagent init' first.");
            return 1;
        }

        // ── 2. Validate artifacts dir (pre-flight) ───────────────────────────
        var rawArtifacts = string.IsNullOrWhiteSpace(config.ArtifactsDir)
            ? ".docagent/artifacts"
            : config.ArtifactsDir;

        // Expand env vars / tilde, then resolve relative to working dir
        var absArtifacts = PathExpander.Expand(rawArtifacts, baseDir: dir)!;

        try
        {
            Directory.CreateDirectory(absArtifacts);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[docagent update] Cannot create artifacts directory '{absArtifacts}': {ex.Message}");
            return 1;
        }

        // ── 3. Resolve ingestion service ─────────────────────────────────────
        if (ingestionService is null)
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();

            var sp = CliServiceProvider.Build(absArtifacts);
            ingestionService = sp.GetRequiredService<IIngestionService>();
        }

        // ── 4. Collect sources to ingest ─────────────────────────────────────
        //   Primary: always
        //   Secondary: only "dotnet" and "typescript" types, and only when the path exists
        var sources = new List<string>();

        // Primary — expand env vars / tilde, then resolve relative to working dir
        var absPrimary = PathExpander.Expand(config.PrimarySource, baseDir: dir)!;
        sources.Add(absPrimary);

        foreach (var secondary in config.SecondarySources)
        {
            var t = secondary.Type?.ToLowerInvariant();
            if (t is not ("dotnet" or "typescript"))
            {
                if (!quiet)
                    Console.Error.WriteLine(
                        $"[docagent update] Skipping secondary source of unknown type '{secondary.Type}'.");
                continue;
            }

            var absSecondary = PathExpander.Expand(secondary.Path, baseDir: dir)!;

            if (!File.Exists(absSecondary) && !Directory.Exists(absSecondary))
            {
                Console.Error.WriteLine(
                    $"[docagent update] Secondary source not found, skipping: '{absSecondary}'");
                continue;
            }

            sources.Add(absSecondary);
        }

        // ── 5. Ingest each source ────────────────────────────────────────────
        var totalSymbols   = 0;
        var totalProjects  = 0;
        var totalMs        = 0L;
        var lastHash       = string.Empty;
        var warnings       = new List<string>();

        foreach (var source in sources)
        {
            if (!quiet)
                Console.Error.WriteLine($"[docagent update] Ingesting: {source}");

            try
            {
                var result = await ingestionService.IngestAsync(
                    source,
                    includeGlob: null,
                    excludeGlob: null,
                    forceReindex: false,
                    reportProgress: null,
                    cancellationToken: CancellationToken.None,
                    forceFullReingestion: false);

                totalSymbols  += result.SymbolCount;
                totalProjects += result.ProjectCount;
                totalMs       += (long)result.Duration.TotalMilliseconds;
                lastHash       = result.SnapshotId;

                warnings.AddRange(result.Warnings);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[docagent update] Ingestion failed for '{source}': {ex.Message}");
                // Non-primary failures don't abort — but primary failure should
                if (source == absPrimary)
                    return 1;
            }
        }

        // ── 6. Emit JSON summary to stdout ───────────────────────────────────
        var summary = new
        {
            status          = "ok",
            projectsIngested = totalProjects,
            symbolCount     = totalSymbols,
            durationMs      = totalMs,
            snapshotHash    = lastHash,
            warnings        = warnings.Count > 0 ? warnings : null
        };

        emit(JsonSerializer.Serialize(summary, SummaryJsonOptions));
        return 0;
    }

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        PropertyNamingPolicy    = JsonNamingPolicy.CamelCase,
        WriteIndented           = false,
        DefaultIgnoreCondition  = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
