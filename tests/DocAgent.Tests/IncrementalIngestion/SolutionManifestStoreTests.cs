using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.IncrementalIngestion;

public sealed class SolutionManifestStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"manifest-tests-{Guid.NewGuid():N}");

    public SolutionManifestStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ManifestFileName_UseSolutionRelativePath_NoCollision()
    {
        var slnPath = Path.Combine(_tempDir, "MySolution.sln");

        var name1 = SolutionManifestStore.ManifestFileName(
            slnPath, Path.Combine(_tempDir, "src", "A", "MyLib.csproj"));
        var name2 = SolutionManifestStore.ManifestFileName(
            slnPath, Path.Combine(_tempDir, "lib", "B", "MyLib.csproj"));

        name1.Should().NotBe(name2, "same-named projects in different directories must produce different manifest filenames");
        name1.Should().EndWith(".manifest.json");
        name2.Should().EndWith(".manifest.json");
    }

    [Fact]
    public void ManifestFileName_NormalizesPathSeparators()
    {
        var slnPath = Path.Combine(_tempDir, "MySolution.sln");
        var projectPath = Path.Combine(_tempDir, "src", "MyLib", "MyLib.csproj");

        var name = SolutionManifestStore.ManifestFileName(slnPath, projectPath);

        // Should not contain directory separators
        name.Should().NotContain("/");
        name.Should().NotContain("\\");
        name.Should().Contain("__", "separators should be replaced with double underscores");
    }

    [Fact]
    public async Task ComputeProjectManifest_IncludesProjectRefsAndTfm()
    {
        // Create a temp project directory with a .cs file
        var projectDir = Path.Combine(_tempDir, "TestProject");
        Directory.CreateDirectory(projectDir);
        var csFile = Path.Combine(projectDir, "Program.cs");
        await File.WriteAllTextAsync(csFile, "// hello");
        var projectFile = Path.Combine(projectDir, "TestProject.csproj");

        var manifest = await SolutionManifestStore.ComputeProjectManifestAsync(
            projectFile,
            ["../OtherProject/OtherProject.csproj"],
            "net10.0");

        manifest.FileHashes.Should().ContainKey(csFile);
        manifest.FileHashes.Should().ContainKey("__project_refs__");
        manifest.FileHashes.Should().ContainKey("__tfm__");
        manifest.FileHashes["__project_refs__"].Should().NotBeNullOrEmpty();
        manifest.FileHashes["__tfm__"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip()
    {
        var slnPath = Path.Combine(_tempDir, "MySolution.sln");
        var projectPath = Path.Combine(_tempDir, "src", "MyLib", "MyLib.csproj");

        var original = new FileHashManifest(
            new Dictionary<string, string> { ["file.cs"] = "abc123" },
            DateTimeOffset.UtcNow);

        await SolutionManifestStore.SaveAsync(_tempDir, slnPath, projectPath, original);
        var loaded = await SolutionManifestStore.LoadAsync(_tempDir, slnPath, projectPath);

        loaded.Should().NotBeNull();
        loaded!.FileHashes.Should().BeEquivalentTo(original.FileHashes);
    }

    [Fact]
    public void CleanOrphanedManifests_RemovesStaleFiles()
    {
        var manifestDir = SolutionManifestStore.ManifestDirectory(_tempDir);

        // Create two manifest files
        File.WriteAllText(Path.Combine(manifestDir, "keep.manifest.json"), "{}");
        File.WriteAllText(Path.Combine(manifestDir, "stale.manifest.json"), "{}");

        var current = new HashSet<string> { "keep.manifest.json" };
        SolutionManifestStore.CleanOrphanedManifests(_tempDir, current);

        File.Exists(Path.Combine(manifestDir, "keep.manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(manifestDir, "stale.manifest.json")).Should().BeFalse();
    }
}
