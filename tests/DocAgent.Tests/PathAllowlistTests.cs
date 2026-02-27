using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

public sealed class PathAllowlistTests
{
    private static PathAllowlist Create(string[]? allow = null, string[]? deny = null)
    {
        var opts = new DocAgentServerOptions
        {
            AllowedPaths = allow ?? [],
            DeniedPaths = deny ?? [],
        };
        return new PathAllowlist(Options.Create(opts));
    }

    // ── Default / unconfigured behaviour ───────────────────────────────

    [Fact]
    public void IsAllowed_DefaultUnconfigured_AllowsCwd()
    {
        var sut = Create();
        var pathUnderCwd = Path.Combine(Directory.GetCurrentDirectory(), "somefile.cs");

        sut.IsAllowed(pathUnderCwd).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_DefaultUnconfigured_DeniesOutsideCwd()
    {
        var sut = Create();
        // Use a temp path that is guaranteed to not be under cwd
        var outsidePath = Path.GetTempPath() + "outsidefile.txt";

        sut.IsAllowed(outsidePath).Should().BeFalse();
    }

    // ── Allow pattern matching ─────────────────────────────────────────

    [Fact]
    public void IsAllowed_AllowPattern_MatchesGlob()
    {
        var projectsDir = Path.Combine(Path.GetTempPath(), "projects");
        var sut = Create(allow: [Path.Combine(projectsDir, "**")]);
        var testPath = Path.Combine(projectsDir, "myapp", "src", "file.cs");

        sut.IsAllowed(testPath).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_AllowPattern_DeniesUnmatched()
    {
        var projectsDir = Path.Combine(Path.GetTempPath(), "projects");
        var sut = Create(allow: [Path.Combine(projectsDir, "**")]);
        var outsidePath = Path.Combine(Path.GetTempPath(), "other", "file.cs");

        sut.IsAllowed(outsidePath).Should().BeFalse();
    }

    // ── Deny takes precedence ─────────────────────────────────────────

    [Fact]
    public void IsAllowed_DenyTakesPrecedence()
    {
        var projectsDir = Path.Combine(Path.GetTempPath(), "projects");
        var secretsDir = Path.Combine(projectsDir, "secrets");
        var sut = Create(
            allow: [Path.Combine(projectsDir, "**")],
            deny: [Path.Combine(secretsDir, "**")]);
        var secretPath = Path.Combine(secretsDir, "key.pem");

        sut.IsAllowed(secretPath).Should().BeFalse();
    }

    // ── Path traversal normalization ──────────────────────────────────

    [Fact]
    public void IsAllowed_NormalizesTraversal()
    {
        // Create a path that attempts traversal outside the allow pattern
        var projectsDir = Path.Combine(Path.GetTempPath(), "projects");
        var sut = Create(allow: [Path.Combine(projectsDir, "**")]);

        // Build a traversal that goes inside allowed then escapes
        var traversalPath = Path.Combine(projectsDir, "sub", "..", "..", "etc", "passwd");

        // After normalization this resolves outside projectsDir
        sut.IsAllowed(traversalPath).Should().BeFalse();
    }

    // ── Env var override ─────────────────────────────────────────────

    [Fact]
    public void IsAllowed_EnvVarExtends()
    {
        var extraDir = Path.Combine(Path.GetTempPath(), "extra-allowed");
        var envVarValue = Path.Combine(extraDir, "**");

        var prevValue = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS", envVarValue);
            var sut = Create(); // no config allow patterns — env var should kick in
            var testPath = Path.Combine(extraDir, "file.txt");

            sut.IsAllowed(testPath).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS", prevValue);
        }
    }

    // ── Case insensitivity ────────────────────────────────────────────

    [Fact]
    public void IsAllowed_CaseInsensitive()
    {
        var lowerDir = Path.Combine(Path.GetTempPath(), "casetest");
        // Pattern uses lowercase
        var sut = Create(allow: [Path.Combine(lowerDir.ToLowerInvariant(), "**")]);
        // Path uses different casing
        var mixedPath = Path.Combine(lowerDir.ToUpperInvariant(), "File.cs");

        // FileSystemGlobbing uses OrdinalIgnoreCase → should match
        sut.IsAllowed(mixedPath).Should().BeTrue();
    }

    // ── Deny on unconfigured list still denies ─────────────────────────

    [Fact]
    public void IsAllowed_DenyOnlyPattern_DeniesMatchedPathUnderCwd()
    {
        // No allow patterns, so cwd is default; deny pattern inside cwd should still deny
        var cwdSub = Path.Combine(Directory.GetCurrentDirectory(), "secrets", "apikey.txt");
        var sut = Create(deny: [Path.Combine(Directory.GetCurrentDirectory(), "secrets", "**")]);

        sut.IsAllowed(cwdSub).Should().BeFalse();
    }
}
