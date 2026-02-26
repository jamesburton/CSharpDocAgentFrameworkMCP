using DocAgent.Core;
using Microsoft.CodeAnalysis.MSBuild;

namespace DocAgent.Ingestion;

/// <summary>
/// Discovers projects from a local .sln file, directory, or explicit .csproj paths
/// using Roslyn's MSBuildWorkspace.
/// </summary>
public sealed class LocalProjectSource : IProjectSource
{
    private static readonly string[] TestProjectSuffixes = [".Tests", ".Test", ".Specs"];

    private readonly bool _includeTestProjects;
    private readonly Action<string>? _logWarning;

    public LocalProjectSource(bool includeTestProjects = false, Action<string>? logWarning = null)
    {
        _includeTestProjects = includeTestProjects;
        _logWarning = logWarning;
    }

    public async Task<ProjectInventory> DiscoverAsync(ProjectLocator locator, CancellationToken ct)
    {
        var path = locator.PathOrUrl;

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return await DiscoverFromCsprojAsync(path, ct).ConfigureAwait(false);
        }

        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await DiscoverFromSolutionAsync(path, ct).ConfigureAwait(false);
        }

        if (Directory.Exists(path))
        {
            return await DiscoverFromDirectoryAsync(path, ct).ConfigureAwait(false);
        }

        throw new ArgumentException(
            $"Path does not exist or is not a recognised type (.sln/.csproj/directory): {path}",
            nameof(locator));
    }

    // ── Explicit csproj path(s) ──────────────────────────────────────────────

    private async Task<ProjectInventory> DiscoverFromCsprojAsync(string csprojPath, CancellationToken ct)
    {
        // Support semicolon-delimited list of csproj paths
        var paths = csprojPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var absProjects = paths
            .Select(p => Path.GetFullPath(p))
            .ToList();

        foreach (var p in absProjects)
        {
            if (!File.Exists(p))
                throw new FileNotFoundException($"Project file not found: {p}", p);
        }

        var rootPath = absProjects.Count == 1
            ? Path.GetDirectoryName(absProjects[0])!
            : FindCommonParent(absProjects.Select(Path.GetDirectoryName).OfType<string>().ToList());

        var xmlDocs = CollectXmlDocFiles(absProjects);

        return await Task.FromResult(new ProjectInventory(
            RootPath: rootPath,
            SolutionFiles: [],
            ProjectFiles: absProjects,
            XmlDocFiles: xmlDocs)).ConfigureAwait(false);
    }

    // ── Solution file ────────────────────────────────────────────────────────

    private async Task<ProjectInventory> DiscoverFromSolutionAsync(string slnPath, CancellationToken ct)
    {
        var absSln = Path.GetFullPath(slnPath);
        if (!File.Exists(absSln))
            throw new FileNotFoundException($"Solution file not found: {absSln}", absSln);

        var rootPath = Path.GetDirectoryName(absSln)!;
        var projectFiles = await OpenSolutionProjectsAsync(absSln, ct).ConfigureAwait(false);
        var xmlDocs = CollectXmlDocFiles(projectFiles);

        return new ProjectInventory(
            RootPath: rootPath,
            SolutionFiles: [absSln],
            ProjectFiles: projectFiles,
            XmlDocFiles: xmlDocs);
    }

    // ── Directory scan ───────────────────────────────────────────────────────

    private async Task<ProjectInventory> DiscoverFromDirectoryAsync(string dir, CancellationToken ct)
    {
        var absDir = Path.GetFullPath(dir);
        var slnFiles = Directory.GetFiles(absDir, "*.sln", SearchOption.TopDirectoryOnly);

        if (slnFiles.Length == 1)
        {
            return await DiscoverFromSolutionAsync(slnFiles[0], ct).ConfigureAwait(false);
        }

        if (slnFiles.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple .sln files found in '{absDir}'. Specify one explicitly:\n  " +
                string.Join("\n  ", slnFiles));
        }

        // No .sln — fall back to all .csproj files in the directory tree
        var csprojFiles = Directory.GetFiles(absDir, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToList();

        var filtered = _includeTestProjects ? csprojFiles : FilterTestProjects(csprojFiles);
        var xmlDocs = CollectXmlDocFiles(filtered);

        return new ProjectInventory(
            RootPath: absDir,
            SolutionFiles: [],
            ProjectFiles: filtered,
            XmlDocFiles: xmlDocs);
    }

    // ── MSBuildWorkspace helpers ─────────────────────────────────────────────

    private async Task<IReadOnlyList<string>> OpenSolutionProjectsAsync(string slnPath, CancellationToken ct)
    {
        using var workspace = MSBuildWorkspace.Create();

        workspace.WorkspaceFailed += (_, args) =>
            _logWarning?.Invoke($"MSBuildWorkspace [{args.Diagnostic.Kind}]: {args.Diagnostic.Message}");

        var solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: ct).ConfigureAwait(false);

        var projectFiles = solution.Projects
            .Select(p => p.FilePath)
            .OfType<string>()
            .Select(Path.GetFullPath)
            .ToList();

        return _includeTestProjects ? projectFiles : FilterTestProjects(projectFiles);
    }

    // ── Filtering helpers ────────────────────────────────────────────────────

    private static List<string> FilterTestProjects(IEnumerable<string> projectFiles)
    {
        return projectFiles
            .Where(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                return !TestProjectSuffixes.Any(suffix =>
                    name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    // ── XML doc file discovery ───────────────────────────────────────────────

    private static IReadOnlyList<string> CollectXmlDocFiles(IEnumerable<string> projectFiles)
    {
        var xmlDocs = new List<string>();

        foreach (var proj in projectFiles)
        {
            var projectDir = Path.GetDirectoryName(proj);
            if (projectDir is null) continue;

            var assemblyName = Path.GetFileNameWithoutExtension(proj);

            // Convention: look in common output dirs (bin/Debug|Release/**/assemblyName.xml)
            var candidates = Directory.GetFiles(projectDir, $"{assemblyName}.xml", SearchOption.AllDirectories);
            xmlDocs.AddRange(candidates.Select(Path.GetFullPath));
        }

        return xmlDocs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Path utilities ────────────────────────────────────────────────────────

    private static string FindCommonParent(IList<string> dirs)
    {
        if (dirs.Count == 0) return Directory.GetCurrentDirectory();
        if (dirs.Count == 1) return dirs[0];

        var parts = dirs[0].Split(Path.DirectorySeparatorChar);
        var depth = parts.Length;

        foreach (var dir in dirs.Skip(1))
        {
            var otherParts = dir.Split(Path.DirectorySeparatorChar);
            depth = Math.Min(depth, otherParts.Length);
            for (int i = 0; i < depth; i++)
            {
                if (!string.Equals(parts[i], otherParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    depth = i;
                    break;
                }
            }
        }

        return depth == 0
            ? Path.GetPathRoot(dirs[0]) ?? Directory.GetCurrentDirectory()
            : string.Join(Path.DirectorySeparatorChar, parts.Take(depth));
    }
}
