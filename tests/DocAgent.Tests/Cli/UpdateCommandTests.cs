using System.Text.Json;
using DocAgent.McpServer.Cli;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Moq;

namespace DocAgent.Tests.Cli;

public class UpdateCommandTests : IDisposable
{
    private readonly string _workingDir;
    private readonly Mock<IIngestionService> _mockIngestion;

    public UpdateCommandTests()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_workingDir);

        _mockIngestion = new Mock<IIngestionService>(MockBehavior.Strict);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDir))
            Directory.Delete(_workingDir, recursive: true);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task WriteProjectConfigAsync(string primarySource = "App.sln",
        string? artifactsDir = null,
        List<SecondarySource>? secondarySources = null)
    {
        var config = new ProjectConfig
        {
            PrimarySource = primarySource,
            ArtifactsDir = artifactsDir ?? Path.Combine(_workingDir, "artifacts"),
            SecondarySources = secondarySources ?? []
        };
        var path = Path.Combine(_workingDir, ProjectConfig.DefaultFileName);
        await ProjectConfig.SaveAsync(config, path);
    }

    private IngestionResult MakeResult(int symbolCount = 42, int projectCount = 2,
        string snapshotId = "abc123") =>
        new(snapshotId, symbolCount, projectCount,
            TimeSpan.FromMilliseconds(500), [], null);

    private void SetupIngestionMock(IngestionResult result)
    {
        _mockIngestion
            .Setup(s => s.IngestAsync(
                It.IsAny<string>(),
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(result);
    }

    // ── Test 1: No project JSON → non-zero exit ──────────────────────────────

    [Fact]
    public async Task RunAsync_MissingProjectJson_ReturnsNonZeroExitCode()
    {
        // No docagent.project.json written — directory is empty
        var exitCode = await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object);

        exitCode.Should().NotBe(0, "missing project config should cause non-zero exit");
    }

    [Fact]
    public async Task RunAsync_MissingProjectJson_WritesErrorMessage()
    {
        var captured = new List<string>();

        var exitCode = await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object,
            writeOutput: s => captured.Add(s));

        exitCode.Should().NotBe(0);
        // No JSON summary should have been emitted
        captured.Should().BeEmpty();
    }

    // ── Test 2: With project JSON → calls ingestion, outputs valid JSON ──────

    [Fact]
    public async Task RunAsync_WithProjectJson_CallsIngestionAndOutputsValidJson()
    {
        await WriteProjectConfigAsync(primarySource: "App.sln");
        var result = MakeResult(symbolCount: 100, projectCount: 3, snapshotId: "deadbeef");
        SetupIngestionMock(result);

        var captured = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object,
            writeOutput: s => captured.Add(s));

        exitCode.Should().Be(0);
        captured.Should().HaveCount(1, "exactly one JSON line should be written to stdout");

        var json = captured[0];
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("ok");

        root.TryGetProperty("symbolCount", out var symbolCountProp).Should().BeTrue();
        symbolCountProp.GetInt32().Should().Be(100);

        root.TryGetProperty("snapshotHash", out var hashProp).Should().BeTrue();
        hashProp.GetString().Should().Be("deadbeef");

        root.TryGetProperty("durationMs", out _).Should().BeTrue();
        root.TryGetProperty("projectsIngested", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WithProjectJson_IngestsThePrimarySource()
    {
        var primaryPath = Path.Combine(_workingDir, "MySolution.sln");
        await WriteProjectConfigAsync(primarySource: primaryPath);
        SetupIngestionMock(MakeResult());

        await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object);

        _mockIngestion.Verify(
            s => s.IngestAsync(
                primaryPath,
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false),
            Times.Once);
    }

    // ── Test 3: --quiet flag → still outputs JSON to stdout ──────────────────

    [Fact]
    public async Task RunAsync_QuietFlag_StillOutputsJsonSummary()
    {
        await WriteProjectConfigAsync();
        SetupIngestionMock(MakeResult());

        var captured = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            args: ["--quiet"],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object,
            writeOutput: s => captured.Add(s));

        exitCode.Should().Be(0);
        captured.Should().HaveCount(1, "--quiet should not suppress the JSON summary");

        using var doc = JsonDocument.Parse(captured[0]);
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
    }

    // ── Test 4: missing secondary source → skips, primary still ingested ──────

    [Fact]
    public async Task RunAsync_MissingSecondarySource_SkipsAndStillIngestsPrimary()
    {
        var primaryPath = Path.Combine(_workingDir, "App.sln");
        var missingSrc = "/nonexistent/path/does-not-exist.sln";

        await WriteProjectConfigAsync(
            primarySource: primaryPath,
            secondarySources:
            [
                new SecondarySource { Type = "dotnet", Path = missingSrc }
            ]);

        // Primary ingestion succeeds
        _mockIngestion
            .Setup(s => s.IngestAsync(
                primaryPath,
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(MakeResult());

        // Secondary ingestion is NOT set up (no matching call expected)

        var captured = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object,
            writeOutput: s => captured.Add(s));

        exitCode.Should().Be(0, "a missing secondary source is a warning, not a fatal error");
        captured.Should().HaveCount(1);

        // Primary was ingested
        _mockIngestion.Verify(
            s => s.IngestAsync(
                primaryPath,
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false),
            Times.Once);

        // Secondary was NOT ingested
        _mockIngestion.Verify(
            s => s.IngestAsync(
                missingSrc,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<Func<int, int, string, Task>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    // ── Test 5: summary aggregates across multiple ingestion results ──────────

    [Fact]
    public async Task RunAsync_WithMultipleSources_AggregatesSymbolCount()
    {
        var primaryPath = Path.Combine(_workingDir, "App.sln");
        var secondaryPath = Path.Combine(_workingDir, "Other.sln");

        // Create placeholder files so secondary source exists
        await File.WriteAllTextAsync(primaryPath, "placeholder");
        await File.WriteAllTextAsync(secondaryPath, "placeholder");

        await WriteProjectConfigAsync(
            primarySource: primaryPath,
            secondarySources:
            [
                new SecondarySource { Type = "dotnet", Path = secondaryPath }
            ]);

        _mockIngestion
            .Setup(s => s.IngestAsync(
                primaryPath,
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(MakeResult(symbolCount: 50, projectCount: 1, snapshotId: "hash1"));

        _mockIngestion
            .Setup(s => s.IngestAsync(
                secondaryPath,
                (string?)null,
                (string?)null,
                false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(MakeResult(symbolCount: 30, projectCount: 1, snapshotId: "hash2"));

        var captured = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            args: [],
            workingDir: _workingDir,
            ingestionService: _mockIngestion.Object,
            writeOutput: s => captured.Add(s));

        exitCode.Should().Be(0);

        using var doc = JsonDocument.Parse(captured[0]);
        var root = doc.RootElement;

        root.GetProperty("symbolCount").GetInt32().Should().Be(80, "50 + 30 = 80");
        root.GetProperty("projectsIngested").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }
}
