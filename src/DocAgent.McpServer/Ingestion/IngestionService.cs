using System.Collections.Concurrent;
using System.Diagnostics;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Orchestrates the full ingestion pipeline: discover → parse → snapshot → index.
/// Concurrent ingestion of different projects runs in parallel.
/// Same-project ingestion is serialized via per-path <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class IngestionService : IIngestionService
{
    private readonly SnapshotStore _store;
    private readonly ISearchIndex _index;
    private readonly DocAgentServerOptions _options;
    private readonly ILogger<IngestionService> _logger;

    // Optional injectable pipeline for testing. When null the real Roslyn pipeline is used.
    internal Func<ProjectInventory, List<string>, CancellationToken, Task<SymbolGraphSnapshot>>? PipelineOverride { get; set; }

    // Per-path semaphores — serialize same-project ingestion, allow different projects in parallel.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public IngestionService(
        SnapshotStore store,
        ISearchIndex index,
        IOptions<DocAgentServerOptions> options,
        ILogger<IngestionService> logger)
    {
        _store = store;
        _index = index;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> IngestAsync(
        string path,
        string? includeGlob,
        string? excludeGlob,
        bool forceReindex,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(path);
        var semaphore = _locks.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));

        // Linked token: respect both caller cancellation and configurable timeout.
        var timeoutSeconds = _options.IngestionTimeoutSeconds > 0 ? _options.IngestionTimeoutSeconds : 300;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            // Stage 1/4 — Discover
            if (reportProgress is not null)
                await reportProgress(1, 4, "Discovering projects...").ConfigureAwait(false);

            var source = new LocalProjectSource(logWarning: w => warnings.Add(w));
            var inventory = await source.DiscoverAsync(new ProjectLocator(normalizedPath), ct).ConfigureAwait(false);

            // Apply include/exclude glob filters to project files.
            var filteredProjects = ApplyGlobs(inventory.ProjectFiles, includeGlob, excludeGlob);
            inventory = inventory with { ProjectFiles = filteredProjects };

            var projectCount = filteredProjects.Count;

            // Stage 2/4 — Parse
            if (reportProgress is not null)
                await reportProgress(2, 4, $"Parsing ({projectCount} projects)...").ConfigureAwait(false);

            SymbolGraphSnapshot snapshot;
            if (PipelineOverride is not null)
            {
                snapshot = await PipelineOverride(inventory, warnings, ct).ConfigureAwait(false);
            }
            else
            {
                var parser = new XmlDocParser();
                var resolver = new InheritDocResolver();
                var builder = new RoslynSymbolGraphBuilder(parser, resolver, logWarning: w => warnings.Add(w));
                var docs = new DocInputSet(new Dictionary<string, string>());
                try
                {
                    snapshot = await builder.BuildAsync(inventory, docs, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Ingestion build failed for {Path}", normalizedPath);
                    throw;
                }
            }

            // Fix CreatedAt via with-expression (builder sets it to default).
            snapshot = snapshot with { CreatedAt = DateTimeOffset.UtcNow };

            // Stage 3/4 — Save snapshot
            if (reportProgress is not null)
                await reportProgress(3, 4, "Saving snapshot...").ConfigureAwait(false);

            var saved = await _store.SaveAsync(snapshot, ct: ct).ConfigureAwait(false);

            // Stage 4/4 — Index (soft failure — snapshot already saved)
            if (reportProgress is not null)
                await reportProgress(4, 4, "Indexing...").ConfigureAwait(false);

            string? indexError = null;
            try
            {
                await _index.IndexAsync(saved, ct, forceReindex).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                indexError = ex.Message;
                _logger.LogWarning(ex, "Index update failed for snapshot {SnapshotId} — snapshot was saved but index may be stale", saved.ContentHash);
            }

            sw.Stop();

            return new IngestionResult(
                SnapshotId: saved.ContentHash ?? saved.SourceFingerprint,
                SymbolCount: saved.Nodes.Count,
                ProjectCount: projectCount,
                Duration: sw.Elapsed,
                Warnings: warnings.AsReadOnly(),
                IndexError: indexError);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // ── Glob filtering ──────────────────────────────────────────────────────

    private static IReadOnlyList<string> ApplyGlobs(
        IReadOnlyList<string> projectFiles,
        string? includeGlob,
        string? excludeGlob)
    {
        if (includeGlob is null && excludeGlob is null)
            return projectFiles;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(includeGlob ?? "**");
        if (excludeGlob is not null)
            matcher.AddExclude(excludeGlob);

        return projectFiles.Where(p =>
        {
            var root = Path.GetPathRoot(p) ?? "";
            var rel = p.Substring(root.Length);
            return matcher.Match(root, rel).HasMatches;
        }).ToList();
    }
}
