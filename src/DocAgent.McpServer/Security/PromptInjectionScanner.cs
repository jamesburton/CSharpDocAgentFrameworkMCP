using System.Text.RegularExpressions;

namespace DocAgent.McpServer.Security;

/// <summary>
/// Scans text content for known prompt injection patterns and sanitizes them.
/// Used to protect against malicious doc comment content that might influence agent behavior.
/// </summary>
public static class PromptInjectionScanner
{
    private static readonly Regex[] s_patterns =
    [
        new(@"ignore\s+previous\s+instructions?", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"you\s+are\s+now\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bsystem\s+prompt\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"forget\s+everything", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bact\s+as\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bdisregard\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Scans the given content for prompt injection patterns.
    /// Matched patterns are wrapped in [SUSPICIOUS: ...] markers.
    /// </summary>
    /// <param name="content">Content to scan. Null returns (empty string, false).</param>
    /// <returns>A tuple of (sanitized content, warning flag).</returns>
    public static (string Sanitized, bool Warning) Scan(string? content)
    {
        if (content is null)
            return (string.Empty, false);

        var result = content;
        var hasWarning = false;

        foreach (var pattern in s_patterns)
        {
            if (pattern.IsMatch(result))
            {
                hasWarning = true;
                result = pattern.Replace(result, m => $"[SUSPICIOUS: {m.Value}]");
            }
        }

        return (result, hasWarning);
    }
}
