using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocAgent.McpServer.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace DocAgent.McpServer.Security;

/// <summary>JSONL audit log entry written for every MCP tool call.</summary>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Tool,
    string InputSummary,
    string? InputFull,
    int ResponseBytes,
    string? ResponseFull,
    bool Success,
    string? ErrorCode,
    TimeSpan Duration);

/// <summary>
/// Writes JSONL audit entries to stderr (via ILogger) and optionally to a file.
/// Supports tiered verbosity: metadata-only by default, full bodies when Verbose=true.
/// Applies configurable regex redaction patterns before writing.
/// </summary>
public sealed class AuditLogger
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<AuditLogger> _logger;
    private readonly AuditOptions _auditOptions;
    private readonly IReadOnlyList<Regex> _redactionPatterns;

    public AuditLogger(IOptions<DocAgentServerOptions> options, ILogger<AuditLogger> logger)
    {
        _logger = logger;
        _auditOptions = options.Value.Audit;

        var patterns = new List<Regex>();
        if (_auditOptions.RedactionPatterns is { Length: > 0 } redactPatterns)
        {
            foreach (var pattern in redactPatterns)
                patterns.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
        }

        _redactionPatterns = patterns.AsReadOnly();
    }

    /// <summary>
    /// Log a completed tool call. Should be awaited — not fire-and-forget.
    /// </summary>
    public void Log(
        string? tool,
        IDictionary<string, object?>? arguments,
        CallToolResult? result,
        TimeSpan duration,
        bool success,
        string? error = null)
    {
        var inputSummary = BuildInputSummary(arguments);
        string? inputFull = _auditOptions.Verbose ? BuildInputFull(arguments) : null;

        var responseText = result?.Content is { Count: > 0 }
            ? string.Join("", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text))
            : string.Empty;
        var responseBytes = Encoding.UTF8.GetByteCount(responseText);
        string? responseFull = _auditOptions.Verbose ? responseText : null;

        var entry = new AuditEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Tool: tool ?? "(unknown)",
            InputSummary: inputSummary,
            InputFull: inputFull,
            ResponseBytes: responseBytes,
            ResponseFull: responseFull,
            Success: success,
            ErrorCode: error,
            Duration: duration);

        var jsonLine = Redact(JsonSerializer.Serialize(entry, s_jsonOptions));

        // Write to stderr via ILogger (LogToStandardErrorThreshold = Trace ensures this goes to stderr)
        _logger.LogInformation("[AUDIT] {JsonLine}", jsonLine);

        // Optionally append to file
        if (_auditOptions.FilePath is not null)
        {
            // Fire-and-forget is acceptable for file append; audit is best-effort for file output
            _ = AppendToFileAsync(_auditOptions.FilePath, jsonLine + "\n");
        }
    }

    private static string BuildInputSummary(IDictionary<string, object?>? arguments)
    {
        if (arguments is null or { Count: 0 })
            return "(no args)";

        var parts = arguments.Select(kv =>
        {
            var valueLen = kv.Value?.ToString()?.Length ?? 0;
            return $"{kv.Key}({valueLen})";
        });

        return string.Join(", ", parts);
    }

    private static string BuildInputFull(IDictionary<string, object?>? arguments)
    {
        if (arguments is null)
            return "{}";
        return JsonSerializer.Serialize(arguments);
    }

    private string Redact(string text)
    {
        foreach (var pattern in _redactionPatterns)
            text = pattern.Replace(text, "[REDACTED]");
        return text;
    }

    private static async Task AppendToFileAsync(string filePath, string content)
    {
        try
        {
            await File.AppendAllTextAsync(filePath, content);
        }
        catch
        {
            // Swallow file errors — don't let audit failure break the tool call
        }
    }
}
