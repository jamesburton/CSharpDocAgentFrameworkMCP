using DocAgent.McpServer.Validation;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests;

public class NodeAvailabilityValidatorTests
{
    [Theory]
    [InlineData("v22.3.1", 22, 3, 1, true, true)]
    [InlineData("22.3.1", 22, 3, 1, true, true)]
    [InlineData("v18.0.0", 18, 0, 0, true, false)]
    [InlineData("v24.1.0", 24, 1, 0, true, true)]
    public void ParseNodeVersion_with_valid_version_returns_correct_result(
        string output, int major, int minor, int build, bool available, bool supported)
    {
        // Act
        var result = NodeAvailabilityValidator.ParseNodeVersion(output);

        // Assert
        result.IsAvailable.Should().Be(available);
        result.IsSupported.Should().Be(supported);
        result.ParsedVersion.Should().Be(new Version(major, minor, build));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-version")]
    public void ParseNodeVersion_with_invalid_input_returns_not_available(string? output)
    {
        // Act
        var result = NodeAvailabilityValidator.ParseNodeVersion(output);

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.IsSupported.Should().BeFalse();
        result.ParsedVersion.Should().BeNull();
    }

    [Fact]
    public void CheckSidecarBuild_detects_missing_dist()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Act
            var result = NodeAvailabilityValidator.CheckSidecarBuild(tempDir);

            // Assert
            result.NeedsBuild.Should().BeTrue();
            result.DistPath.Should().EndWith(Path.Combine("dist", "index.js"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CheckSidecarBuild_detects_existing_dist()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var distDir = Path.Combine(tempDir, "dist");
        Directory.CreateDirectory(distDir);
        var distFile = Path.Combine(distDir, "index.js");
        File.WriteAllText(distFile, "console.log('test')");
        try
        {
            // Act
            var result = NodeAvailabilityValidator.CheckSidecarBuild(tempDir);

            // Assert
            result.NeedsBuild.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
