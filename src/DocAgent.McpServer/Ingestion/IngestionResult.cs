namespace DocAgent.McpServer.Ingestion;

/// <summary>Result returned by <see cref="IIngestionService.IngestAsync"/> after pipeline completion.</summary>
public sealed record IngestionResult(
    string SnapshotId,
    int SymbolCount,
    int ProjectCount,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings,
    string? IndexError = null);
