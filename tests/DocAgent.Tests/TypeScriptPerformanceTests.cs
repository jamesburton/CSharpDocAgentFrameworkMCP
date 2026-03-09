using System.Diagnostics;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace DocAgent.Tests;

public class TypeScriptPerformanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly TypeScriptIngestionService _tsService;
    private readonly ITestOutputHelper _output;
    private readonly string _stressProjectPath;

    public TypeScriptPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptPerformanceTests", Guid.NewGuid().ToString());
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

        _stressProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "stress-project"));
        if (!Directory.Exists(_stressProjectPath))
        {
            _stressProjectPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "artifacts", "stress-project"));
        }
    }

    [Fact]
    public async Task Measure_TypeScript_Ingestion_Performance()
    {
        if (!Directory.Exists(_stressProjectPath))
        {
            _output.WriteLine("Stress project not found. Skipping performance test.");
            return;
        }

        var tsconfigPath = Path.Combine(_stressProjectPath, "tsconfig.json");

        // 1. Cold Ingestion (First run)
        _output.WriteLine("Starting Cold Ingestion...");
        var sw = Stopwatch.StartNew();
        IngestionResult result1;
        try 
        {
            result1 = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new Exception($"Ingestion failed: {ex.Message}. Full: {ex}");
        }
        sw.Stop();
        _output.WriteLine($"Cold Ingestion 1 took: {sw.ElapsedMilliseconds}ms for {result1.SymbolCount} symbols.");
        
        result1.SymbolCount.Should().BeGreaterThan(400); // 150 files * (~3-4 symbols per file)

        // 2. Cold Ingestion (Second run, but snapshots might be cached in OS but not in our logic yet as we have no manifest)
        // Wait, the first run SAVED the manifest. So this will be an INCREMENTAL HIT.
        
        // 3. Warm Ingestion (Incremental Hit)
        _output.WriteLine("Starting Warm Ingestion (Incremental Hit)...");
        sw.Restart();
        var result2 = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);
        sw.Stop();
        _output.WriteLine($"Warm Ingestion (Hit) took: {sw.ElapsedMilliseconds}ms.");
        
        result2.SymbolCount.Should().Be(result1.SymbolCount);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "Incremental hit should be very fast (< 2s)");

        // 4. Incremental Miss (Change one file)
        var fileToChange = Path.Combine(_stressProjectPath, "src", "file0.ts");
        var originalContent = await File.ReadAllTextAsync(fileToChange);
        try
        {
            await File.AppendAllTextAsync(fileToChange, "\nexport const newSymbol = 123;");
            
            _output.WriteLine("Starting Incremental Ingestion (One file changed)...");
            sw.Restart();
            var result3 = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);
            sw.Stop();
            _output.WriteLine($"Incremental Ingestion (Miss) took: {sw.ElapsedMilliseconds}ms.");
            
            result3.SymbolCount.Should().BeGreaterThan(result1.SymbolCount);
        }
        finally
        {
            await File.WriteAllTextAsync(fileToChange, originalContent);
        }

        // 5. Search Performance
        _output.WriteLine("Measuring Search Performance for 'StressClass75'...");
        sw.Restart();
        var searchHits = await _index.SearchToListAsync("StressClass75");
        sw.Stop();
        _output.WriteLine($"Search took: {sw.ElapsedMilliseconds}ms. Found {searchHits.Count} hits.");
        
        foreach (var hit in searchHits)
        {
            _output.WriteLine($"Hit: {hit.Snippet} (ID: {hit.Id.Value})");
        }

        searchHits.Should().NotBeEmpty();
        searchHits.Any(h => h.Snippet.Contains("StressClass75")).Should().BeTrue();
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
