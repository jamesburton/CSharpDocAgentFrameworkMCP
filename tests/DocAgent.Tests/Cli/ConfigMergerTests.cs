using System.Text.Json;
using System.Text.Json.Nodes;
using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class ConfigMergerTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigMergerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string TempFile(string name = "config.json") =>
        Path.Combine(_tempDir, name);

    // ── Test 1: New file → creates with docagent entry, returns None ──────────

    [Fact]
    public async Task MergeAsync_NewFile_CreatesWithDocagentEntry_ReturnsNone()
    {
        var path = TempFile();
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");

        var result = await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);

        result.Should().Be(MergeConflict.None);
        File.Exists(path).Should().BeTrue();

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("mcpServers", out var servers).Should().BeTrue();
        servers.TryGetProperty("docagent", out _).Should().BeTrue();
    }

    // ── Test 2: Existing file with other servers → preserves them ────────────

    [Fact]
    public async Task MergeAsync_ExistingFileWithOtherServers_PreservesOtherServers()
    {
        var path = TempFile();
        await File.WriteAllTextAsync(path, """
            {
                "mcpServers": {
                    "other-server": {
                        "command": "other",
                        "args": []
                    }
                }
            }
            """);

        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");
        await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var servers = doc.RootElement.GetProperty("mcpServers");
        servers.TryGetProperty("other-server", out _).Should().BeTrue("other server should be preserved");
        servers.TryGetProperty("docagent", out _).Should().BeTrue("docagent should be added");
    }

    // ── Test 3: Idempotent → second call is a no-op ───────────────────────────

    [Fact]
    public async Task MergeAsync_Idempotent_SecondCallIsNoOp()
    {
        var path = TempFile();
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");

        await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);
        var contentAfterFirst = await File.ReadAllTextAsync(path);
        var modifiedAfterFirst = File.GetLastWriteTimeUtc(path);

        // Small sleep to allow mtime to differ if file is rewritten
        await Task.Delay(10);

        var result = await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);

        result.Should().Be(MergeConflict.None);
        var contentAfterSecond = await File.ReadAllTextAsync(path);
        contentAfterSecond.Should().Be(contentAfterFirst, "file should be byte-identical on idempotent call");
    }

    // ── Test 4: Conflict + nonInteractive without --yes → Skipped ────────────

    [Fact]
    public async Task MergeAsync_ConflictNonInteractiveNoYes_ReturnsSkipped_FileUnchanged()
    {
        var path = TempFile();
        await File.WriteAllTextAsync(path, """
            {
                "mcpServers": {
                    "docagent": {
                        "command": "old-command",
                        "args": []
                    }
                }
            }
            """);

        var originalContent = await File.ReadAllTextAsync(path);
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");

        var result = await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true, yesFlag: false);

        result.Should().Be(MergeConflict.Skipped);
        var contentAfter = await File.ReadAllTextAsync(path);
        contentAfter.Should().Be(originalContent, "file should be unchanged when skipped");
    }

    // ── Test 5: Conflict + nonInteractive with --yes → Overwritten ───────────

    [Fact]
    public async Task MergeAsync_ConflictNonInteractiveWithYes_ReturnsOverwritten()
    {
        var path = TempFile();
        await File.WriteAllTextAsync(path, """
            {
                "mcpServers": {
                    "docagent": {
                        "command": "old-command",
                        "args": []
                    }
                }
            }
            """);

        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");

        var result = await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true, yesFlag: true);

        result.Should().Be(MergeConflict.Overwritten);

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var docagent = doc.RootElement.GetProperty("mcpServers").GetProperty("docagent");
        docagent.GetProperty("command").GetString().Should().Be("docagent",
            "command should be updated to new entry value");
    }

    // ── BuildMcpEntry tests ───────────────────────────────────────────────────

    [Fact]
    public void BuildMcpEntry_ModeA_HasDocagentCommand()
    {
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/home/user/.docagent/artifacts");

        entry["command"]!.GetValue<string>().Should().Be("docagent");
        entry["args"].Should().NotBeNull();
        var env = entry["env"]!.AsObject();
        env["DOCAGENT_ARTIFACTS_DIR"]!.GetValue<string>().Should().Be("/home/user/.docagent/artifacts");
    }

    [Fact]
    public void BuildMcpEntry_ModeB_HasAbsoluteBinaryPath()
    {
        var entry = ConfigMerger.BuildMcpEntry(
            HostingMode.B,
            binaryPath: "/usr/local/bin/docagent",
            artifactsDir: "/tmp/artifacts");

        entry["command"]!.GetValue<string>().Should().Be("/usr/local/bin/docagent");
        var env = entry["env"]!.AsObject();
        env["DOCAGENT_ARTIFACTS_DIR"]!.GetValue<string>().Should().Be("/tmp/artifacts");
    }

    [Fact]
    public void BuildMcpEntry_ModeC_HasDotnetRunCommand()
    {
        var entry = ConfigMerger.BuildMcpEntry(
            HostingMode.C,
            sourcePath: "/home/user/projects/docagent/src/DocAgent.McpServer",
            artifactsDir: "/tmp/artifacts");

        entry["command"]!.GetValue<string>().Should().Be("dotnet");
        var args = entry["args"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToList();
        args.Should().ContainInOrder("run", "--project",
            "/home/user/projects/docagent/src/DocAgent.McpServer");
        var env = entry["env"]!.AsObject();
        env["DOCAGENT_ARTIFACTS_DIR"]!.GetValue<string>().Should().Be("/tmp/artifacts");
    }

    [Fact]
    public void BuildMcpEntry_ArgsIsEmptyArray_ForModeA()
    {
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp");

        var args = entry["args"]!.AsArray();
        args.Should().BeEmpty();
    }

    // ── Dot-notation keyPath test ──────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_DotNotationKeyPath_NavigatesCorrectly()
    {
        var path = TempFile();
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp/artifacts");

        await ConfigMerger.MergeAsync(path, "mcp.servers", entry, nonInteractive: true);

        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("mcp", out var mcp).Should().BeTrue();
        mcp.TryGetProperty("servers", out var servers).Should().BeTrue();
        servers.TryGetProperty("docagent", out _).Should().BeTrue();
    }

    // ── Write format test ──────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_OutputIsIndented()
    {
        var path = TempFile();
        var entry = ConfigMerger.BuildMcpEntry(HostingMode.A, artifactsDir: "/tmp");

        await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);

        var json = await File.ReadAllTextAsync(path);
        json.Should().Contain("\n", "output should be indented with newlines");
    }
}
