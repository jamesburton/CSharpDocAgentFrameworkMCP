namespace DocAgent.Core;

/// <summary>Represents a project within a solution, including its path and direct dependencies.</summary>
public sealed record ProjectEntry(
    string Name,
    string Path,
    IReadOnlyList<string> DependsOn);

/// <summary>Represents a directed dependency edge between two projects in the project DAG.</summary>
public sealed record ProjectEdge(
    string From,
    string To);

/// <summary>
/// Lightweight metadata-only summary of a per-project snapshot.
/// Replaces storing the full <see cref="SymbolGraphSnapshot"/> in the solution snapshot,
/// avoiding triple in-memory accumulation for large solutions.
/// </summary>
public sealed record ProjectSnapshotSummary(
    string ProjectName,
    string? FilePath,
    int NodeCount,
    int EdgeCount,
    string? ContentHash);

/// <summary>
/// Top-level aggregate that holds per-project snapshot summaries and the project dependency DAG.
/// Enables solution-level MCP tools and cross-project analysis.
/// </summary>
public sealed record SolutionSnapshot(
    string? SolutionName,
    IReadOnlyList<ProjectEntry> Projects,
    IReadOnlyList<ProjectEdge> ProjectDependencies,
    IReadOnlyList<ProjectSnapshotSummary> ProjectSnapshots,
    DateTimeOffset CreatedAt);
