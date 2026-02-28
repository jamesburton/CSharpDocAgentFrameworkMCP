namespace DocAgent.Core;

public enum FileChangeKind
{
    Added,
    Modified,
    Removed
}

public sealed record FileChangeRecord(
    string FilePath,
    FileChangeKind ChangeKind,
    IReadOnlyList<string> AffectedSymbolIds);

public sealed record IngestionMetadata(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool WasFullReingestion,
    IReadOnlyList<FileChangeRecord> FileChanges);
