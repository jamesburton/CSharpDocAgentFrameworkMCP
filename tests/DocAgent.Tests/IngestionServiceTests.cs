using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for <see cref="IngestionService"/> using stub dependencies.
/// The PipelineOverride seam is used to avoid real Roslyn/MSBuild invocations.
/// </summary>
public sealed class IngestionServiceTests : IDisposable
{
    private readonly string _tempDir;

    public IngestionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ingestion-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IngestionService CreateService(
        ISearchIndex? index = null,
        int timeoutSeconds = 300,
        string? artifactsDir = null)
    {
        var store = new SnapshotStore(artifactsDir ?? _tempDir);
        var opts = new DocAgentServerOptions { IngestionTimeoutSeconds = timeoutSeconds };
        return new IngestionService(
            store,
            index ?? new StubSearchIndex(),
            Options.Create(opts),
            NullLogger<IngestionService>.Instance);
    }

    private static SymbolGraphSnapshot MakeSnapshot(string projectName = "TestProject", int nodeCount = 5) =>
        new(
            SchemaVersion: "1",
            ProjectName: projectName,
            SourceFingerprint: Guid.NewGuid().ToString("N"),
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: Enumerable.Range(0, nodeCount)
                .Select(i => new SymbolNode(
                    Id: new SymbolId($"Asm::Ns.Type{i}"),
                    Kind: SymbolKind.Type,
                    DisplayName: $"Type{i}",
                    FullyQualifiedName: $"Ns.Type{i}",
                    PreviousIds: [],
                    Accessibility: Accessibility.Public,
                    Docs: null,
                    Span: null,
                    ReturnType: null,
                    Parameters: Array.Empty<ParameterInfo>(),
                    GenericConstraints: Array.Empty<GenericConstraint>()))
                .ToList(),
            Edges: []);

    private static Func<ProjectInventory, List<string>, CancellationToken, Task<SymbolGraphSnapshot>>
        InstantPipeline(SymbolGraphSnapshot snapshot) =>
        (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_ReturnsResult_WithCorrectCounts()
    {
        var svc = CreateService();
        svc.PipelineOverride = InstantPipeline(MakeSnapshot(nodeCount: 7));

        // Create a temp .csproj so LocalProjectSource accepts the path.
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        var result = await svc.IngestAsync(csprojPath, null, null, false, null, CancellationToken.None);

        result.Should().NotBeNull();
        result.SymbolCount.Should().Be(7);
        result.ProjectCount.Should().Be(1);
        result.SnapshotId.Should().NotBeNullOrWhiteSpace();
        result.Warnings.Should().NotBeNull();
    }

    [Fact]
    public async Task IngestAsync_GlobFilter_Include_KeepsMatchingProjects()
    {
        // When include glob matches only specific projects, non-matching ones are excluded.
        var included = new List<string>();
        var svc = CreateService();
        svc.PipelineOverride = (inventory, _, ct) =>
        {
            included.AddRange(inventory.ProjectFiles);
            return Task.FromResult(MakeSnapshot());
        };

        // Create two csproj files in different subdirs.
        var subA = Path.Combine(_tempDir, "SubA");
        var subB = Path.Combine(_tempDir, "SubB");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);

        var projA = Path.Combine(subA, "ServiceA.csproj");
        var projB = Path.Combine(subB, "ServiceB.csproj");
        await File.WriteAllTextAsync(projA, "<Project />");
        await File.WriteAllTextAsync(projB, "<Project />");

        await svc.IngestAsync(_tempDir, includeGlob: "**/SubA/**", excludeGlob: null, false, null, CancellationToken.None);

        included.Should().ContainSingle(p => p.Contains("SubA"));
        included.Should().NotContain(p => p.Contains("SubB"));
    }

    [Fact]
    public async Task IngestAsync_GlobFilter_Exclude_RemovesMatchingProjects()
    {
        var included = new List<string>();
        var svc = CreateService();
        svc.PipelineOverride = (inventory, _, ct) =>
        {
            included.AddRange(inventory.ProjectFiles);
            return Task.FromResult(MakeSnapshot());
        };

        var subA = Path.Combine(_tempDir, "SubA");
        var subB = Path.Combine(_tempDir, "Tests");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);

        var projA = Path.Combine(subA, "App.csproj");
        var projB = Path.Combine(subB, "App.Tests.csproj");
        await File.WriteAllTextAsync(projA, "<Project />");
        await File.WriteAllTextAsync(projB, "<Project />");

        await svc.IngestAsync(_tempDir, includeGlob: null, excludeGlob: "**/Tests/**", false, null, CancellationToken.None);

