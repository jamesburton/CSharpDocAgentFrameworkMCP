using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DocAgent.McpServer.Config;

/// <summary>
/// Cross-platform path expansion: resolves environment variables (<c>%VAR%</c> on Windows,
/// <c>$VAR</c> / <c>${VAR}</c> on *nix), tilde (<c>~</c>) home-directory shorthand,
/// and relative paths against an optional base directory.
/// </summary>
public static partial class PathExpander
{
    // %VARIABLE_NAME%  (Windows-style)
    [GeneratedRegex(@"%([^%]+)%")]
    private static partial Regex WindowsEnvVarPattern();

    // ${VARIABLE_NAME} or $VARIABLE_NAME  (Unix-style)
    [GeneratedRegex(@"\$\{([^}]+)\}|\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex UnixEnvVarPattern();

    /// <summary>
    /// Expands environment variables, tilde, and relative segments in <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The raw path string (may contain <c>%VAR%</c>, <c>$VAR</c>, <c>${VAR}</c>, or <c>~</c>).</param>
    /// <param name="baseDir">
    /// Base directory for resolving relative paths. When <see langword="null"/>, the current
    /// working directory is used.
    /// </param>
    /// <returns>A fully-resolved absolute path, or <see langword="null"/> if <paramref name="path"/> is null or whitespace.</returns>
    public static string? Expand(string? path, string? baseDir = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Normalize backslashes to forward slashes on non-Windows platforms
        // so that Windows-style paths like ~\foo\bar or ..\dir\file.cs resolve correctly on Linux/macOS
        var expanded = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? path.Replace('\\', '/')
            : path;

        // 1. Expand tilde (must come before env-var expansion so ~/$HOME doesn't double-expand)
        expanded = ExpandTilde(expanded);

        // 2. Expand environment variables (both styles on all platforms for portability)
        expanded = ExpandEnvironmentVariables(expanded);

        // 3. Resolve to absolute path
        if (!Path.IsPathRooted(expanded))
        {
            var effectiveBase = baseDir ?? Directory.GetCurrentDirectory();
            expanded = Path.Combine(effectiveBase, expanded);
        }

        // 4. Normalize (resolve .., ., trailing separators)
        expanded = Path.GetFullPath(expanded);

        return expanded;
    }

    /// <summary>
    /// Expands each path in <paramref name="paths"/> via <see cref="Expand"/>, skipping null/empty entries.
    /// </summary>
    public static string[] ExpandAll(IEnumerable<string>? paths, string? baseDir = null)
    {
        if (paths is null)
            return [];

        var result = new List<string>();
        foreach (var p in paths)
        {
            var expanded = Expand(p, baseDir);
            if (expanded is not null)
                result.Add(expanded);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Expands environment variables and tilde in a glob pattern <b>without</b> resolving
    /// relative paths. Use this for allowlist/denylist glob patterns like <c>%USERPROFILE%\projects\**</c>
    /// where the pattern may be relative (e.g. <c>**</c>) and should not be anchored to cwd.
    /// </summary>
    public static string? ExpandGlob(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var expanded = ExpandTilde(pattern);
        expanded = ExpandEnvironmentVariables(expanded);
        return expanded;
    }

    /// <summary>
    /// Expands each glob pattern via <see cref="ExpandGlob"/>, skipping null/empty entries.
    /// </summary>
    public static string[] ExpandAllGlobs(IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return [];

        var result = new List<string>();
        foreach (var p in patterns)
        {
            var expanded = ExpandGlob(p);
            if (expanded is not null)
                result.Add(expanded);
        }
        return result.ToArray();
    }

    // ── Internals ────────────────────────────────────────────────────────

    private static string ExpandTilde(string path)
    {
        if (!path.StartsWith('~'))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return path; // can't expand — leave as-is

        // ~/foo  or  ~\foo  or just ~
        if (path.Length == 1)
            return home;

        if (path[1] == '/' || path[1] == '\\')
            return Path.Combine(home, path[2..]);

        // ~someuser style is not supported — return as-is
        return path;
    }

    private static string ExpandEnvironmentVariables(string path)
    {
        // Windows-style %VAR%
        var result = WindowsEnvVarPattern().Replace(path, match =>
        {
            var varName = match.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(varName);
            return value ?? match.Value; // leave token intact if unresolvable
        });

        // Unix-style $VAR or ${VAR}
        result = UnixEnvVarPattern().Replace(result, match =>
        {
            // Group 1 = ${VAR} capture, Group 2 = $VAR capture
            var varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var value = Environment.GetEnvironmentVariable(varName);
            return value ?? match.Value; // leave token intact if unresolvable
        });

        return result;
    }
}
