namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Discovers tool manifests and MSBuild script files in a solution directory tree.
/// </summary>
public static class ToolScriptDiscovery
{
    public sealed record DiscoveredFiles(
        IReadOnlyList<string> DotnetToolsManifests,
        IReadOnlyList<string> MSBuildFiles);

    /// <summary>
    /// Scans the solution directory (and ancestors for dotnet-tools.json) for
    /// tool manifests and MSBuild .targets/.props files.
    /// </summary>
    public static DiscoveredFiles DiscoverToolsAndScripts(string solutionDir)
    {
        var toolManifests = new List<string>();
        var msbuildFiles = new List<string>();

        if (!Directory.Exists(solutionDir))
            return new DiscoveredFiles(toolManifests, msbuildFiles);

        // Search for .config/dotnet-tools.json in the solution dir and parents
        var dir = solutionDir;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, ".config", "dotnet-tools.json");
            if (File.Exists(candidate))
            {
                toolManifests.Add(Path.GetFullPath(candidate));
                break; // Only closest one matters
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Discover .targets and .props files in the solution directory tree
        try
        {
            foreach (var file in Directory.EnumerateFiles(solutionDir, "*.targets", SearchOption.AllDirectories))
            {
                msbuildFiles.Add(Path.GetFullPath(file));
            }
            foreach (var file in Directory.EnumerateFiles(solutionDir, "*.props", SearchOption.AllDirectories))
            {
                msbuildFiles.Add(Path.GetFullPath(file));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Swallow permission errors during discovery
        }

        return new DiscoveredFiles(toolManifests, msbuildFiles);
    }
}
