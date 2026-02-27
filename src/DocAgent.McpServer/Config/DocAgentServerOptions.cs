namespace DocAgent.McpServer.Config;

/// <summary>Strongly-typed configuration options for the DocAgent MCP server.</summary>
public sealed class DocAgentServerOptions
{
    /// <summary>Glob patterns for allowed file paths. Empty means default to cwd.</summary>
    public string[] AllowedPaths { get; init; } = [];

    /// <summary>Glob patterns for denied file paths. Deny takes precedence over allow.</summary>
    public string[] DeniedPaths { get; init; } = [];

    /// <summary>When true, error responses include path and allowlist details instead of opaque messages.</summary>
    public bool VerboseErrors { get; init; } = false;

    /// <summary>Root directory for snapshot and index artifacts. Default: ./artifacts</summary>
    public string? ArtifactsDir { get; init; }

    /// <summary>Audit logging configuration.</summary>
    public AuditOptions Audit { get; init; } = new();
}

/// <summary>Audit logging configuration options.</summary>
public sealed class AuditOptions
{
    /// <summary>When true, logs full request and response bodies in addition to metadata.</summary>
    public bool Verbose { get; init; } = false;

    /// <summary>Optional path to append JSONL audit entries. Null disables file output.</summary>
    public string? FilePath { get; init; }

    /// <summary>Optional regex patterns to redact from audit log entries before writing.</summary>
    public string[]? RedactionPatterns { get; init; }
}