        included.Should().ContainSingle(p => p.Contains("SubA"));
        included.Should().NotContain(p => p.Contains("Tests"));
    }

    [Fact]
    public async Task IngestAsync_Timeout_ThrowsOperationCanceledException()
    {
        var svc = CreateService(timeoutSeconds: 1);
        svc.PipelineOverride = async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct); // will be cancelled
            return MakeSnapshot();
        };

        var csprojPath = Path.Combine(_tempDir, "Slow.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        var act = () => svc.IngestAsync(csprojPath, null, null, false, null, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IngestAsync_DifferentPaths_RunInParallel_NoDeadlock()
    {
        // Two different paths should not serialize — both should complete without blocking each other.
        // Use separate artifact dirs per task to avoid manifest file conflicts.
        var dirA = Path.Combine(_tempDir, "artifacts-a");
        var dirB = Path.Combine(_tempDir, "artifacts-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var reachedPipeline = 0;
        var bothStarted = new SemaphoreSlim(0, 2);

        // We need two separate services with separate artifact dirs to avoid manifest contention.
        var svcA = CreateService(artifactsDir: dirA);
        var svcB = CreateService(artifactsDir: dirB);

        Func<ProjectInventory, List<string>, CancellationToken, Task<SymbolGraphSnapshot>> makeOverride() =>
            async (_, _, ct) =>
            {
                Interlocked.Increment(ref reachedPipeline);
                bothStarted.Release();
                await Task.Delay(30, ct);
                return MakeSnapshot();
            };

        svcA.PipelineOverride = makeOverride();
        svcB.PipelineOverride = makeOverride();

        var pathA = Path.Combine(_tempDir, "PathA.csproj");
        var pathB = Path.Combine(_tempDir, "PathB.csproj");
        await File.WriteAllTextAsync(pathA, "<Project />");
        await File.WriteAllTextAsync(pathB, "<Project />");

        var taskA = svcA.IngestAsync(pathA, null, null, false, null, CancellationToken.None);
        var taskB = svcB.IngestAsync(pathB, null, null, false, null, CancellationToken.None);

        // Both should complete without deadlock.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(taskA, taskB).WaitAsync(cts.Token);

        reachedPipeline.Should().Be(2);
    }

    [Fact]
    public async Task IngestAsync_SamePath_Serialized_SecondWaitsForFirst()
    {
        // Same path must serialize — verify second call runs after first completes.
        var executionOrder = new List<string>();
        var firstStarted = new TaskCompletionSource();
        var firstCanProceed = new TaskCompletionSource();

        var callCount = 0;
        var svc = CreateService();
        svc.PipelineOverride = async (_, _, ct) =>
        {
            var callIndex = Interlocked.Increment(ref callCount);
            executionOrder.Add($"start-{callIndex}");
            if (callIndex == 1)
            {
                firstStarted.TrySetResult();
                await firstCanProceed.Task;
            }
            executionOrder.Add($"end-{callIndex}");
            return MakeSnapshot();
        };

        var csprojPath = Path.Combine(_tempDir, "Shared.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        var taskA = svc.IngestAsync(csprojPath, null, null, false, null, CancellationToken.None);

        // Wait until first call is running, then start the second.
        await firstStarted.Task;
        var taskB = svc.IngestAsync(csprojPath, null, null, false, null, CancellationToken.None);

        // Let first complete.
        firstCanProceed.TrySetResult();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(taskA, taskB).WaitAsync(cts.Token);

        // First call must fully complete before second begins.
        executionOrder.Should().Equal("start-1", "end-1", "start-2", "end-2");
    }

    [Fact]
    public async Task IngestAsync_IndexFailure_ReturnsResultWithIndexError()
    {
        var failingIndex = new FailingSearchIndex();
        var svc = CreateService(index: failingIndex);
        svc.PipelineOverride = InstantPipeline(MakeSnapshot());

        var csprojPath = Path.Combine(_tempDir, "IndexFail.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        var result = await svc.IngestAsync(csprojPath, null, null, false, null, CancellationToken.None);

        // Snapshot was saved — result is still returned, not an exception.
        result.SnapshotId.Should().NotBeNullOrWhiteSpace();
        result.IndexError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IngestAsync_ProgressCallback_InvokedForAllStages()
    {
        var progress = new List<(int current, int total, string stage)>();
        var svc = CreateService();
        svc.PipelineOverride = InstantPipeline(MakeSnapshot());

        var csprojPath = Path.Combine(_tempDir, "Progress.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        await svc.IngestAsync(csprojPath, null, null, false,
            (current, total, stage) =>
            {
                progress.Add((current, total, stage));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        progress.Should().HaveCount(4);
        progress.Select(p => p.current).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task IngestAsync_ForceReindexTrue_PassesTrueToIndex()
    {
        // forceReindex=true should be forwarded to ISearchIndex.IndexAsync.
        var stub = new StubSearchIndex();
        var svc = CreateService(index: stub);
        svc.PipelineOverride = InstantPipeline(MakeSnapshot());

        var csprojPath = Path.Combine(_tempDir, "Force.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        await svc.IngestAsync(csprojPath, null, null, forceReindex: true, null, CancellationToken.None);

        stub.LastForceReindex.Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_ForceReindexFalse_PassesFalseToIndex()
    {
        // forceReindex=false (default) should be forwarded as false to ISearchIndex.IndexAsync.
        var stub = new StubSearchIndex();
        var svc = CreateService(index: stub);
        svc.PipelineOverride = InstantPipeline(MakeSnapshot());

        var csprojPath = Path.Combine(_tempDir, "NoForce.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project />");

        await svc.IngestAsync(csprojPath, null, null, forceReindex: false, null, CancellationToken.None);

        stub.LastForceReindex.Should().BeFalse();
    }

    // ── Stub implementations ──────────────────────────────────────────────────

    private sealed class StubSearchIndex : ISearchIndex
    {
        public int IndexCallCount { get; private set; }
        public bool LastForceReindex { get; private set; }

        public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct, bool forceReindex = false)
        {
            IndexCallCount++;
            LastForceReindex = forceReindex;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<SearchHit> SearchAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, string? projectFilter = null)
        {
            yield break;
        }

        public Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct) =>
            Task.FromResult<SymbolNode?>(null);
    }

    private sealed class FailingSearchIndex : ISearchIndex
    {
        public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct, bool forceReindex = false) =>
            Task.FromException(new InvalidOperationException("Index write failed."));

        public async IAsyncEnumerable<SearchHit> SearchAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, string? projectFilter = null)
        {
            yield break;
        }

        public Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct) =>
            Task.FromResult<SymbolNode?>(null);
    }
}
