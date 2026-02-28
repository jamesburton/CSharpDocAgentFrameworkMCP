namespace DocAgent.McpServer.Ingestion;

/// <summary>Orchestrates the full ingestion pipeline: discover → parse → snapshot → index.</summary>
public interface IIngestionService
{
    /// <summary>
    /// Runs the ingestion pipeline for the given solution, project, or directory path.
    /// Concurrent calls for different paths run in parallel; same-path calls are serialized.
    /// </summary>
    /// <param name="path">Absolute path to a .sln, .slnx, .csproj, or directory.</param>
    /// <param name="includeGlob">Optional glob pattern to include project files (e.g. <c>**/*.csproj</c>).</param>
    /// <param name="excludeGlob">Optional glob pattern to exclude project files (e.g. <c>**/Tests/**</c>).</param>
    /// <param name="forceReindex">When true, re-indexes even if snapshot is already current.</param>
    /// <param name="reportProgress">Optional async callback: (current, total, stageName). Only invoked when a progress token is present.</param>
    /// <param name="cancellationToken">Cancellation token (also used to enforce the configured timeout).</param>
    Task<IngestionResult> IngestAsync(
        string path,
        string? includeGlob,
        string? excludeGlob,
        bool forceReindex,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken cancellationToken);
}
