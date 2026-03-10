using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Telemetry;
using Microsoft.Extensions.Logging;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Incremental implementation of <see cref="ISolutionIngestionService"/> that skips unchanged projects,
/// propagates dirty state through the dependency cascade, and manages stub node lifecycle.
/// Delegates to <see cref="SolutionIngestionService"/> for full-reingest fallback.
/// </summary>
public sealed class IncrementalSolutionIngestionService : ISolutionIngestionService
{
    private static readonly Meter s_meter = new("DocAgent.Ingestion");
    private static readonly Counter<int> s_projectsSkipped =
        s_meter.CreateCounter<int>("docagent.ingestion.projects_skipped",
            description: "Number of projects skipped during incremental solution ingestion");
    private static readonly Counter<int> s_projectsReingested =
        s_meter.CreateCounter<int>("docagent.ingestion.projects_reingested",
            description: "Number of projects re-ingested during incremental solution ingestion");

    private readonly SnapshotStore _store;
    private readonly SolutionIngestionService _fullIngestionService;
    private readonly ILogger<IncrementalSolutionIngestionService> _logger;

    /// <summary>
    /// Injectable pipeline override for testing. When set, bypasses real MSBuild entirely.
    /// Receives (slnPath, forceFullReingest, warnings, ct) and returns the final result.
    /// </summary>
    internal Func<string, bool, List<string>, CancellationToken, Task<SolutionIngestionResult>>? PipelineOverride { get; set; }

