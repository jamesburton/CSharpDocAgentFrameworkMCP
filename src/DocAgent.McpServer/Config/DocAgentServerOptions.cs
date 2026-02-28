namespace DocAgent.McpServer.Config;

/// <summary>Strongly-typed configuration options for the DocAgent MCP server.</summary>
public sealed class DocAgentServerOptions
{
    /// <summary>Glob patterns for allowed file paths. Empty means default to cwd.</summary>
    public string[] AllowedPaths { get; set; } = [];

    /// <summary>Glob patterns for denied file paths. Deny takes precedence over allow.</summary>
    public string[] DeniedPaths { get; set; } = [];

    /// <summary>When true, error responses include path and allowlist details instead of opaque messages.</summary>
    public bool VerboseErrors { get; set; } = false;

    /// <summary>Root directory for snapshot and index artifacts. Default: ./artifacts</summary>
    public string? ArtifactsDir { get; set; }

    /// <summary>Maximum seconds allowed for the ingestion pipeline. Default 300 (5 minutes).</summary>
    public int IngestionTimeoutSeconds { get; set; } = 300;

    /// <summary>Audit logging configuration.</summary>
    public AuditOptions Audit { get; set; } = new();
}

/// <summary>Audit logging configuration options.</summary>
public sealed class AuditOptions
{
    /// <summary>When true, logs full request and response bodies in addition to metadata.</summary>
    public bool Verbose { get; set; } = false;

    /// <summary>Optional path to append JSONL audit entries. Null disables file output.</summary>
    public string? FilePath { get; set; }

    /// <summary>Optional regex patterns to redact from audit log entries before writing.</summary>
    public string[]? RedactionPatterns { get; set; }
}
