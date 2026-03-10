using BenchmarkDotNet.Attributes;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.Benchmarks;

/// <summary>
/// Measures latency (PERF-01) and memory high-water mark (PERF-02) during solution ingestion
/// using the repo's own solution as the benchmark target.
/// </summary>
[MemoryDiagnoser]
[JsonExporter]
public class SolutionIngestionBenchmarks
{
    private string _slnPath = null!;
    private string _tempDir = null!;
    private ISolutionIngestionService _service = null!;
    private MSBuildWorkspace? _warmWorkspace;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Resolve path to the repo's own solution relative to the benchmark assembly location
        var assemblyDir = Path.GetDirectoryName(typeof(SolutionIngestionBenchmarks).Assembly.Location)!;
        // Navigate from tests/DocAgent.Benchmarks/bin/Release/net10.0 up to repo root
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", ".."));
        _slnPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DocAgentFramework.sln"));

        _tempDir = Path.Combine(Path.GetTempPath(), "DocAgent.Benchmarks." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var store = new SnapshotStore(_tempDir);
        var index = new InMemorySearchIndex();
        var fullLogger = LoggerFactory.Create(b => b.AddConsole())
            .CreateLogger<SolutionIngestionService>();
        var fullService = new SolutionIngestionService(store, index, fullLogger, Options.Create(new DocAgentServerOptions()));
        var incrLogger = LoggerFactory.Create(b => b.AddConsole())
            .CreateLogger<IncrementalSolutionIngestionService>();
        _service = new IncrementalSolutionIngestionService(store, fullService, incrLogger);

        // Open a warm workspace and cache it for the warm benchmark
        _warmWorkspace = MSBuildWorkspace.Create();
        _warmWorkspace.OpenSolutionAsync(_slnPath).GetAwaiter().GetResult();

        if (_warmWorkspace.Diagnostics.Count > 0)
        {
            Console.WriteLine($"[BenchmarkSetup] MSBuildWorkspace opened with {_warmWorkspace.Diagnostics.Count} diagnostic(s).");
            if (_warmWorkspace.Diagnostics.All(d => d.Message.Contains("not found") || d.Message.Contains("zero projects", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("[BenchmarkSetup] WARNING: workspace opened 0 projects — benchmark results may be meaningless.");
            }
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _warmWorkspace?.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Measures full solution ingestion latency and memory (PERF-01, PERF-02).
    /// Forces a complete re-ingest to get worst-case numbers.
    /// </summary>
    [Benchmark(Description = "FullSolutionIngestion")]
    public async Task FullSolutionIngestion()
    {
        await _service.IngestAsync(_slnPath, null, CancellationToken.None, forceFullReingest: true);
    }

    /// <summary>
    /// Measures incremental skip-path latency when nothing has changed since last ingest.
    /// Runs a full ingest first (GlobalSetup does not pre-warm the store), then measures
    /// the second pass which should exercise the skip path.
    /// </summary>
    [Benchmark(Description = "IncrementalNoChange")]
    public async Task IncrementalNoChange()
    {
        // First call: populates the snapshot store
        await _service.IngestAsync(_slnPath, null, CancellationToken.None, forceFullReingest: false);
        // Second call: should hit the incremental skip path (nothing changed)
        await _service.IngestAsync(_slnPath, null, CancellationToken.None, forceFullReingest: false);
    }

    // Per-iteration workspace for ColdWorkspaceOpen
    private MSBuildWorkspace? _coldWorkspace;

    [IterationSetup(Target = nameof(ColdWorkspaceOpen))]
    public void SetupColdWorkspace()
    {
        _coldWorkspace = MSBuildWorkspace.Create();
    }

    [IterationCleanup(Target = nameof(ColdWorkspaceOpen))]
    public void CleanupColdWorkspace()
    {
        _coldWorkspace?.Dispose();
        _coldWorkspace = null;
    }

    /// <summary>
    /// Measures raw MSBuildWorkspace open latency without the ingestion pipeline overhead.
    /// Each iteration creates a fresh workspace to avoid caching effects.
    /// </summary>
    [Benchmark(Description = "ColdWorkspaceOpen")]
    public async Task ColdWorkspaceOpen()
    {
        await _coldWorkspace!.OpenSolutionAsync(_slnPath);
    }
}
