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

    /// <summary>Maximum seconds allowed for the ingestion pipeline. Default 1800 (30 minutes).</summary>
    public int IngestionTimeoutSeconds { get; set; } = 1800;

    /// <summary>When true, skip test class files during ingestion. Base* files are always included.</summary>
    public bool ExcludeTestFiles { get; set; } = true;

    /// <summary>File name suffixes (no extension) treated as test files. Null = use defaults.</summary>
    public string[]? TestFileSuffixes { get; set; }

    /// <summary>Path to the TypeScript sidecar directory (contains dist/index.js). Default: ./src/ts-symbol-extractor</summary>
    public string? SidecarDir { get; set; }

    /// <summary>Path to the Node.js executable. Default: node</summary>
    public string NodeExecutable { get; set; } = "node";

    /// <summary>File extensions to scan for TypeScript incremental hashing. Default: .ts,.tsx</summary>
    public string[] TypeScriptFileExtensions { get; set; } = [".ts", ".tsx"];

    /// <summary>Audit logging configuration.</summary>
    public AuditOptions Audit { get; set; } = new();

    /// <summary>Rate limiting configuration for tool calls.</summary>
    public RateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>Token-bucket rate limiting configuration for MCP tool calls.</summary>
public sealed class RateLimitOptions
{
    /// <summary>When false, rate limiting is disabled entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum tokens in the query bucket.</summary>
    public int QueryTokenLimit { get; set; } = 100;

    /// <summary>Tokens added to the query bucket per replenishment period.</summary>
    public int QueryTokensPerPeriod { get; set; } = 100;

    /// <summary>Replenishment period in seconds for the query bucket.</summary>
    public int QueryReplenishmentPeriodSeconds { get; set; } = 60;

    /// <summary>Maximum tokens in the ingestion bucket.</summary>
    public int IngestionTokenLimit { get; set; } = 10;

    /// <summary>Tokens added to the ingestion bucket per replenishment period.</summary>
    public int IngestionTokensPerPeriod { get; set; } = 10;

    /// <summary>Replenishment period in seconds for the ingestion bucket.</summary>
    public int IngestionReplenishmentPeriodSeconds { get; set; } = 60;
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
