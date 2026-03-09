using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocAgent.Tests;

public class TypeScriptIngestionServiceTests
{
    private readonly Mock<ISearchIndex> _indexMock = new();
    private readonly SnapshotStore _store;
    private readonly TypeScriptIngestionService _service;
    private readonly string _tempDir;
    private readonly string _tsconfigPath;

    public TypeScriptIngestionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DocAgentTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);

        _tsconfigPath = Path.Combine(_tempDir, "tsconfig.json");
        File.WriteAllText(_tsconfigPath, "{}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        File.WriteAllText(Path.Combine(_tempDir, "src", "index.ts"), "export const x = 1;");

        var options = Options.Create(new DocAgentServerOptions
        {
            ArtifactsDir = _tempDir,
            AllowedPaths = ["**"]
        });
        var allowlist = new PathAllowlist(options);
        _service = new TypeScriptIngestionService(
            _store,
            _indexMock.Object,
            allowlist,
            options,
            NullLogger<TypeScriptIngestionService>.Instance);
    }

    [Fact]
    public async Task IngestTypeScriptAsync_with_PipelineOverride_returns_snapshot()
    {
        // Arrange
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", "test-project", "fingerprint", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>());

        _service.PipelineOverride = path => Task.FromResult(expectedSnapshot);

        // Act
        var result = await _service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SnapshotId.Should().NotBeNullOrEmpty();
        
        // Verify saved to store
        var entries = await _store.ListAsync(CancellationToken.None);
        entries.Should().ContainSingle();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_performs_incremental_hit()
    {
        // Arrange
        var projectName = Path.GetFileName(_tempDir);
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>(), null, null);

        int spawnCount = 0;
        _service.PipelineOverride = path => 
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Create a new service instance to ensure fresh state, but same store/artifacts
        var options = Options.Create(new DocAgentServerOptions { ArtifactsDir = _tempDir, AllowedPaths = ["**"] });
        var allowlist = new PathAllowlist(options);
        var service = new TypeScriptIngestionService(_store, _indexMock.Object, allowlist, options, NullLogger<TypeScriptIngestionService>.Instance);
        service.PipelineOverride = _service.PipelineOverride;

        // Act 1: First ingestion
        await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);

        // Act 2: Second ingestion (no changes)
        var result2 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(1); // Should NOT have called pipeline override again
        result2.Should().NotBeNull();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_performs_incremental_miss_on_change()
    {
        // Arrange
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", Path.GetFileName(_tempDir), "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>());

        int spawnCount = 0;
        _service.PipelineOverride = path => 
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Act 1: First ingestion
        await _service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);

        // Change a file
        File.WriteAllText(Path.Combine(_tempDir, "src", "index.ts"), "export const x = 2;");

        // Act 2: Second ingestion (with changes)
        await _service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(2); // Should HAVE called pipeline override again
    }

    [Fact]
    public async Task IngestTypeScriptAsync_throws_ArgumentException_for_null_path()
    {
        // Act
        Func<Task> act = () => _service.IngestTypeScriptAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("tsconfigPath");
    }
}
