using System;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Thrown when the TypeScript sidecar process fails or returns an error.
/// </summary>
public sealed class TypeScriptIngestionException : Exception
{
    /// <summary>
    /// The exit code returned by the sidecar process.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// The combined stderr output from the sidecar process.
    /// </summary>
    public string StderrOutput { get; }

    /// <summary>
    /// Diagnostic error category: sidecar_timeout, parse_error, tsconfig_invalid.
    /// </summary>
    public string? Category { get; }

    public TypeScriptIngestionException(int exitCode, string stderrOutput)
        : base($"TypeScript sidecar exited with code {exitCode}: {stderrOutput}")
    {
        ExitCode = exitCode;
        StderrOutput = stderrOutput;
    }

    /// <summary>
    /// Creates an exception with a structured error category.
    /// </summary>
    public TypeScriptIngestionException(string category, string message)
        : base(message)
    {
        ExitCode = -1;
        StderrOutput = string.Empty;
        Category = category;
    }

    public TypeScriptIngestionException(string message) : base(message)
    {
        ExitCode = -1;
        StderrOutput = string.Empty;
    }

    public TypeScriptIngestionException(string message, Exception innerException) : base(message, innerException)
    {
        ExitCode = -1;
        StderrOutput = string.Empty;
    }
}
