namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Shared helper for determining whether a source file should be excluded
/// from ingestion as a test file.
/// </summary>
internal static class TestFileFilter
{
    /// <summary>Default file name suffixes (no extension) treated as test files.</summary>
    internal static readonly string[] DefaultTestSuffixes =
        ["Test", "Tests", "Fixture", "Spec", "Specs", "Steps"];

    /// <summary>
    /// Returns true if <paramref name="filePath"/> should be skipped during ingestion.
    /// Files whose base name starts with "Base" (case-insensitive) are always included.
    /// </summary>
    internal static bool ShouldSkipSourceFile(string filePath, string[] testSuffixes)
    {
        var name = Path.GetFileNameWithoutExtension(filePath.AsSpan());
        if (name.StartsWith("Base".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var suffix in testSuffixes)
        {
            if (name.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
