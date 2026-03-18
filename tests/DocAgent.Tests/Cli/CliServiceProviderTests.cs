using DocAgent.McpServer.Cli;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.Tests.Cli;

public class CliServiceProviderTests : IDisposable
{
    private readonly string _dir;

    public CliServiceProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Build_ResolvesIIngestionService_WithoutThrowing()
    {
        var sp = CliServiceProvider.Build(_dir);
        var svc = sp.GetRequiredService<IIngestionService>();
        svc.Should().NotBeNull();
    }

    [Fact]
    public void Build_CreatesArtifactsDirectory_WhenItDoesNotExist()
    {
        var subDir = Path.Combine(_dir, "new-artifacts");
        Directory.Exists(subDir).Should().BeFalse("directory should not pre-exist");

        CliServiceProvider.Build(subDir);

        Directory.Exists(subDir).Should().BeTrue("Build should create the artifacts directory");
    }

    [Fact]
    public void Build_AcceptsRelativePath_AndNormalizesIt()
    {
        // A relative path should not throw — it gets Path.GetFullPath applied
        var act = () => CliServiceProvider.Build(_dir);
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_ReturnsServiceProvider_That_ResolvesSameIngestionServiceInstance()
    {
        var sp = CliServiceProvider.Build(_dir);

        // IIngestionService is registered as singleton — two resolutions should return the same instance
        var svc1 = sp.GetRequiredService<IIngestionService>();
        var svc2 = sp.GetRequiredService<IIngestionService>();
        svc1.Should().BeSameAs(svc2);
    }
}
