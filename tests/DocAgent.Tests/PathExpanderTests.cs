using DocAgent.McpServer.Config;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests;

[Collection("EnvVarMutating")]
public sealed class PathExpanderTests
{
    // ── Null / empty input ────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Expand_NullOrWhitespace_ReturnsNull(string? input)
    {
        PathExpander.Expand(input).Should().BeNull();
    }

    // ── Tilde expansion ──────────────────────────────────────────────

    [Fact]
    public void Expand_TildeOnly_ReturnsUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        PathExpander.Expand("~").Should().Be(home);
    }

    [Fact]
    public void Expand_TildeSlashPath_ExpandsToHomeSubdir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathExpander.Expand("~/some/path");
        result.Should().Be(Path.GetFullPath(Path.Combine(home, "some", "path")));
    }

    [Fact]
    public void Expand_TildeBackslashPath_ExpandsToHomeSubdir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathExpander.Expand(@"~\some\path");
        result.Should().Be(Path.GetFullPath(Path.Combine(home, "some", "path")));
    }

    // ── Windows-style %VAR% expansion ────────────────────────────────

    [Fact]
    public void Expand_WindowsPercentVar_ExpandsVariable()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", "/resolved/dir");
            var result = PathExpander.Expand("%DOCAGENT_TEST_VAR%/sub");
            result.Should().Be(Path.GetFullPath("/resolved/dir/sub"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    [Fact]
    public void Expand_UserProfile_ExpandsOnWindows()
    {
        // %USERPROFILE% is set on Windows; on *nix this test still validates the pattern
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (userProfile is null)
            return; // skip on non-Windows

        var result = PathExpander.Expand(@"%USERPROFILE%\.docagent\artifacts");
        result.Should().Be(Path.GetFullPath(Path.Combine(userProfile, ".docagent", "artifacts")));
    }

    [Fact]
    public void Expand_UnknownPercentVar_LeavesTokenIntact()
    {
        // Use a variable name that won't exist
        var result = PathExpander.Expand("%DOCAGENT_NONEXISTENT_12345%/foo");
        result.Should().Contain("DOCAGENT_NONEXISTENT_12345");
    }

    // ── Unix-style $VAR and ${VAR} expansion ─────────────────────────

    [Fact]
    public void Expand_DollarVar_ExpandsVariable()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", "/unix/dir");
            var result = PathExpander.Expand("$DOCAGENT_TEST_VAR/sub");
            result.Should().Be(Path.GetFullPath("/unix/dir/sub"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    [Fact]
    public void Expand_DollarBraceVar_ExpandsVariable()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", "/braced/dir");
            var result = PathExpander.Expand("${DOCAGENT_TEST_VAR}/sub");
            result.Should().Be(Path.GetFullPath("/braced/dir/sub"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    // ── Relative path resolution ─────────────────────────────────────

    [Fact]
    public void Expand_RelativePath_ResolvesAgainstBaseDir()
    {
        var baseDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = PathExpander.Expand("sub/dir", baseDir: baseDir);
        result.Should().Be(Path.GetFullPath(Path.Combine(baseDir, "sub", "dir")));
    }

    [Fact]
    public void Expand_DotDotRelativePath_ResolvesCorrectly()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "project", "src");
        var result = PathExpander.Expand(@"..\OtherProject\file.cs", baseDir: baseDir);
        var expected = Path.GetFullPath(Path.Combine(baseDir, "..", "OtherProject", "file.cs"));
        result.Should().Be(expected);
    }

    [Fact]
    public void Expand_RelativePath_DefaultsToCurrentDirectory()
    {
        var result = PathExpander.Expand("relative/path");
        var expected = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "relative", "path"));
        result.Should().Be(expected);
    }

    // ── Absolute paths pass through ──────────────────────────────────

    [Fact]
    public void Expand_AbsolutePath_RemainsAbsolute()
    {
        var abs = OperatingSystem.IsWindows() ? @"C:\absolute\path" : "/absolute/path";
        var result = PathExpander.Expand(abs);
        result.Should().Be(Path.GetFullPath(abs));
    }

    // ── ExpandAll ────────────────────────────────────────────────────

    [Fact]
    public void ExpandAll_NullInput_ReturnsEmpty()
    {
        PathExpander.ExpandAll(null).Should().BeEmpty();
    }

    [Fact]
    public void ExpandAll_MixedPaths_ExpandsAllNonEmpty()
    {
        var baseDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var result = PathExpander.ExpandAll(["~/foo", "relative", "", "  "], baseDir: baseDir);
        result.Should().HaveCount(2);
    }

    // ── Combined: env var + relative ─────────────────────────────────

    [Fact]
    public void Expand_EnvVarProducingRelativePath_ResolvesAgainstBaseDir()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", "relative");
            var baseDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            var result = PathExpander.Expand("$DOCAGENT_TEST_VAR/sub", baseDir: baseDir);
            result.Should().Be(Path.GetFullPath(Path.Combine(baseDir, "relative", "sub")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    // ── ExpandGlob (env var + tilde only, no relative path resolution) ──

    [Fact]
    public void ExpandGlob_NullOrWhitespace_ReturnsNull()
    {
        PathExpander.ExpandGlob(null).Should().BeNull();
        PathExpander.ExpandGlob("").Should().BeNull();
        PathExpander.ExpandGlob("  ").Should().BeNull();
    }

    [Fact]
    public void ExpandGlob_DoubleStarGlob_NotResolvedToCwd()
    {
        var result = PathExpander.ExpandGlob("**");
        result.Should().Be("**", "pure glob patterns must not be resolved to cwd");
    }

    [Fact]
    public void ExpandGlob_TildeGlob_ExpandsHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathExpander.ExpandGlob("~/projects/**");
        // Tilde is replaced with home dir; the glob suffix is preserved as-is
        result.Should().StartWith(home);
        result.Should().EndWith("projects/**");
    }

    [Fact]
    public void ExpandGlob_EnvVarGlob_ExpandsVarOnly()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", "/some/dir");
            var result = PathExpander.ExpandGlob("$DOCAGENT_TEST_VAR/**");
            result.Should().Be("/some/dir/**");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    [Fact]
    public void ExpandGlob_WindowsEnvVarGlob_ExpandsVarOnly()
    {
        var prev = Environment.GetEnvironmentVariable("DOCAGENT_TEST_VAR");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", @"C:\Users\test");
            var result = PathExpander.ExpandGlob(@"%DOCAGENT_TEST_VAR%\projects\**");
            result.Should().Be(@"C:\Users\test\projects\**");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_TEST_VAR", prev);
        }
    }

    [Fact]
    public void ExpandAllGlobs_NullInput_ReturnsEmpty()
    {
        PathExpander.ExpandAllGlobs(null).Should().BeEmpty();
    }

    [Fact]
    public void ExpandAllGlobs_PreservesRelativeGlobs()
    {
        var result = PathExpander.ExpandAllGlobs(["**", "src/**/*.cs", ""]);
        result.Should().HaveCount(2);
        result[0].Should().Be("**");
        result[1].Should().Be("src/**/*.cs");
    }
}
