using DocAgent.McpServer.Cli;
using FluentAssertions;

namespace DocAgent.Tests.Cli;

public class HooksCommandTests : IDisposable
{
    private readonly string _workingDir;
    private readonly string _hooksDir;

    public HooksCommandTests()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _hooksDir = Path.Combine(_workingDir, ".git", "hooks");
        Directory.CreateDirectory(_hooksDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDir))
            Directory.Delete(_workingDir, recursive: true);
    }

    // ── Test 1: Enable creates post-commit and post-merge files ──────────────

    [Fact]
    public async Task RunAsync_Enable_CreatesPostCommitAndPostMergeFiles()
    {
        var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        File.Exists(Path.Combine(_hooksDir, "post-commit")).Should().BeTrue("post-commit hook should be created");
        File.Exists(Path.Combine(_hooksDir, "post-merge")).Should().BeTrue("post-merge hook should be created");
    }

    // ── Test 2: Hook content contains sentinel and command ───────────────────

    [Fact]
    public async Task RunAsync_Enable_HookContentContainsSentinelAndCommand()
    {
        var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        var postCommit = await File.ReadAllTextAsync(Path.Combine(_hooksDir, "post-commit"));
        postCommit.Should().Contain("# BEGIN docagent-mcp", "sentinel begin should be present");
        postCommit.Should().Contain("# END docagent-mcp", "sentinel end should be present");
        postCommit.Should().Contain("docagent update --quiet || true", "hook command should be present");

        var postMerge = await File.ReadAllTextAsync(Path.Combine(_hooksDir, "post-merge"));
        postMerge.Should().Contain("# BEGIN docagent-mcp");
        postMerge.Should().Contain("# END docagent-mcp");
        postMerge.Should().Contain("docagent update --quiet || true");
    }

    // ── Test 3: Idempotent — enable twice doesn't duplicate sentinel ─────────

    [Fact]
    public async Task RunAsync_EnableTwice_DoesNotDuplicateSentinel()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);
        var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        var postCommit = await File.ReadAllTextAsync(Path.Combine(_hooksDir, "post-commit"));
        var beginCount = CountOccurrences(postCommit, "# BEGIN docagent-mcp");
        beginCount.Should().Be(1, "sentinel block should not be duplicated on second enable");

        var endCount = CountOccurrences(postCommit, "# END docagent-mcp");
        endCount.Should().Be(1, "sentinel end should not be duplicated on second enable");
    }

    // ── Test 4: Existing hook content is preserved (append) ─────────────────

    [Fact]
    public async Task RunAsync_Enable_ExistingHookContentIsPreserved()
    {
        var existingContent = "#!/bin/sh\necho 'existing hook'\n";
        var hookPath = Path.Combine(_hooksDir, "post-commit");
        await File.WriteAllTextAsync(hookPath, existingContent);

        var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        var content = await File.ReadAllTextAsync(hookPath);
        content.Should().Contain("existing hook", "existing hook content must be preserved");
        content.Should().Contain("# BEGIN docagent-mcp", "sentinel should be added");
        content.Should().Contain("docagent update --quiet || true", "command should be added");
    }

    // ── Test 5: Disable removes sentinel block ───────────────────────────────

    [Fact]
    public async Task RunAsync_Disable_RemovesSentinelBlock()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);

        var exitCode = await HooksCommand.RunAsync(["disable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        // Hook files should be deleted when only sentinel content remains
        File.Exists(Path.Combine(_hooksDir, "post-commit")).Should().BeFalse(
            "hook file should be deleted when only sentinel content remains");
        File.Exists(Path.Combine(_hooksDir, "post-merge")).Should().BeFalse(
            "hook file should be deleted when only sentinel content remains");
    }

    // ── Test 6: Disable preserves other hook content ─────────────────────────

    [Fact]
    public async Task RunAsync_Disable_PreservesOtherHookContent()
    {
        var existingContent = "#!/bin/sh\necho 'my existing hook logic'\n";
        var hookPath = Path.Combine(_hooksDir, "post-commit");
        await File.WriteAllTextAsync(hookPath, existingContent);

        await HooksCommand.RunAsync(["enable"], workingDir: _workingDir);
        var exitCode = await HooksCommand.RunAsync(["disable"], workingDir: _workingDir);

        exitCode.Should().Be(0);

        File.Exists(hookPath).Should().BeTrue("hook file should remain because it had existing content");
        var content = await File.ReadAllTextAsync(hookPath);
        content.Should().Contain("my existing hook logic", "existing content should be preserved");
        content.Should().NotContain("# BEGIN docagent-mcp", "sentinel should have been removed");
        content.Should().NotContain("docagent update --quiet || true", "command should have been removed");
    }

    // ── Test 7: No .git dir → returns non-zero ───────────────────────────────

    [Fact]
    public async Task RunAsync_NoGitDir_ReturnsNonZero()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(emptyDir);
        try
        {
            var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: emptyDir);
            exitCode.Should().NotBe(0, "missing .git/hooks directory should cause non-zero exit");
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    // ── Test 8: No subcommand → returns 1 (usage) ────────────────────────────

    [Fact]
    public async Task RunAsync_NoSubcommand_ReturnOne()
    {
        var exitCode = await HooksCommand.RunAsync([], workingDir: _workingDir);
        exitCode.Should().Be(1, "missing subcommand should print usage and return 1");
    }

    // ── Test 9: "help" subcommand → returns 1 (usage) ────────────────────────

    [Fact]
    public async Task RunAsync_HelpSubcommand_ReturnOne()
    {
        var exitCode = await HooksCommand.RunAsync(["help"], workingDir: _workingDir);
        exitCode.Should().Be(1, "help subcommand should print usage and return 1");
    }

    // ── Test 10: Mode B uses binary path ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_Enable_ModeBUsesHomeBinaryPath()
    {
        var userConfigDir = Path.Combine(_workingDir, "userconfig");
        Directory.CreateDirectory(userConfigDir);
        var userConfigPath = Path.Combine(userConfigDir, "config.json");

        var config = new UserConfig { HostingMode = HostingMode.B };
        await UserConfig.SaveAsync(config, userConfigPath);

        var exitCode = await HooksCommand.RunAsync(
            ["enable"],
            workingDir: _workingDir,
            userConfigPath: userConfigPath);

        exitCode.Should().Be(0);

        var postCommit = await File.ReadAllTextAsync(Path.Combine(_hooksDir, "post-commit"));
        postCommit.Should().Contain("$HOME/.docagent/bin/docagent", "Mode B should use the home binary path");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
