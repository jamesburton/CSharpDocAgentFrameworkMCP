namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Ingests an entire .sln file, processing each C# project, and produces a flat merged
/// <see cref="DocAgent.Core.SymbolGraphSnapshot"/> persisted via <see cref="DocAgent.Ingestion.SnapshotStore"/>.
/// </summary>
public interface ISolutionIngestionService
{
    /// <summary>
    /// Opens the solution at <paramref name="slnPath"/>, processes all C# projects
    /// (skipping non-C#, deduplicating multi-targeted projects, handling MSBuild failures),
    /// stamps <c>ProjectOrigin</c> on every node, merges into a flat snapshot, and persists it.
    /// </summary>
    /// <param name="slnPath">Absolute path to the .sln file.</param>
    /// <param name="reportProgress">Optional progress callback: (current, total, message).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SolutionIngestionResult"/> describing per-project outcomes and the merged snapshot.</returns>
    Task<SolutionIngestionResult> IngestAsync(
        string slnPath,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken cancellationToken);
}
