using DocAgent.McpServer.Config;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Security;

/// <summary>
/// Enforces a default-deny path allowlist using glob pattern matching.
/// Allow and deny patterns are supported; deny patterns take precedence.
/// When no allow patterns are configured, only the server's working directory is accessible.
/// </summary>
public sealed class PathAllowlist
{
    private readonly IReadOnlyList<string> _allowPatterns;
    private readonly IReadOnlyList<string> _denyPatterns;
    private readonly string? _artifactsDir;

    /// <summary>True when explicit allow patterns are configured (from config or env var).</summary>
    internal bool IsConfigured => _allowPatterns.Count > 0;

    public PathAllowlist(IOptions<DocAgentServerOptions> options)
    {
        var opts = options.Value;

        // Merge config allow patterns with env var overrides, expanding env vars and ~ in each
        var allowList = new List<string>(PathExpander.ExpandAll(opts.AllowedPaths));
        var envPaths = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
        if (!string.IsNullOrWhiteSpace(envPaths))
        {
            var rawEntries = envPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allowList.AddRange(PathExpander.ExpandAll(rawEntries));
        }

        // Auto-include the artifacts directory — it's internal server storage, not user-facing.
        // Without this, tools like explain_solution fail when the artifacts dir is outside the allowed paths.
        var artifactsRaw = !string.IsNullOrWhiteSpace(opts.ArtifactsDir)
            ? opts.ArtifactsDir
            : Environment.GetEnvironmentVariable("DOCAGENT_ARTIFACTS_DIR");
        _artifactsDir = PathExpander.Expand(artifactsRaw);

        _allowPatterns = allowList.AsReadOnly();
        _denyPatterns = opts.DeniedPaths.ToList().AsReadOnly();
    }

    /// <summary>
    /// Returns true if the given absolute path is permitted by the allowlist.
    /// Deny patterns take precedence over allow patterns.
    /// </summary>
    public bool IsAllowed(string absolutePath)
    {
        // Always normalize to prevent path traversal attacks
        var normalized = Path.GetFullPath(absolutePath);

        // Deny patterns take precedence — check first
        if (MatchesAny(normalized, _denyPatterns))
            return false;

        // The artifacts directory and its children are always allowed (internal server storage).
        if (_artifactsDir is not null &&
            normalized.StartsWith(_artifactsDir, StringComparison.OrdinalIgnoreCase))
            return true;

        // Default when unconfigured: allow cwd only
        if (!IsConfigured)
            return normalized.StartsWith(Directory.GetCurrentDirectory(), StringComparison.OrdinalIgnoreCase);

        return MatchesAny(normalized, _allowPatterns);
    }

    private static bool MatchesAny(string path, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
            return false;

        // FileSystemGlobbing's Matcher requires relative paths.
        // Strip the path root (e.g. "C:\") from both the file path and each pattern
        // so that absolute glob patterns work correctly on all platforms.
        var pathRoot = Path.GetPathRoot(path) ?? string.Empty;
        var relativePath = path.Substring(pathRoot.Length);

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            // Strip the same root prefix from the pattern if it is rooted
            var patternRoot = Path.GetPathRoot(pattern) ?? string.Empty;
            var relativePattern = patternRoot.Length > 0
                ? pattern.Substring(patternRoot.Length)
                : pattern;
            matcher.AddInclude(relativePattern);
        }

        // Match using the path root as the directory base
        return matcher.Match(pathRoot, relativePath).HasMatches;
    }
}
