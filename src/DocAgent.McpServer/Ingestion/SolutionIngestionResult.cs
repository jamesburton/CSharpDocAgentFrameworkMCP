namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Per-project outcome from solution ingestion.
/// </summary>
/// <param name="Name">The project name (without extension).</param>
/// <param name="FilePath">Absolute path to the .csproj file.</param>
/// <param name="Status">"ok", "skipped", or "failed".</param>
/// <param name="Reason">Human-readable reason for skipped/failed projects; null for ok.</param>
/// <param name="NodeCount">Number of symbol nodes contributed; null unless Status is "ok".</param>
/// <param name="ChosenTfm">The target framework moniker that was selected when deduplicating multi-targeted projects; null if not applicable.</param>
public sealed record ProjectIngestionStatus(
    string Name,
    string FilePath,
    string Status,
    string? Reason,
    int? NodeCount,
    string? ChosenTfm);

/// <summary>
/// Aggregate result from ingesting a .sln file.
/// </summary>
/// <param name="SnapshotId">Content hash of the persisted merged snapshot.</param>
/// <param name="SolutionName">Name derived from the .sln filename (without extension).</param>
/// <param name="TotalProjectCount">Total number of projects discovered in the solution (including non-C# and failed).</param>
/// <param name="IngestedProjectCount">Number of projects whose symbols were merged into the snapshot.</param>
/// <param name="TotalNodeCount">Total number of symbol nodes in the merged snapshot.</param>
/// <param name="TotalEdgeCount">Total number of symbol edges in the merged snapshot.</param>
/// <param name="Duration">Wall-clock time for the ingestion run.</param>
/// <param name="Projects">Per-project status details.</param>
/// <param name="Warnings">Non-fatal warnings accumulated during ingestion (e.g., MSBuildWorkspace diagnostics).</param>
public sealed record SolutionIngestionResult(
    string SnapshotId,
    string SolutionName,
    int TotalProjectCount,
    int IngestedProjectCount,
    int TotalNodeCount,
    int TotalEdgeCount,
    TimeSpan Duration,
    IReadOnlyList<ProjectIngestionStatus> Projects,
    IReadOnlyList<string> Warnings);
