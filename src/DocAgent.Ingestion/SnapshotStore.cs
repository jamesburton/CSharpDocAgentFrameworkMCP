using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using DocAgent.Core;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Ingestion;

/// <summary>
/// Persists and retrieves SymbolGraphSnapshot artifacts to/from the filesystem.
/// Snapshots are stored as MessagePack files named by content hash.
/// A manifest.json index tracks all stored snapshots.
/// </summary>
public sealed class SnapshotStore
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        ContractlessStandardResolver.Options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _artifactsDir;

    /// <summary>The directory where snapshots and manifests are persisted.</summary>
    public string ArtifactsDir => _artifactsDir;

    public SnapshotStore(string artifactsDir)
    {
        _artifactsDir = artifactsDir;
    }

    /// <summary>
    /// Serialize snapshot, compute content hash, write to artifacts/{hash}.msgpack, update manifest.
    /// Returns the snapshot with ContentHash set.
    /// </summary>
    public async Task<SymbolGraphSnapshot> SaveAsync(
        SymbolGraphSnapshot snapshot,
        string? gitCommitSha = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_artifactsDir);

        // 1. Serialize with ContentHash=null to get stable bytes for hashing
        var snapshotForHashing = snapshot with { ContentHash = null };
        var bytesForHashing = MessagePackSerializer.Serialize(snapshotForHashing, SerializerOptions, ct);

        // 2. Compute XxHash128 over the stable bytes
        var hashBytes = XxHash128.Hash(bytesForHashing);
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // 3. Create snapshot with ContentHash set
        var finalSnapshot = snapshot with { ContentHash = hashHex };

        // 4. Re-serialize with ContentHash set — this is what gets written to disk
        var finalBytes = MessagePackSerializer.Serialize(finalSnapshot, SerializerOptions, ct);

        // 5. Write to {artifactsDir}/{hash}.msgpack
        var filePath = Path.Combine(_artifactsDir, $"{hashHex}.msgpack");
        await File.WriteAllBytesAsync(filePath, finalBytes, ct).ConfigureAwait(false);

        // 6. Update manifest.json
        await UpdateManifestAsync(hashHex, finalSnapshot, gitCommitSha, ct).ConfigureAwait(false);

        return finalSnapshot;
    }

    /// <summary>
    /// Load a snapshot by content hash.
    /// </summary>
    public async Task<SymbolGraphSnapshot?> LoadAsync(string contentHash, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_artifactsDir, $"{contentHash}.msgpack");
        if (!File.Exists(filePath))
            return null;

        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        return MessagePackSerializer.Deserialize<SymbolGraphSnapshot>(bytes, SerializerOptions, ct);
    }

    /// <summary>
    /// List all snapshots from manifest.
    /// </summary>
    public async Task<IReadOnlyList<SnapshotManifestEntry>> ListAsync(CancellationToken ct = default)
    {
        var manifestPath = Path.Combine(_artifactsDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return Array.Empty<SnapshotManifestEntry>();

        var json = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions);
        return manifest?.Snapshots ?? (IReadOnlyList<SnapshotManifestEntry>)Array.Empty<SnapshotManifestEntry>();
    }

    private async Task UpdateManifestAsync(
        string hashHex,
        SymbolGraphSnapshot snapshot,
        string? gitCommitSha,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(_artifactsDir, "manifest.json");
        var tempPath = manifestPath + ".tmp";

        ManifestFile manifest;
        if (File.Exists(manifestPath))
        {
            var existingJson = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            manifest = JsonSerializer.Deserialize<ManifestFile>(existingJson, JsonOptions) ?? new ManifestFile();
        }
        else
        {
            manifest = new ManifestFile();
        }

        var entry = new SnapshotManifestEntry(
            ContentHash: hashHex,
            ProjectName: snapshot.ProjectName,
            GitCommitSha: gitCommitSha,
            IngestedAt: snapshot.CreatedAt,
            SchemaVersion: snapshot.SchemaVersion,
            NodeCount: snapshot.Nodes.Count,
            EdgeCount: snapshot.Edges.Count);

        // Replace existing entry with same hash or add new
        var snapshots = manifest.Snapshots.ToList();
        var existing = snapshots.FindIndex(s => s.ContentHash == hashHex);
        if (existing >= 0)
            snapshots[existing] = entry;
        else
            snapshots.Add(entry);

        manifest = manifest with { Snapshots = snapshots };

        var updatedJson = JsonSerializer.Serialize(manifest, JsonOptions);

        // Atomic write: write to temp file, rename
        await File.WriteAllTextAsync(tempPath, updatedJson, Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(tempPath, manifestPath, overwrite: true);
    }

    // Internal manifest file model
    private sealed record ManifestFile
    {
        public string Version { get; init; } = "1.0";
        public List<SnapshotManifestEntry> Snapshots { get; init; } = new();
    }
}

public sealed record SnapshotManifestEntry(
    string ContentHash,
    string ProjectName,
    string? GitCommitSha,
    DateTimeOffset IngestedAt,
    string SchemaVersion,
    int NodeCount,
    int EdgeCount);
