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

    /// <summary>True when explicit allow patterns are configured (from config or env var).</summary>
    internal bool IsConfigured => _allowPatterns.Count > 0;

    public PathAllowlist(IOptions<DocAgentServerOptions> options)
    {
        var opts = options.Value;

        // Merge config allow patterns with env var overrides
        var allowList = new List<string>(opts.AllowedPaths);
        var envPaths = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
        if (!string.IsNullOrWhiteSpace(envPaths))
        {
            allowList.AddRange(
                envPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

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

        // Default when unconfigured: allow cwd only
        if (!IsConfigured)
            return normalized.StartsWith(Directory.GetCurrentDirectory(), StringComparison.OrdinalIgnoreCase);

        return MatchesAny(normalized, _allowPatterns);
    }

    private static bool MatchesAny(string path, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
            return false;

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
            matcher.AddInclude(pattern);

        // Matcher works with relative paths from a root; use the path as-is for absolute matching
        return matcher.Match(path).HasMatches;
    }
}
