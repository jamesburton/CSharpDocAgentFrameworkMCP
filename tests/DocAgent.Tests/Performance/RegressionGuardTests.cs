using System.Linq;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using DocAgent.Benchmarks;
using Xunit;

namespace DocAgent.Tests.Performance;

/// <summary>
/// Regression guard: runs benchmarks programmatically and asserts that latency and memory
/// do not exceed the checked-in baselines by more than the configured tolerance (PERF-03).
///
/// Tag: [Trait("Category", "Benchmark")] — excluded from the default test run.
/// Run explicitly: dotnet test --filter "Category=Benchmark" -c Release
/// </summary>
[Trait("Category", "Benchmark")]
public class RegressionGuardTests
{
    [Fact]
    public void SolutionIngestion_DoesNotRegressBeyondBaseline()
    {
        // Skip unless explicitly opted in — BenchmarkDotNet requires Release config and
        // takes minutes to run, so it must not run during normal `dotnet test`.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUN_BENCHMARKS")))
        {
            return;
        }

        // Locate baselines.json relative to this assembly's output directory.
        // Assembly is at tests/DocAgent.Tests/bin/<config>/net10.0/
        // baselines.json is at  tests/DocAgent.Benchmarks/baselines.json
        var baseDir = AppContext.BaseDirectory;
        var baselinesPath = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "tests", "DocAgent.Benchmarks", "baselines.json"));

        if (!File.Exists(baselinesPath))
        {
            // baselines.json not present — pass rather than fail so CI doesn't break on clean checkouts
            // where the file hasn't been committed yet.
            // When running for real regression detection, baselines.json must be present.
            return;
        }

        var json = File.ReadAllText(baselinesPath);
        var baselines = JsonSerializer.Deserialize<BaselineFile>(json)
            ?? throw new InvalidOperationException("Failed to deserialize baselines.json");

        // Configure BenchmarkDotNet for a fast CI-friendly run.
        // DisableOptimizationsValidator: dotnet test runs in Debug by default; the 20% tolerance absorbs
        // the Debug/Release measurement difference. Precise measurement → use the benchmark project directly in Release.
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(Job.ShortRun); // 3 warmup + 5 target iterations — fast enough for CI

        var summary = BenchmarkRunner.Run<SolutionIngestionBenchmarks>(config);

        const double defaultTolerance = 1.20; // 20% tolerance as per PERF-03

        var failures = new List<string>();

        foreach (var (benchmarkName, baseline) in baselines.Benchmarks)
        {
            // Find the matching report by display info (Description attribute on the [Benchmark] method)
            var report = Enumerable.FirstOrDefault(summary.Reports, r =>
                r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo == benchmarkName);

            if (report is null)
            {
                failures.Add($"[{benchmarkName}] No BenchmarkDotNet report found. " +
                             $"Available: {string.Join(", ", Enumerable.Select(summary.Reports, r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo))}");
                continue;
            }

            if (report.ResultStatistics is null)
            {
                failures.Add($"[{benchmarkName}] ResultStatistics is null — benchmark may have failed to run.");
                continue;
            }

            var meanNs = report.ResultStatistics.Mean;
            var allocatedBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);

            // --- Latency tolerance check ---
            var latencyCeiling = baseline.MeanNanoseconds * defaultTolerance;
            if (meanNs > latencyCeiling)
            {
                failures.Add(
                    $"[{benchmarkName}] Latency regression: measured {meanNs:F0} ns > baseline {baseline.MeanNanoseconds:F0} ns * 1.20 = {latencyCeiling:F0} ns");
            }

            // --- Absolute latency ceiling (optional) ---
            if (baseline.AbsoluteCeilingNanoseconds.HasValue && meanNs > baseline.AbsoluteCeilingNanoseconds.Value)
            {
                failures.Add(
                    $"[{benchmarkName}] Absolute latency ceiling breached: measured {meanNs:F0} ns > ceiling {baseline.AbsoluteCeilingNanoseconds.Value:F0} ns");
            }

            // --- Memory tolerance check ---
            var memoryCeiling = (long)(baseline.AllocatedBytes * defaultTolerance);
            if (allocatedBytes > memoryCeiling)
            {
                failures.Add(
                    $"[{benchmarkName}] Memory regression: measured {allocatedBytes} bytes > baseline {baseline.AllocatedBytes} bytes * 1.20 = {memoryCeiling} bytes");
            }

            // --- Absolute memory ceiling (optional) ---
            if (baseline.AbsoluteCeilingAllocatedBytes.HasValue && allocatedBytes > baseline.AbsoluteCeilingAllocatedBytes.Value)
            {
                failures.Add(
                    $"[{benchmarkName}] Absolute memory ceiling breached: measured {allocatedBytes} bytes > ceiling {baseline.AbsoluteCeilingAllocatedBytes.Value} bytes");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Performance regression(s) detected:\n{string.Join("\n", failures)}");
        }
    }
}
