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

public class TypeScriptIngestionServiceTests : IDisposable
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

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TypeScriptIngestionService CreateService(DocAgentServerOptions? opts = null)
    {
        var o = opts ?? new DocAgentServerOptions { ArtifactsDir = _tempDir, AllowedPaths = ["**"] };
        var options = Options.Create(o);
        var allowlist = new PathAllowlist(options);
        return new TypeScriptIngestionService(
            _store, _indexMock.Object, allowlist, options,
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
    public async Task IngestTypeScriptAsync_incremental_hit_returns_skipped_true()
    {
        // Arrange
        var projectName = Path.GetFileName(_tempDir);
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>(), null, null);

        int spawnCount = 0;
        var service = CreateService();
        service.PipelineOverride = path =>
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Act 1: First ingestion
        var result1 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);
        result1.Skipped.Should().BeFalse();

        // Act 2: Second ingestion (no changes) — should be incremental hit
        var result2 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(1, "sidecar should NOT be called again on incremental hit");
        result2.Should().NotBeNull();
        result2.Skipped.Should().BeTrue();
        result2.Reason.Should().Be("no changes detected");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_incremental_miss_on_ts_file_change()
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

        // Change a .ts file
        File.WriteAllText(Path.Combine(_tempDir, "src", "index.ts"), "export const x = 2;");

        // Act 2: Second ingestion (with changes)
        var result2 = await _service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(2, "sidecar SHOULD be called again when .ts files change");
        result2.Skipped.Should().BeFalse();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_forceReindex_bypasses_incremental()
    {
        // Arrange
        var projectName = Path.GetFileName(_tempDir);
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>(), null, null);

        int spawnCount = 0;
        var service = CreateService();
        service.PipelineOverride = path =>
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Act 1: First ingestion
        await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);

        // Act 2: Second ingestion with forceReindex=true (no changes, but should still re-run)
        var result2 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None, forceReindex: true);

        // Assert
        spawnCount.Should().Be(2, "sidecar SHOULD be called when forceReindex=true even with no changes");
        result2.Skipped.Should().BeFalse();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_tsconfig_change_triggers_reingestion()
    {
        // Arrange
        var projectName = Path.GetFileName(_tempDir);
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>(), null, null);

        int spawnCount = 0;
        var service = CreateService();
        service.PipelineOverride = path =>
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Act 1: First ingestion
        await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);

        // Change tsconfig.json
        File.WriteAllText(_tsconfigPath, "{\"compilerOptions\":{\"strict\":true}}");

        // Act 2: Second ingestion — tsconfig changed
        var result2 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(2, "sidecar SHOULD be called when tsconfig.json changes");
        result2.Skipped.Should().BeFalse();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_package_lock_change_triggers_reingestion()
    {
        // Arrange — create a package-lock.json
        var packageLockPath = Path.Combine(_tempDir, "package-lock.json");
        File.WriteAllText(packageLockPath, "{\"lockfileVersion\":2}");

        var projectName = Path.GetFileName(_tempDir);
        var expectedSnapshot = new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null, DateTimeOffset.UtcNow,
            new List<SymbolNode>(), new List<SymbolEdge>(), null, null);

        int spawnCount = 0;
        var service = CreateService();
        service.PipelineOverride = path =>
        {
            spawnCount++;
            return Task.FromResult(expectedSnapshot);
        };

        // Act 1: First ingestion
        await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);
        spawnCount.Should().Be(1);

        // Change package-lock.json
        File.WriteAllText(packageLockPath, "{\"lockfileVersion\":3,\"packages\":{}}");

        // Act 2: Second ingestion — package-lock changed
        var result2 = await service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

        // Assert
        spawnCount.Should().Be(2, "sidecar SHOULD be called when package-lock.json changes");
        result2.Skipped.Should().BeFalse();
    }

    [Fact]
    public async Task IngestTypeScriptAsync_PathAllowlist_enforcement()
    {
        // Arrange — create service with restricted allowlist and separate artifacts dir
        // so that _tempDir is NOT auto-allowed via the artifacts directory bypass
        var separateArtifacts = Path.Combine(Path.GetTempPath(), "DocAgentTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(separateArtifacts);
        try
        {
            var restrictedOptions = new DocAgentServerOptions
            {
                ArtifactsDir = separateArtifacts,
                AllowedPaths = [Path.Combine(_tempDir, "allowed-only")]
            };
            var service = CreateService(restrictedOptions);

            // Act — try to ingest path outside allowlist
            Func<Task> act = () => service.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*allow list*");
        }
        finally
        {
            try { Directory.Delete(separateArtifacts, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task IngestTypeScriptAsync_throws_ArgumentException_for_null_path()
    {
        // Act
        Func<Task> act = () => _service.IngestTypeScriptAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("tsconfigPath");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_missing_tsconfig_throws_with_category()
    {
        // Arrange
        var missingPath = Path.Combine(_tempDir, "nonexistent", "tsconfig.json");

        // Act
        Func<Task> act = () => _service.IngestTypeScriptAsync(missingPath, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<TypeScriptIngestionException>();
        ex.Which.Category.Should().Be("tsconfig_invalid");
    }
}
