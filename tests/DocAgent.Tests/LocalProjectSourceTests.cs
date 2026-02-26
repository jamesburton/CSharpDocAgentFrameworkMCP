using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

[Trait("Category", "Integration")]
public sealed class LocalProjectSourceTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string SlnPath => Path.Combine(RepoRoot, "src", "DocAgentFramework.sln");
    private static string SrcDir => Path.Combine(RepoRoot, "src");
    private static string CoreCsproj => Path.Combine(RepoRoot, "src", "DocAgent.Core", "DocAgent.Core.csproj");

    [Fact]
    public async Task DiscoverAsync_with_solution_path_returns_projects()
    {
        var sut = new LocalProjectSource();

        var inventory = await sut.DiscoverAsync(new ProjectLocator(SlnPath), CancellationToken.None);

        inventory.ProjectFiles.Should().NotBeEmpty();
        inventory.ProjectFiles.Should().Contain(p => p.Contains("DocAgent.Core"));
        inventory.ProjectFiles.Should().Contain(p => p.Contains("DocAgent.Ingestion"));
        inventory.SolutionFiles.Should().ContainSingle(s => s.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_excludes_test_projects()
    {
        var sut = new LocalProjectSource(includeTestProjects: false);

        var inventory = await sut.DiscoverAsync(new ProjectLocator(SlnPath), CancellationToken.None);

        inventory.ProjectFiles.Should().NotContain(p =>
            p.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".Specs.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverAsync_with_csproj_path_returns_single_project()
    {
        var sut = new LocalProjectSource();

        var inventory = await sut.DiscoverAsync(new ProjectLocator(CoreCsproj), CancellationToken.None);

        inventory.ProjectFiles.Should().ContainSingle();
        inventory.ProjectFiles[0].Should().EndWith("DocAgent.Core.csproj");
        inventory.SolutionFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_with_directory_finds_solution()
    {
        var sut = new LocalProjectSource();

        var inventory = await sut.DiscoverAsync(new ProjectLocator(SrcDir), CancellationToken.None);

        inventory.SolutionFiles.Should().ContainSingle();
        inventory.SolutionFiles[0].ToLower().Should().EndWith(".sln");
        inventory.ProjectFiles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_sets_root_path()
    {
        var sut = new LocalProjectSource();

        var inventory = await sut.DiscoverAsync(new ProjectLocator(SlnPath), CancellationToken.None);

        inventory.RootPath.Should().Be(Path.GetDirectoryName(SlnPath));
    }
}
