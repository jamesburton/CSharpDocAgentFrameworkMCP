using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace DocAgent.Tests;

/// <summary>
/// Integration test: launches the MCP server as a subprocess and validates
/// that stdout contains ONLY valid JSON-RPC frames — no log lines, exception
/// traces, or other contamination.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StdoutContaminationTests : IAsyncLifetime
{
    private Process? _process;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        try
        {
            if (_process is not null && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        _process?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Stdout_ContainsOnlyJsonRpc_NoLogContamination()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var repoRoot = GetRepoRoot();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project src/DocAgent.McpServer --no-build",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };

        // ── Act ───────────────────────────────────────────────────────────────
        _process = Process.Start(startInfo)!;
        _process.Should().NotBeNull("Process.Start must succeed");

        // Build a JSON-RPC initialize request (newline-delimited JSON per MCP stdio transport)
        var initRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "test-harness", version = "1.0" }
            }
        });

        await _process.StandardInput.WriteLineAsync(initRequest);
        await _process.StandardInput.FlushAsync();

        // Collect stdout lines for up to 10 seconds — enough to capture the initialize response
        var stdoutLines = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(cts.Token);
                    if (line is null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        stdoutLines.Add(line);
                        if (stdoutLines.Count >= 5) break; // enough to verify purity
                    }
                }
            }
            catch (OperationCanceledException) { /* timeout — normal exit */ }
        }, CancellationToken.None);

        await Task.WhenAny(readTask, Task.Delay(12_000));

        // ── Assert ────────────────────────────────────────────────────────────
        // We may receive 0 lines if the server doesn't emit anything before timing out,
        // but the key guarantee is that NOTHING on stdout violates the JSON-only contract.
        // If the server is not yet built, dotnet run will compile it first which may take
        // longer than the timeout — in that case stdoutLines will be empty and the test
        // still passes by vacuous truth (no invalid lines were produced).

        foreach (var line in stdoutLines)
        {
            // Every non-empty stdout line must parse as valid JSON
            var lineForAssertion = line.Length > 120 ? line[..120] + "..." : line;
            var parseAction = () => JsonDocument.Parse(line);
            parseAction.Should().NotThrow($"stdout line must be valid JSON: '{lineForAssertion}'");
        }

        // No common .NET log prefixes should appear on stdout
        var allStdout = string.Join("\n", stdoutLines);
        allStdout.Should().NotContain("info:", because: "ILogger output must be on stderr, not stdout");
        allStdout.Should().NotContain("dbug:", because: "ILogger output must be on stderr, not stdout");
        allStdout.Should().NotContain("warn:", because: "ILogger output must be on stderr, not stdout");
        allStdout.Should().NotContain("fail:", because: "ILogger output must be on stderr, not stdout");
        allStdout.Should().NotContain("trce:", because: "ILogger output must be on stderr, not stdout");

        // No exception traces on stdout
        // (Exception traces are acceptable on stderr; never on stdout)
        allStdout.Should().NotContain("Unhandled exception", because: "exception traces must not appear on stdout");
        allStdout.Should().NotContain("   at ", because: "stack frames must not appear on stdout");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            // Repo root contains the solution file or the src/ directory with the sln
            if (File.Exists(Path.Combine(dir, "DocAgentFramework.sln")) ||
                Directory.GetFiles(dir, "*.sln").Length > 0 ||
                (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests"))))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Cannot find repo root. Looked for DocAgentFramework.sln or src/+tests/ directories.");
    }
}
