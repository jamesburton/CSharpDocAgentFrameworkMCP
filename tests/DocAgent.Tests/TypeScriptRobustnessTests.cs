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

public class TypeScriptRobustnessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly TypeScriptIngestionService _tsService;

    public TypeScriptRobustnessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptRobustnessTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        
        _store = new SnapshotStore(_tempDir);
        _index = new BM25SearchIndex(_tempDir);
        
        var options = new DocAgentServerOptions 
        { 
            AllowedPaths = ["**"],
            ArtifactsDir = _tempDir
        };
        var optionsWrapper = Options.Create(options);
        var allowlist = new PathAllowlist(optionsWrapper);

        _tsService = new TypeScriptIngestionService(
            _store, _index, allowlist, optionsWrapper, NullLogger<TypeScriptIngestionService>.Instance);

        // Resolve SidecarDir
        var sidecarDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ts-symbol-extractor"));
        if (!Directory.Exists(sidecarDir))
        {
            sidecarDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "src", "ts-symbol-extractor"));
        }
        options.SidecarDir = sidecarDir;
    }

    [Fact]
    public async Task IngestTypeScriptAsync_throws_UnauthorizedAccessException_if_outside_allowlist()
    {
        // Arrange
        var restrictedOptions = Options.Create(new DocAgentServerOptions { AllowedPaths = ["C:/Allowed"] });
        var restrictedAllowlist = new PathAllowlist(restrictedOptions);
        var service = new TypeScriptIngestionService(_store, _index, restrictedAllowlist, restrictedOptions, NullLogger<TypeScriptIngestionService>.Instance);

        // Act
        Func<Task> act = () => service.IngestTypeScriptAsync("C:/Forbidden/tsconfig.json", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*not in the configured allow list*");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_missing_tsconfig()
    {
        // Act
        Func<Task> act = () => _tsService.IngestTypeScriptAsync(Path.Combine(_tempDir, "non-existent.json"), CancellationToken.None);

        // Assert — service now validates tsconfig existence early with structured category
        var ex = await act.Should().ThrowAsync<TypeScriptIngestionException>().WithMessage("*tsconfig.json not found*");
        ex.Which.Category.Should().Be("tsconfig_invalid");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_invalid_tsconfig_json()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDir, "invalid-json");
        Directory.CreateDirectory(projectDir);
        var tsconfigPath = Path.Combine(projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, "{ invalid json }");

        // Act
        Func<Task> act = () => _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<TypeScriptIngestionException>().WithMessage("*Error reading tsconfig.json*");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_ts_syntax_errors_gracefully()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDir, "syntax-error");
        Directory.CreateDirectory(projectDir);
        var tsconfigPath = Path.Combine(projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, "{}");
        File.WriteAllText(Path.Combine(projectDir, "index.ts"), "export class Valid { }  export class Invalid { ??? syntax error ??? }");

        // Act
        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        // Assert
        // TS compiler is resilient and should still find the Valid class.
        result.SymbolCount.Should().BeGreaterThan(0);
        
        var searchHits = await _index.SearchToListAsync("Valid");
        searchHits.Should().NotBeEmpty();
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
