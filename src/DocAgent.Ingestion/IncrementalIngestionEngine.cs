using DocAgent.Core;

namespace DocAgent.Ingestion;

/// <summary>
/// Orchestrates incremental ingestion: detects file changes via SHA-256 manifest comparison,
/// re-parses only changed projects, merges unchanged symbols from the previous snapshot,
/// and produces a new <see cref="SymbolGraphSnapshot"/> with <see cref="IngestionMetadata"/>.
/// </summary>
public sealed class IncrementalIngestionEngine
{
    private readonly ISymbolGraphBuilder _builder;
    private readonly SnapshotStore _store;
    private readonly string _artifactsDir;
    private readonly Action<string>? _logWarning;

    private static readonly string ManifestFileName = "file-hashes.json";

    /// <summary>
    /// Optional injectable build delegate for testing. When set, bypasses the real Roslyn builder.
    /// Signature: (ProjectInventory inventory, DocInputSet docs, CancellationToken ct)
    /// </summary>
    internal Func<ProjectInventory, DocInputSet, CancellationToken, Task<SymbolGraphSnapshot>>? BuildOverride { get; set; }

    public IncrementalIngestionEngine(
        ISymbolGraphBuilder builder,
        SnapshotStore store,
        string artifactsDir,
        Action<string>? logWarning = null)
    {
        _builder = builder;
        _store = store;
        _artifactsDir = artifactsDir;
        _logWarning = logWarning;
    }

    /// <summary>
    /// Runs incremental ingestion.
    /// </summary>
    /// <param name="inventory">Projects and files to ingest.</param>
    /// <param name="docs">Pre-loaded XML doc inputs.</param>
    /// <param name="previousSnapshot">The previous snapshot to merge unchanged symbols from, or null for first run.</param>
    /// <param name="forceFullReingestion">When true, re-parses all projects regardless of changes.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SymbolGraphSnapshot> IngestAsync(
        ProjectInventory inventory,
        DocInputSet docs,
        SymbolGraphSnapshot? previousSnapshot,
        bool forceFullReingestion,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid().ToString("N");

        // Step 1: Enumerate .cs files and compute current manifest
        var allCsFiles = EnumerateCsFiles(inventory);
        var currentManifest = await FileHasher.ComputeManifestAsync(allCsFiles, ct).ConfigureAwait(false);

        // Step 2: Load previous manifest
        var manifestPath = Path.Combine(_artifactsDir, ManifestFileName);
        var previousManifest = await FileHasher.LoadAsync(manifestPath, ct).ConfigureAwait(false);

        // Step 3: Compute diff
        var diff = FileHasher.Diff(previousManifest, currentManifest);

        // Step 4: Determine if full re-ingestion is needed
        bool doFullReingestion = forceFullReingestion || previousSnapshot is null || previousManifest is null;

        SymbolGraphSnapshot newSnapshot;
        bool wasFullReingestion;

