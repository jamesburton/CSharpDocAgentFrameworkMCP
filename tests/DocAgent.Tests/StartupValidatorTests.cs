using DocAgent.McpServer.Config;
using DocAgent.McpServer.Validation;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests;

public sealed class StartupValidatorTests
{
    private static DocAgentServerOptions CreateOptions(
        string[]? allowedPaths = null,
        string? artifactsDir = null)
    {
        return new DocAgentServerOptions
        {
            AllowedPaths = allowedPaths ?? [],
            ArtifactsDir = artifactsDir,
        };
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        var opts = CreateOptions(
            allowedPaths: [Path.Combine(Path.GetTempPath(), "**")],
            artifactsDir: Path.GetTempPath());

        var result = StartupValidator.Validate(opts);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyAllowedPaths_ReturnsWarning()
    {
        var prevValue = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS", null);
            var opts = CreateOptions(
                allowedPaths: [],
                artifactsDir: Path.GetTempPath());

            var result = StartupValidator.Validate(opts);

            result.IsValid.Should().BeTrue("warnings are non-fatal");
            result.Warnings.Should().ContainSingle()
                .Which.Should().Contain("AllowedPaths");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS", prevValue);
        }
    }

    [Fact]
    public void Validate_NullArtifactsDir_ReturnsError()
    {
        var opts = CreateOptions(
            allowedPaths: [Path.Combine(Path.GetTempPath(), "**")],
            artifactsDir: null);

        var result = StartupValidator.Validate(opts);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("ArtifactsDir");
    }

    [Fact]
    public void Validate_EmptyStringArtifactsDir_ReturnsError()
    {
        var opts = CreateOptions(
            allowedPaths: [Path.Combine(Path.GetTempPath(), "**")],
            artifactsDir: "");

        var result = StartupValidator.Validate(opts);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("ArtifactsDir");
    }

    [Fact]
    public void Validate_NonWritableArtifactsDir_ReturnsError()
    {
        // Use a path with an invalid drive letter on Windows
        var invalidPath = "Z:\\nonexistent\\deeply\\nested\\path";

        var opts = CreateOptions(
            allowedPaths: [Path.Combine(Path.GetTempPath(), "**")],
            artifactsDir: invalidPath);

        var result = StartupValidator.Validate(opts);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("not writable");
    }

    [Fact]
    public void Validate_AllowedPathsViaEnvVar_NoWarning()
    {
        var prevValue = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS");
        try
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS",
                Path.Combine(Path.GetTempPath(), "**"));
            var opts = CreateOptions(
                allowedPaths: [],
                artifactsDir: Path.GetTempPath());

            var result = StartupValidator.Validate(opts);

            result.IsValid.Should().BeTrue();
            result.Warnings.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS", prevValue);
        }
    }
}
