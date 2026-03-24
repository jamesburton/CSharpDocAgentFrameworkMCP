using DocAgent.McpServer.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.Ingestion;

public class ToolScriptDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public ToolScriptDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tool-discovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Discover_FindsDotnetToolsJson()
    {
        var configDir = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "dotnet-tools.json"),
            """{ "version": 1, "tools": {} }""");

        var result = ToolScriptDiscovery.DiscoverToolsAndScripts(_tempDir);

        result.DotnetToolsManifests.Should().ContainSingle();
        result.DotnetToolsManifests[0].Should().EndWith("dotnet-tools.json");
    }

    [Fact]
    public void Discover_FindsDotnetToolsJsonInParentDirectory()
    {
        // Put dotnet-tools.json in parent
        var parentConfigDir = Path.Combine(_tempDir, ".config");
        Directory.CreateDirectory(parentConfigDir);
        File.WriteAllText(
            Path.Combine(parentConfigDir, "dotnet-tools.json"),
            """{ "version": 1, "tools": {} }""");

        // Create a child directory to search from
        var childDir = Path.Combine(_tempDir, "src", "MyProject");
        Directory.CreateDirectory(childDir);

        var result = ToolScriptDiscovery.DiscoverToolsAndScripts(childDir);

        result.DotnetToolsManifests.Should().ContainSingle();
    }

    [Fact]
    public void Discover_FindsTargetsAndPropsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(_tempDir, "Directory.Build.props"), "<Project />");

        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "custom.targets"), "<Project />");

        var result = ToolScriptDiscovery.DiscoverToolsAndScripts(_tempDir);

        result.MSBuildFiles.Should().HaveCount(3);
        result.MSBuildFiles.Should().Contain(f => f.EndsWith("build.targets"));
        result.MSBuildFiles.Should().Contain(f => f.EndsWith("Directory.Build.props"));
        result.MSBuildFiles.Should().Contain(f => f.EndsWith("custom.targets"));
    }

    [Fact]
    public void Discover_EmptyDirectory_ReturnsEmptyResults()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = ToolScriptDiscovery.DiscoverToolsAndScripts(emptyDir);

        result.DotnetToolsManifests.Should().BeEmpty();
        result.MSBuildFiles.Should().BeEmpty();
    }

    [Fact]
    public void Discover_NonexistentDirectory_ReturnsEmptyResults()
    {
        var result = ToolScriptDiscovery.DiscoverToolsAndScripts(
            Path.Combine(_tempDir, "does-not-exist"));

        result.DotnetToolsManifests.Should().BeEmpty();
        result.MSBuildFiles.Should().BeEmpty();
    }
}