        if (doFullReingestion)
        {
            wasFullReingestion = true;
            newSnapshot = await BuildSnapshotAsync(inventory, docs, ct).ConfigureAwait(false);
        }
        else if (!diff.HasChanges)
        {
            // No changes — return previous snapshot with updated metadata
            wasFullReingestion = false;
            var metadata = new IngestionMetadata(
                RunId: runId,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                WasFullReingestion: false,
                FileChanges: []);

            await FileHasher.SaveAsync(currentManifest, manifestPath, ct).ConfigureAwait(false);

            return previousSnapshot! with { IngestionMetadata = metadata };
        }
        else
        {
            wasFullReingestion = false;

            // Step 5: Determine which projects need re-parsing
            // Include removed files — a removed .cs file means the project must be re-parsed to drop those symbols
            var changedAndAdded = new HashSet<string>(
                diff.ChangedFiles.Concat(diff.RemovedFiles),
                StringComparer.OrdinalIgnoreCase);

            var changedProjects = inventory.ProjectFiles
                .Where(projectFile => changedAndAdded.Any(f => IsFileInProjectDirectory(f, projectFile)))
                .ToList();

            var unchangedProjects = inventory.ProjectFiles
                .Except(changedProjects, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Step 6: Build only changed projects
            var changedInventory = inventory with { ProjectFiles = changedProjects };
            var partialSnapshot = await BuildSnapshotAsync(changedInventory, docs, ct).ConfigureAwait(false);

            // Step 7: Determine which file paths belong to changed projects (for filtering preserved nodes)
            var changedProjectDirs = new HashSet<string>(
                changedProjects.Select(p => Path.GetDirectoryName(p) ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            // Step 8: Merge — preserve nodes from previous snapshot that are NOT in re-parsed project dirs
            var preservedNodes = previousSnapshot!.Nodes
                .Where(n => !IsNodeInProjectDirs(n, changedProjectDirs))
                .ToList();

            var preservedEdges = previousSnapshot.Edges
                .Where(e => !IsEdgeInProjectDirs(e, previousSnapshot.Nodes, changedProjectDirs))
                .ToList();

            // Combine and deduplicate by SymbolId (new wins over old)
            var newNodeIds = new HashSet<SymbolId>(partialSnapshot.Nodes.Select(n => n.Id));
            var mergedNodes = preservedNodes
                .Where(n => !newNodeIds.Contains(n.Id))
                .Concat(partialSnapshot.Nodes)
                .ToList();

            var newEdgeSet = new HashSet<(SymbolId, SymbolId, SymbolEdgeKind)>(
                partialSnapshot.Edges.Select(e => (e.From, e.To, e.Kind)));
            var mergedEdges = preservedEdges
                .Where(e => !newEdgeSet.Contains((e.From, e.To, e.Kind)))
                .Concat(partialSnapshot.Edges)
                .ToList();

            // Determine project name from the original inventory
            var projectName = inventory.SolutionFiles.Count > 0
                ? Path.GetFileNameWithoutExtension(inventory.SolutionFiles[0])
                : inventory.ProjectFiles.Count > 0
                    ? Path.GetFileNameWithoutExtension(inventory.ProjectFiles[0])
                    : previousSnapshot.ProjectName;

            newSnapshot = new SymbolGraphSnapshot(
                SchemaVersion: previousSnapshot.SchemaVersion,
                ProjectName: projectName,
                SourceFingerprint: partialSnapshot.SourceFingerprint,
                ContentHash: null,
                CreatedAt: DateTimeOffset.UtcNow,
                Nodes: SymbolSorter.SortNodes(mergedNodes),
                Edges: SymbolSorter.SortEdges(mergedEdges));
        }

        // Step 9: Build IngestionMetadata with file change records
        var fileChanges = BuildFileChangeRecords(diff, newSnapshot);
        var ingestionMetadata = new IngestionMetadata(
            RunId: runId,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            WasFullReingestion: wasFullReingestion,
            FileChanges: fileChanges);

        newSnapshot = newSnapshot with { IngestionMetadata = ingestionMetadata };

        // Step 10: Save manifest ONLY after successful snapshot construction
        await FileHasher.SaveAsync(currentManifest, manifestPath, ct).ConfigureAwait(false);

        return newSnapshot;
    }

    // ── Build helpers ────────────────────────────────────────────────────────

    private async Task<SymbolGraphSnapshot> BuildSnapshotAsync(
        ProjectInventory inventory,
        DocInputSet docs,
        CancellationToken ct)
    {
        if (BuildOverride is not null)
            return await BuildOverride(inventory, docs, ct).ConfigureAwait(false);

        return await _builder.BuildAsync(inventory, docs, ct).ConfigureAwait(false);
    }

    // ── File enumeration ─────────────────────────────────────────────────────

    private static List<string> EnumerateCsFiles(ProjectInventory inventory)
    {
        var csFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectFile in inventory.ProjectFiles)
        {
            var projectDir = Path.GetDirectoryName(projectFile);
            if (projectDir is null || !Directory.Exists(projectDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                csFiles.Add(file);
            }
        }

        return csFiles.OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    // ── Project directory membership ─────────────────────────────────────────

    /// <summary>Returns true if the given source file path is under the same directory as the project file.</summary>
    private static bool IsFileInProjectDirectory(string filePath, string projectFilePath)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath);
        if (projectDir is null)
            return false;

        // Normalize separators for cross-platform comparison
        var normalizedFile = Path.GetFullPath(filePath);
        var normalizedDir = Path.GetFullPath(projectDir);

        if (!normalizedDir.EndsWith(Path.DirectorySeparatorChar))
            normalizedDir += Path.DirectorySeparatorChar;

        return normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNodeInProjectDirs(SymbolNode node, HashSet<string> projectDirs)
    {
        if (node.Span is null)
            return false;

        var fileDir = Path.GetDirectoryName(Path.GetFullPath(node.Span.FilePath));
        if (fileDir is null)
            return false;

        // Check if the node's file is under any of the changed project directories
        foreach (var projDir in projectDirs)
        {
            var normalizedProjDir = Path.GetFullPath(projDir);
            if (!normalizedProjDir.EndsWith(Path.DirectorySeparatorChar))
                normalizedProjDir += Path.DirectorySeparatorChar;

            var normalizedFilePath = Path.GetFullPath(node.Span.FilePath);
            if (normalizedFilePath.StartsWith(normalizedProjDir, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsEdgeInProjectDirs(
        SymbolEdge edge,
        IReadOnlyList<SymbolNode> nodes,
        HashSet<string> projectDirs)
    {
        // An edge is considered "from changed project" if either endpoint node is in a changed project dir
        var fromNode = nodes.FirstOrDefault(n => n.Id == edge.From);
        if (fromNode is not null && IsNodeInProjectDirs(fromNode, projectDirs))
            return true;

        return false;
    }

    // ── IngestionMetadata construction ───────────────────────────────────────

    private static IReadOnlyList<FileChangeRecord> BuildFileChangeRecords(
        ManifestDiff diff,
        SymbolGraphSnapshot snapshot)
    {
        var records = new List<FileChangeRecord>();

        // Build a lookup: file path → list of symbol IDs whose span is in that file
        var fileToSymbols = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in snapshot.Nodes)
        {
            if (node.Span is null)
                continue;

            var filePath = node.Span.FilePath;
            if (!fileToSymbols.TryGetValue(filePath, out var list))
            {
                list = [];
                fileToSymbols[filePath] = list;
            }

            list.Add(node.Id.Value);
        }

        foreach (var file in diff.AddedFiles)
        {
            var affected = fileToSymbols.TryGetValue(file, out var syms) ? (IReadOnlyList<string>)syms : [];
            records.Add(new FileChangeRecord(file, FileChangeKind.Added, affected));
        }

        foreach (var file in diff.ModifiedFiles)
        {
            var affected = fileToSymbols.TryGetValue(file, out var syms) ? (IReadOnlyList<string>)syms : [];
            records.Add(new FileChangeRecord(file, FileChangeKind.Modified, affected));
        }

        foreach (var file in diff.RemovedFiles)
        {
            records.Add(new FileChangeRecord(file, FileChangeKind.Removed, []));
        }

        return records;
    }
}