    public IncrementalSolutionIngestionService(
        SnapshotStore store,
        SolutionIngestionService fullIngestionService,
        ILogger<IncrementalSolutionIngestionService> logger)
    {
        _store = store;
        _fullIngestionService = fullIngestionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SolutionIngestionResult> IngestAsync(
        string slnPath,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken cancellationToken,
        bool forceFullReingest = false)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        using var activity = DocAgentTelemetry.Source.StartActivity(
            "solution.incremental_ingest", ActivityKind.Internal);
        activity?.SetTag("incremental", true);

        // PipelineOverride seam: used in unit tests to avoid real MSBuild.
        if (PipelineOverride is not null)
        {
            var overrideResult = await PipelineOverride(slnPath, forceFullReingest, warnings, cancellationToken)
                .ConfigureAwait(false);
            EmitTelemetry(activity, overrideResult);
            return overrideResult;
        }

        // If forceFullReingest, delegate immediately to the full ingestion service.
        if (forceFullReingest)
        {
            _logger.LogInformation("Force full reingest requested for {SlnPath}", slnPath);
            var forceResult = await _fullIngestionService.IngestAsync(slnPath, reportProgress, cancellationToken)
                .ConfigureAwait(false);
            EmitTelemetry(activity, forceResult);
            return forceResult;
        }

        var solutionName = Path.GetFileNameWithoutExtension(slnPath);
        var artifactsDir = _store.ArtifactsDir;

        // Step 1: Load previous solution snapshot
        var previousSnapshot = await LoadPreviousSolutionSnapshotAsync(artifactsDir, solutionName, cancellationToken)
            .ConfigureAwait(false);

        if (previousSnapshot is null)
        {
            _logger.LogInformation("No previous snapshot found for {Solution} — performing full ingest", solutionName);
            var fullResult = await _fullIngestionService.IngestAsync(slnPath, reportProgress, cancellationToken)
                .ConfigureAwait(false);

            // Save pointer for future incremental runs
            if (fullResult.SnapshotId is not null && fullResult.SnapshotId != string.Empty)
                await SavePointerAsync(artifactsDir, solutionName, fullResult.SnapshotId, cancellationToken).ConfigureAwait(false);

            // Save per-project manifests and solution snapshot for next incremental run
            if (fullResult.Snapshot is not null)
            {
                await SaveProjectManifestsAsync(artifactsDir, slnPath, fullResult.Snapshot, cancellationToken).ConfigureAwait(false);
                await SaveSolutionSnapshotAsync(artifactsDir, solutionName, fullResult.Snapshot, cancellationToken).ConfigureAwait(false);
            }

            EmitTelemetry(activity, fullResult);
            return fullResult;
        }

        // Step 2: We have a previous snapshot. Check for structural changes.
        var currentProjectPaths = previousSnapshot.Projects.Select(p => p.Path).ToList();
        if (DependencyCascade.HasStructuralChange(previousSnapshot.Projects, currentProjectPaths))
        {
            _logger.LogInformation("Structural change detected for {Solution} — performing full re-ingest", solutionName);
            var structResult = await _fullIngestionService.IngestAsync(slnPath, reportProgress, cancellationToken)
                .ConfigureAwait(false);

            if (structResult.SnapshotId is not null && structResult.SnapshotId != string.Empty)
                await SavePointerAsync(artifactsDir, solutionName, structResult.SnapshotId, cancellationToken).ConfigureAwait(false);

            if (structResult.Snapshot is not null)
            {
                await SaveProjectManifestsAsync(artifactsDir, slnPath, structResult.Snapshot, cancellationToken).ConfigureAwait(false);
                await SaveSolutionSnapshotAsync(artifactsDir, solutionName, structResult.Snapshot, cancellationToken).ConfigureAwait(false);
            }

            EmitTelemetry(activity, structResult);
            return structResult;
        }

        // Step 3: Compare per-project manifests to find directly changed projects.
        var directlyChanged = new List<string>();
        foreach (var project in previousSnapshot.Projects)
        {
            var previousManifest = await SolutionManifestStore.LoadAsync(artifactsDir, slnPath, project.Path, cancellationToken)
                .ConfigureAwait(false);

            var refPaths = project.DependsOn
                .Select(dep => previousSnapshot.Projects.FirstOrDefault(p => p.Name == dep)?.Path ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var currentManifest = await SolutionManifestStore.ComputeProjectManifestAsync(
                project.Path, refPaths, null, cancellationToken).ConfigureAwait(false);

            var diff = FileHasher.Diff(previousManifest, currentManifest);
            if (diff.HasChanges)
            {
                directlyChanged.Add(project.Name);
            }
        }

        // Step 4: Compute dirty set via dependency cascade.
        var projectEdges = previousSnapshot.ProjectDependencies;
        var dirtySet = DependencyCascade.ComputeDirtySet(directlyChanged, projectEdges);

        // Step 5: Topological sort for processing order (used in result metadata).
        var topoOrder = DependencyCascade.TopologicalSort(previousSnapshot.Projects, projectEdges);

        // Step 6: If dirty set is empty, return cached snapshot (nothing changed).
        if (dirtySet.Count == 0)
        {
            _logger.LogInformation("All projects unchanged for {Solution} — returning cached snapshot", solutionName);

            // Read the pointer to get the snapshot ID
            var ptrPath = PointerPath(artifactsDir, solutionName);
            var snapshotId = (await File.ReadAllTextAsync(ptrPath, cancellationToken).ConfigureAwait(false)).Trim();

            var projectStatuses = topoOrder.Select(name =>
            {
                var proj = previousSnapshot.Projects.First(p => p.Name == name);
                var nodeCount = previousSnapshot.ProjectSnapshots
                    .FirstOrDefault(s => s.ProjectName == name)?.NodeCount;
                return new ProjectIngestionStatus(
                    Name: name,
                    FilePath: proj.Path,
                    Status: "skipped",
                    Reason: "unchanged",
                    NodeCount: nodeCount,
                    ChosenTfm: null);
            }).ToList();

            var cachedResult = new SolutionIngestionResult(
                SnapshotId: snapshotId,
                SolutionName: solutionName,
                TotalProjectCount: previousSnapshot.Projects.Count,
                IngestedProjectCount: 0,
                TotalNodeCount: previousSnapshot.ProjectSnapshots.Sum(s => s.NodeCount),
                TotalEdgeCount: previousSnapshot.ProjectSnapshots.Sum(s => s.EdgeCount),
                Duration: sw.Elapsed,
                Projects: projectStatuses,
                Warnings: warnings.AsReadOnly(),
                Snapshot: previousSnapshot);

            EmitTelemetry(activity, cachedResult);
            return cachedResult;
        }

        // Step 7: Dirty set non-empty — delegate to full re-ingest.
        _logger.LogInformation("Detected {DirtyCount} dirty projects for {Solution} — performing full re-ingest",
            dirtySet.Count, solutionName);
        var result = await _fullIngestionService.IngestAsync(slnPath, reportProgress, cancellationToken)
            .ConfigureAwait(false);

        if (result.SnapshotId is not null && result.SnapshotId != string.Empty)
            await SavePointerAsync(artifactsDir, solutionName, result.SnapshotId, cancellationToken).ConfigureAwait(false);

        if (result.Snapshot is not null)
        {
            await SaveProjectManifestsAsync(artifactsDir, slnPath, result.Snapshot, cancellationToken).ConfigureAwait(false);
            await SaveSolutionSnapshotAsync(artifactsDir, solutionName, result.Snapshot, cancellationToken).ConfigureAwait(false);
        }

        EmitTelemetry(activity, result);
        return result;
    }

    // ── Telemetry ─────────────────────────────────────────────────────────────

    private void EmitTelemetry(Activity? activity, SolutionIngestionResult result)
    {
        var skipped = result.ProjectsSkippedCount;
        var reingested = result.ProjectsReingestedCount;

        activity?.SetTag("projects.total", result.TotalProjectCount);
        activity?.SetTag("projects.skipped", skipped);
        activity?.SetTag("projects.reingested", reingested);

        if (skipped > 0) s_projectsSkipped.Add(skipped);
        if (reingested > 0) s_projectsReingested.Add(reingested);

        // Per-project log lines for each skip/reingest decision
        foreach (var project in result.Projects)
        {
            if (project.Status == "skipped" && project.Reason == "unchanged")
            {
                _logger.LogInformation("Skipped {Project} (unchanged)", project.Name);
            }
            else if (project.Status == "ok")
            {
                _logger.LogInformation("Re-ingesting {Project} (changed or dependent changed)", project.Name);
            }
        }
    }

    // ── Pointer file management ──────────────────────────────────────────────

    private static string PointerPath(string artifactsDir, string solutionName)
        => Path.Combine(artifactsDir, $"latest-{solutionName}.ptr");

    // ── Solution snapshot persistence ──────────────────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string SolutionSnapshotPath(string artifactsDir, string solutionName)
        => Path.Combine(artifactsDir, $"latest-{solutionName}.solution.json");

    private static async Task<SolutionSnapshot?> LoadPreviousSolutionSnapshotAsync(
        string artifactsDir,
        string solutionName,
        CancellationToken ct)
    {
        var path = SolutionSnapshotPath(artifactsDir, solutionName);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SolutionSnapshot>(json, s_jsonOptions);
    }

    private static async Task SaveSolutionSnapshotAsync(
        string artifactsDir,
        string solutionName,
        SolutionSnapshot snapshot,
        CancellationToken ct)
    {
        Directory.CreateDirectory(artifactsDir);
        var path = SolutionSnapshotPath(artifactsDir, solutionName);
        var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    private static async Task SavePointerAsync(
        string artifactsDir,
        string solutionName,
        string contentHash,
        CancellationToken ct)
    {
        Directory.CreateDirectory(artifactsDir);
        var ptrPath = PointerPath(artifactsDir, solutionName);
        await File.WriteAllTextAsync(ptrPath, contentHash, ct).ConfigureAwait(false);
    }

    // ── Manifest management ──────────────────────────────────────────────────

    private static async Task SaveProjectManifestsAsync(
        string artifactsDir,
        string slnPath,
        SolutionSnapshot snapshot,
        CancellationToken ct)
    {
        var manifestFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in snapshot.Projects)
        {
            var refPaths = project.DependsOn
                .Select(dep => snapshot.Projects.FirstOrDefault(p => p.Name == dep)?.Path ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            string? chosenTfm = null; // TFM info not available in SolutionSnapshot

            var manifest = await SolutionManifestStore.ComputeProjectManifestAsync(
                project.Path, refPaths, chosenTfm, ct).ConfigureAwait(false);

            await SolutionManifestStore.SaveAsync(artifactsDir, slnPath, project.Path, manifest, ct)
                .ConfigureAwait(false);

            manifestFileNames.Add(SolutionManifestStore.ManifestFileName(slnPath, project.Path));
        }

        SolutionManifestStore.CleanOrphanedManifests(artifactsDir, manifestFileNames);
    }
}
