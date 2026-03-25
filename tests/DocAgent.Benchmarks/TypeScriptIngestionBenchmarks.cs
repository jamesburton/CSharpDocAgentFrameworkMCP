using BenchmarkDotNet.Attributes;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.Benchmarks;

/// <summary>
/// Benchmarks for the TypeScript ingestion pipeline: cold start and warm start (incremental hit).
/// Measures the C# plumbing cost: manifest hash computation, store I/O, BM25 index writes.
///
/// Run with: dotnet run -c Release --project tests/DocAgent.Benchmarks
///
/// PREREQUISITE: Node.js must be installed and the sidecar must be built:
///   cd src/ts-symbol-extractor && npm install && npm run build
///
/// Expected performance targets (VERF-04):
/// - Cold start:  full ingestion including sidecar + store + index — varies by machine
/// - Warm start:  incremental hit (no sidecar call) &lt; 1500 ms for 100-file project
/// </summary>
[MemoryDiagnoser]
[JsonExporter]
public class TypeScriptIngestionBenchmarks
{
    // ─────────────────────────────────────────────────────────────────
    // Configuration
    // ─────────────────────────────────────────────────────────────────

    private const int SyntheticFileCount = 100;
    private const int ClassesPerFile = 3;
    private const int MethodsPerClass = 5;

    // ─────────────────────────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────────────────────────

    private string _projectDir = null!;
    private string _tsconfigPath = null!;
    private string _tempDir = null!;
    private SnapshotStore _store = null!;
    private BM25SearchIndex _index = null!;
    private TypeScriptIngestionService _tsService = null!;

    // ─────────────────────────────────────────────────────────────────
    // Setup / Teardown
    // ─────────────────────────────────────────────────────────────────

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DocAgent.Benchmarks.TS." + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_tempDir, "ts-bench-project");
        Directory.CreateDirectory(_projectDir);

        _store = new SnapshotStore(_tempDir);
        _index = new BM25SearchIndex(_tempDir);

        var options = new DocAgentServerOptions
        {
            AllowedPaths = ["**"],
            ArtifactsDir = _tempDir
        };

        // Resolve sidecar directory relative to assembly location
        var assemblyDir = Path.GetDirectoryName(typeof(TypeScriptIngestionBenchmarks).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", ".."));
        var sidecarDir = Path.Combine(repoRoot, "src", "ts-symbol-extractor");
        if (Directory.Exists(sidecarDir))
            options.SidecarDir = sidecarDir;
        else
            Console.WriteLine($"[BenchmarkSetup] WARNING: sidecar directory not found at '{sidecarDir}'. Benchmarks will fail.");

        var optionsWrapper = Options.Create(options);
        var allowlist = new PathAllowlist(optionsWrapper);

        _tsService = new TypeScriptIngestionService(
            _store, _index, allowlist, optionsWrapper,
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TypeScriptIngestionService>());

        _tsconfigPath = WriteSyntheticProject();

        var approxNodeCount = SyntheticFileCount * (1 + ClassesPerFile + ClassesPerFile * MethodsPerClass);
        Console.WriteLine($"[BenchmarkSetup] Synthetic project: {SyntheticFileCount} files, ~{approxNodeCount} expected nodes");
        Console.WriteLine($"[BenchmarkSetup] tsconfig: {_tsconfigPath}");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────
    // Benchmarks
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cold-start ingestion: forces a full sidecar run and store write on every iteration.
    /// Exercises: path validation, manifest computation, sidecar spawn, snapshot save, index write.
    /// </summary>
    [Benchmark(Description = "ColdStart_TS_Ingestion")]
    public async Task ColdStartIngestion()
    {
        await _tsService.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None, forceReindex: true);
    }

    /// <summary>
    /// Warm-start ingestion: first call writes the manifest, second call hits the incremental path.
    /// The warm call should skip the sidecar entirely and return in well under a second.
    /// Target: second call &lt; 1500 ms for a 100-file project.
    /// </summary>
    [Benchmark(Description = "WarmStart_TS_IncrementalHit")]
    public async Task WarmStartIncrementalHit()
    {
        // First pass: populates the manifest cache
        await _tsService.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None, forceReindex: false);
        // Second pass: incremental hit (no sidecar call, no re-index)
        await _tsService.IngestTypeScriptAsync(_tsconfigPath, CancellationToken.None, forceReindex: false);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private string WriteSyntheticProject()
    {
        var moduleNames = new[] { "alpha", "beta", "gamma", "delta", "epsilon",
                                   "zeta", "eta", "theta", "iota", "kappa" };

        var contractsDir = Path.Combine(_projectDir, "src", "contracts");
        Directory.CreateDirectory(contractsDir);
        File.WriteAllText(Path.Combine(contractsDir, "IEntity.ts"),
            "export interface IEntity { readonly id: string; execute(): void; }\n");

        for (var i = 0; i < SyntheticFileCount; i++)
        {
            var module = moduleNames[i % moduleNames.Length];
            var dir = Path.Combine(_projectDir, "src", module);
            Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("import { IEntity } from '../../contracts/IEntity';");
            for (var c = 0; c < ClassesPerFile; c++)
            {
                var className = $"BenchClass{i:D3}{(char)('A' + c)}";
                sb.AppendLine($"/** @description Bench class #{i * ClassesPerFile + c} in module {module} */");
                sb.AppendLine($"export class {className} implements IEntity {{");
                sb.AppendLine($"    readonly id = '{className}';");
                for (var m = 0; m < MethodsPerClass; m++)
                    sb.AppendLine($"    /** @param x input value @returns length plus offset */");
                sb.AppendLine($"    op{ClassesPerFile - 1}(x: string): number {{ return x.length; }}");
                sb.AppendLine($"    execute(): void {{}}");
                sb.AppendLine("}");
            }
            File.WriteAllText(Path.Combine(dir, $"BenchFile{i:D3}.ts"), sb.ToString());
        }

        var tsconfig = Path.Combine(_projectDir, "tsconfig.json");
        File.WriteAllText(tsconfig,
            """
            {
              "compilerOptions": {
                "target": "ES2020",
                "module": "commonjs",
                "strict": true,
                "outDir": "./dist"
              },
              "include": ["src/**/*"]
            }
            """);

        return tsconfig;
    }
}
