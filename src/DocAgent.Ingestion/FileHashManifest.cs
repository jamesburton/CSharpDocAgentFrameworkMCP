using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocAgent.Ingestion;

public sealed record FileHashManifest(
    IReadOnlyDictionary<string, string> FileHashes,
    DateTimeOffset CreatedAt,
    string SchemaVersion = "1.0");

public sealed record ManifestDiff(
    IReadOnlyList<string> AddedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> RemovedFiles)
{
    public bool HasChanges => AddedFiles.Count > 0 || ModifiedFiles.Count > 0 || RemovedFiles.Count > 0;

    public IReadOnlyList<string> ChangedFiles { get; } = AddedFiles.Concat(ModifiedFiles).ToList();
}

public static class FileHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<string> ComputeAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hashBytes);
    }

    public static async Task<FileHashManifest> ComputeManifestAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken ct = default)
    {
        var hashes = new Dictionary<string, string>(filePaths.Count);
        foreach (var path in filePaths)
        {
            hashes[path] = await ComputeAsync(path, ct).ConfigureAwait(false);
        }

        return new FileHashManifest(
            FileHashes: hashes,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    public static ManifestDiff Diff(FileHashManifest? previous, FileHashManifest current)
    {
        if (previous is null)
        {
            return new ManifestDiff(
                AddedFiles: current.FileHashes.Keys.ToList(),
                ModifiedFiles: [],
                RemovedFiles: []);
        }

        var added = new List<string>();
        var modified = new List<string>();
        var removed = new List<string>();

        foreach (var (path, hash) in current.FileHashes)
        {
            if (!previous.FileHashes.TryGetValue(path, out var prevHash))
                added.Add(path);
            else if (prevHash != hash)
                modified.Add(path);
        }

        foreach (var path in previous.FileHashes.Keys)
        {
            if (!current.FileHashes.ContainsKey(path))
                removed.Add(path);
        }

        return new ManifestDiff(added, modified, removed);
    }

    public static async Task SaveAsync(FileHashManifest manifest, string path, CancellationToken ct = default)
    {
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public static async Task<FileHashManifest?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<FileHashManifest>(json, JsonOptions);
    }
}
