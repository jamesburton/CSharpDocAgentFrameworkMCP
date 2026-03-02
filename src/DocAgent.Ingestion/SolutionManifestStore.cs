using System.Security.Cryptography;
using System.Text;

namespace DocAgent.Ingestion;

/// <summary>
/// Manages per-project file hash manifests keyed by solution-relative path.
/// Uses solution-relative paths to avoid collisions when multiple projects
/// share the same name but live in different directories.
/// </summary>
public static class SolutionManifestStore
{
    /// <summary>
    /// Computes a safe flat filename from the solution-relative path of a project file.
    /// Replaces directory separators with "__" to produce a collision-free flat name.
    /// </summary>
    public static string ManifestFileName(string slnPath, string projectFilePath)
    {
        var slnDir = Path.GetDirectoryName(slnPath) ?? string.Empty;
        var relativePath = Path.GetRelativePath(slnDir, projectFilePath);

        // Normalize to forward slashes first, then replace both separators
        var safe = relativePath
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        // Replace single underscores from separator replacement with double underscores
        // But we need to replace separators with __ directly
        safe = relativePath;
        safe = safe.Replace("\\", "__").Replace("/", "__");

        return safe + ".manifest.json";
    }

    /// <summary>
    /// Returns the manifest storage directory, creating it if necessary.
    /// </summary>
    public static string ManifestDirectory(string artifactsDir)
    {
        var dir = Path.Combine(artifactsDir, "project-manifests");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Computes a project manifest including all .cs files plus synthetic entries
    /// for project references and target framework.
    /// </summary>
    public static async Task<FileHashManifest> ComputeProjectManifestAsync(
        string projectFilePath,
        IReadOnlyList<string> projectReferencePaths,
        string? chosenTfm,
        CancellationToken ct = default)
    {
        var projectDir = Path.GetDirectoryName(projectFilePath) ?? string.Empty;

        // Enumerate all .cs files sorted by ordinal
        var csFiles = Directory.Exists(projectDir)
            ? Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList()
            : new List<string>();

        var manifest = await FileHasher.ComputeManifestAsync(csFiles, ct).ConfigureAwait(false);

        // Inject synthetic hash entries
        var hashes = new Dictionary<string, string>(manifest.FileHashes);

        // __project_refs__ = SHA-256 of sorted comma-joined project reference paths
        var sortedRefs = projectReferencePaths.OrderBy(r => r, StringComparer.Ordinal);
        var refsString = string.Join(",", sortedRefs);
        var refsHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(refsString))).ToLowerInvariant();
        hashes["__project_refs__"] = refsHash;

        // __tfm__ = SHA-256 of TFM string (or empty string if null)
        var tfmString = chosenTfm ?? string.Empty;
        var tfmHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(tfmString))).ToLowerInvariant();
        hashes["__tfm__"] = tfmHash;

        return new FileHashManifest(hashes, manifest.CreatedAt, manifest.SchemaVersion);
    }

    /// <summary>
    /// Saves a project manifest to the standard location.
    /// </summary>
    public static async Task SaveAsync(
        string artifactsDir,
        string slnPath,
        string projectFilePath,
        FileHashManifest manifest,
        CancellationToken ct = default)
    {
        var dir = ManifestDirectory(artifactsDir);
        var fileName = ManifestFileName(slnPath, projectFilePath);
        var fullPath = Path.Combine(dir, fileName);
        await FileHasher.SaveAsync(manifest, fullPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a project manifest from the standard location, or null if not found.
    /// </summary>
    public static async Task<FileHashManifest?> LoadAsync(
        string artifactsDir,
        string slnPath,
        string projectFilePath,
        CancellationToken ct = default)
    {
        var dir = ManifestDirectory(artifactsDir);
        var fileName = ManifestFileName(slnPath, projectFilePath);
        var fullPath = Path.Combine(dir, fileName);
        return await FileHasher.LoadAsync(fullPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes orphaned manifest files that no longer correspond to projects in the solution.
    /// Handles the "project moved or removed" scenario.
    /// </summary>
    public static void CleanOrphanedManifests(
        string artifactsDir,
        IReadOnlySet<string> currentManifestFileNames)
    {
        var dir = Path.Combine(artifactsDir, "project-manifests");
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.manifest.json"))
        {
            var fileName = Path.GetFileName(file);
            if (!currentManifestFileNames.Contains(fileName))
            {
                File.Delete(file);
            }
        }
    }
}
