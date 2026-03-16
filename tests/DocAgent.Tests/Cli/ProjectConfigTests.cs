using System.Text.Json;
using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class ProjectConfigTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new ProjectConfig
        {
            PrimarySource = "MySolution.sln"
        };

        config.Version.Should().Be(1);
        config.ArtifactsDir.Should().Be(".docagent/artifacts");
        config.ExcludeTestFiles.Should().BeTrue();
        config.SecondarySources.Should().BeEmpty();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        var config = new ProjectConfig
        {
            Version = 1,
            PrimarySource = "src/MyApp.sln",
            ArtifactsDir = ".docagent/custom-artifacts",
            ExcludeTestFiles = false,
            SecondarySources =
            [
                new SecondarySource { Type = "nuget", Path = "MyPackage/1.0.0" },
                new SecondarySource { Type = "local", Path = "../other-project" }
            ]
        };

        var json = JsonSerializer.Serialize(config, ProjectConfig.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectConfig>(json, ProjectConfig.JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(1);
        deserialized.PrimarySource.Should().Be("src/MyApp.sln");
        deserialized.ArtifactsDir.Should().Be(".docagent/custom-artifacts");
        deserialized.ExcludeTestFiles.Should().BeFalse();
        deserialized.SecondarySources.Should().HaveCount(2);
        deserialized.SecondarySources[0].Type.Should().Be("nuget");
        deserialized.SecondarySources[0].Path.Should().Be("MyPackage/1.0.0");
        deserialized.SecondarySources[1].Type.Should().Be("local");
        deserialized.SecondarySources[1].Path.Should().Be("../other-project");
    }

    [Fact]
    public async Task JsonPropertyNames_AreCamelCase()
    {
        var config = new ProjectConfig
        {
            PrimarySource = "App.sln",
            ArtifactsDir = ".docagent/artifacts",
            ExcludeTestFiles = true
        };

        var json = JsonSerializer.Serialize(config, ProjectConfig.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("version", out _).Should().BeTrue("version should be camelCase");
        root.TryGetProperty("primarySource", out _).Should().BeTrue("primarySource should be camelCase");
        root.TryGetProperty("secondarySources", out _).Should().BeTrue("secondarySources should be camelCase");
        root.TryGetProperty("artifactsDir", out _).Should().BeTrue("artifactsDir should be camelCase");
        root.TryGetProperty("excludeTestFiles", out _).Should().BeTrue("excludeTestFiles should be camelCase");
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var config = new ProjectConfig
        {
            PrimarySource = "TestSolution.sln",
            SecondarySources = [new SecondarySource { Type = "nuget", Path = "SomeLib/2.0.0" }]
        };

        var path = Path.Combine(_tempDir, "docagent.project.json");
        await ProjectConfig.SaveAsync(config, path);

        File.Exists(path).Should().BeTrue();

        var loaded = await ProjectConfig.LoadAsync(path);
        loaded.Should().NotBeNull();
        loaded!.PrimarySource.Should().Be("TestSolution.sln");
        loaded.SecondarySources.Should().HaveCount(1);
        loaded.SecondarySources[0].Type.Should().Be("nuget");
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileNotFound()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.json");

        var result = await ProjectConfig.LoadAsync(path);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoriesAsNeeded()
    {
        var deepPath = Path.Combine(_tempDir, "deep", "nested", "dir", "docagent.project.json");

        var config = new ProjectConfig { PrimarySource = "App.sln" };
        await ProjectConfig.SaveAsync(config, deepPath);

        File.Exists(deepPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadFromDirAsync_LoadsFromDefaultFilename()
    {
        var config = new ProjectConfig { PrimarySource = "SomeSolution.sln" };
        var path = Path.Combine(_tempDir, "docagent.project.json");
        await ProjectConfig.SaveAsync(config, path);

        var loaded = await ProjectConfig.LoadFromDirAsync(_tempDir);

        loaded.Should().NotBeNull();
        loaded!.PrimarySource.Should().Be("SomeSolution.sln");
    }

    [Fact]
    public async Task LoadFromDirAsync_ReturnsNull_WhenFileAbsent()
    {
        var result = await ProjectConfig.LoadFromDirAsync(_tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FutureSecondarySourceTypes_AcceptedWithoutError()
    {
        var json = """
            {
              "version": 1,
              "primarySource": "App.sln",
              "secondarySources": [
                { "type": "nuget", "path": "SomePackage/3.0.0" },
                { "type": "future-unknown-type", "path": "/some/path" }
              ],
              "artifactsDir": ".docagent/artifacts",
              "excludeTestFiles": true
            }
            """;

        var act = () => JsonSerializer.Deserialize<ProjectConfig>(json, ProjectConfig.JsonOptions);

        act.Should().NotThrow();
        var config = JsonSerializer.Deserialize<ProjectConfig>(json, ProjectConfig.JsonOptions);
        config!.SecondarySources.Should().HaveCount(2);
        config.SecondarySources[1].Type.Should().Be("future-unknown-type");
    }
}
