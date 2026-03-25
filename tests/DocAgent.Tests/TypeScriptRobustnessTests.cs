using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace DocAgent.Tests;

public class TypeScriptRobustnessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly TypeScriptIngestionService _tsService;

    public TypeScriptRobustnessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptRobustnessTests", Guid.NewGuid().ToString());
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
    }

    [Fact]
    public async Task IngestTypeScriptAsync_throws_UnauthorizedAccessException_if_outside_allowlist()
    {
        // Arrange
        var restrictedOptions = Options.Create(new DocAgentServerOptions { AllowedPaths = ["C:/Allowed"] });
        var restrictedAllowlist = new PathAllowlist(restrictedOptions);
        var service = new TypeScriptIngestionService(_store, _index, restrictedAllowlist, restrictedOptions, NullLogger<TypeScriptIngestionService>.Instance);

        // Act
        Func<Task> act = () => service.IngestTypeScriptAsync("C:/Forbidden/tsconfig.json", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*not in the configured allow list*");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_missing_tsconfig()
    {
        // Act
        Func<Task> act = () => _tsService.IngestTypeScriptAsync(Path.Combine(_tempDir, "non-existent.json"), CancellationToken.None);

        // Assert — service now validates tsconfig existence early with structured category
        var ex = await act.Should().ThrowAsync<TypeScriptIngestionException>().WithMessage("*tsconfig.json not found*");
        ex.Which.Category.Should().Be("tsconfig_invalid");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_invalid_tsconfig_json()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDir, "invalid-json");
        Directory.CreateDirectory(projectDir);
        var tsconfigPath = Path.Combine(projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, "{ invalid json }");

        // Act
        Func<Task> act = () => _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<TypeScriptIngestionException>().WithMessage("*Error reading tsconfig.json*");
    }

    [Fact]
    public async Task IngestTypeScriptAsync_handles_ts_syntax_errors_gracefully()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDir, "syntax-error");
        Directory.CreateDirectory(projectDir);
        var tsconfigPath = Path.Combine(projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, "{}");
        File.WriteAllText(Path.Combine(projectDir, "index.ts"), "export class Valid { }  export class Invalid { ??? syntax error ??? }");

        // Act
        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        // Assert
        // TS compiler is resilient and should still find the Valid class.
        result.SymbolCount.Should().BeGreaterThan(0);
        
        var searchHits = await _index.SearchToListAsync("Valid");
        searchHits.Should().NotBeEmpty();
    }

    [Fact]
    public void IngestionTools_constructor_accepts_AuditLogger_parameter()
    {
        // Arrange — verify that IngestionTools now requires AuditLogger in its constructor
        var options = Options.Create(new DocAgentServerOptions
        {
            AllowedPaths = ["**"],
            ArtifactsDir = _tempDir,
        });
        var allowlist = new PathAllowlist(options);
        var auditLogger = new AuditLogger(options, NullLogger<AuditLogger>.Instance);

        var ingestionService = Mock.Of<IIngestionService>();
        var solutionIngestionService = Mock.Of<ISolutionIngestionService>();

        // Act — construct IngestionTools with AuditLogger
        var tools = new IngestionTools(
            ingestionService,
            solutionIngestionService,
            _tsService,
            allowlist,
            auditLogger,
            NullLogger<IngestionTools>.Instance);

        // Assert — construction succeeded (no exception)
        tools.Should().NotBeNull();
    }

    [Fact]
    public async Task IngestTypeScript_logs_audit_entry_with_symbolCount_and_duration()
    {
        // Arrange — set up a valid TS project to ingest
        var projectDir = Path.Combine(_tempDir, "audit-test");
        Directory.CreateDirectory(projectDir);
        var tsconfigPath = Path.Combine(projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, "{}");
        File.WriteAllText(Path.Combine(projectDir, "index.ts"), "export class AuditTestClass { value: number = 42; }");

        var options = Options.Create(new DocAgentServerOptions
        {
            AllowedPaths = ["**"],
            ArtifactsDir = _tempDir,
        });

        // Use a capturing logger to verify audit output
        var logMessages = new List<string>();
        var mockAuditLoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new InMemoryLoggerProvider(logMessages)));
        var auditLoggerInstance = new AuditLogger(options, mockAuditLoggerFactory.CreateLogger<AuditLogger>());
        var allowlist = new PathAllowlist(options);

        var ingestionService = Mock.Of<IIngestionService>();
        var solutionIngestionService = Mock.Of<ISolutionIngestionService>();

        var ingestionTools = new IngestionTools(
            ingestionService,
            solutionIngestionService,
            _tsService,
            allowlist,
            auditLoggerInstance,
            NullLogger<IngestionTools>.Instance);

        // We need a mock McpServer and RequestContext — use null-safe approach
        // The IngestTypeScript method needs these parameters but we can't easily mock them
        // Instead, verify audit logging by checking the captured log messages

        // Act — call the tool directly; McpServer & RequestContext will be null which is OK
        // since progressToken check handles nulls gracefully
        var result = await ingestionTools.IngestTypeScript(null!, null!, tsconfigPath);

        // Assert — audit log should have been written
        logMessages.Should().Contain(msg =>
            msg.Contains("[AUDIT]") &&
            msg.Contains("ingest_typescript") &&
            msg.Contains("symbolCount"));
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}

/// <summary>
/// Simple in-memory logger provider for capturing log messages in tests.
/// </summary>
internal sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages;

    public InMemoryLoggerProvider(List<string> messages) => _messages = messages;

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_messages);

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly List<string> _messages;

        public InMemoryLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
