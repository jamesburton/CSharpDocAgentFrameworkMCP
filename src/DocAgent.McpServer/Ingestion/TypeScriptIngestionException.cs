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

    public TypeScriptIngestionException(int exitCode, string stderrOutput)
        : base($"TypeScript sidecar exited with code {exitCode}: {stderrOutput}")
    {
        ExitCode = exitCode;
        StderrOutput = stderrOutput;
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
