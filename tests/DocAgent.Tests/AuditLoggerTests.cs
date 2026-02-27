using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DocAgent.Tests;

public sealed class AuditLoggerTests
{
    private static AuditLogger Create(AuditOptions? auditOptions = null)
    {
        var opts = new DocAgentServerOptions
        {
            Audit = auditOptions ?? new AuditOptions()
        };
        return new AuditLogger(Options.Create(opts), NullLogger<AuditLogger>.Instance);
    }

    private static CallToolResult MakeResult(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static Dictionary<string, object?> MakeArgs(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    // ── Basic log success ─────────────────────────────────────────────

    [Fact]
    public void Log_Success_DoesNotThrow()
    {
        // AuditLogger uses ILogger internally. With NullLogger it should silently succeed.
        var sut = Create();
        var act = () => sut.Log(
            tool: "search_symbols",
            arguments: MakeArgs(("query", (object?)"Test")),
            result: MakeResult("{\"results\":[]}"),
            duration: TimeSpan.FromMilliseconds(42),
            success: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Log_Error_DoesNotThrow()
    {
        var sut = Create();
        var act = () => sut.Log(
            tool: "get_symbol",
            arguments: MakeArgs(("symbolId", (object?)"bad-id")),
            result: null,
            duration: TimeSpan.FromMilliseconds(5),
            success: false,
            error: "not_found");

        act.Should().NotThrow();
    }

    // ── Default verbosity omits full bodies ───────────────────────────

    [Fact]
    public void Log_DefaultVerbosity_InputFullIsNull()
    {
        // With Verbose=false (default), the AuditEntry.InputFull should be null.
        // We test indirectly by capturing log state through a custom logger.
        var captured = new List<string>();
        var logger = new CaptureLogger<AuditLogger>(captured);
        var opts = new DocAgentServerOptions { Audit = new AuditOptions { Verbose = false } };
        var sut = new AuditLogger(Options.Create(opts), logger);

        sut.Log("search_symbols", MakeArgs(("query", (object?)"hello")), MakeResult("{}"),
            TimeSpan.FromMilliseconds(10), success: true);

        // JSON line logged — inputFull should be null so "inputFull":null appears
        captured.Should().HaveCount(1);
        captured[0].Should().Contain("\"inputFull\":null");
    }

    [Fact]
    public void Log_VerboseMode_InputFullIsPopulated()
    {
        var captured = new List<string>();
        var logger = new CaptureLogger<AuditLogger>(captured);
        var opts = new DocAgentServerOptions { Audit = new AuditOptions { Verbose = true } };
        var sut = new AuditLogger(Options.Create(opts), logger);

        sut.Log("search_symbols", MakeArgs(("query", (object?)"hello")), MakeResult("{}"),
            TimeSpan.FromMilliseconds(10), success: true);

        captured.Should().HaveCount(1);
        captured[0].Should().NotContain("\"inputFull\":null");
        captured[0].Should().Contain("inputFull");
    }

    // ── File output ───────────────────────────────────────────────────

    [Fact]
    public async Task Log_WithFilePath_AppendsJsonlToFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var sut = Create(new AuditOptions { FilePath = tempFile });
            sut.Log("search_symbols", null, MakeResult("{}"), TimeSpan.FromMilliseconds(1), success: true);

            // File append is fire-and-forget; give it a brief moment
            await Task.Delay(200);

            var content = await File.ReadAllTextAsync(tempFile);
            content.Should().NotBeEmpty();
            content.Should().Contain("search_symbols");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Redaction ────────────────────────────────────────────────────

    [Fact]
    public void Log_Redaction_AppliesPattern()
    {
        var captured = new List<string>();
        var logger = new CaptureLogger<AuditLogger>(captured);
        var opts = new DocAgentServerOptions
        {
            Audit = new AuditOptions
            {
                Verbose = true,
                RedactionPatterns = ["secret123"]
            }
        };
        var sut = new AuditLogger(Options.Create(opts), logger);

        sut.Log("search_symbols", MakeArgs(("query", (object?)"secret123")), MakeResult("{}"),
            TimeSpan.FromMilliseconds(5), success: true);

        captured[0].Should().Contain("[REDACTED]");
        captured[0].Should().NotContain("secret123");
    }

    // ── Null tool name ────────────────────────────────────────────────

    [Fact]
    public void Log_NullTool_UsesUnknownFallback()
    {
        var captured = new List<string>();
        var logger = new CaptureLogger<AuditLogger>(captured);
        var opts = new DocAgentServerOptions();
        var sut = new AuditLogger(Options.Create(opts), logger);

        sut.Log(null, null, null, TimeSpan.Zero, success: false);

        captured[0].Should().Contain("(unknown)");
    }

    // ── Helper: capture logger ─────────────────────────────────────────

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        private readonly List<string> _log;
        public CaptureLogger(List<string> log) => _log = log;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _log.Add(formatter(state, exception));
        }
    }
}
