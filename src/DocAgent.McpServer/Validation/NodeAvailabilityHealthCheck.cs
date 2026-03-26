using System.Diagnostics;
using DocAgent.McpServer.Config;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Validation;

/// <summary>
/// Health check that reports Node.js availability as Degraded (not Unhealthy) when absent,
/// so /health returns HTTP 200 even without Node.js — allowing Aspire's own health probe to succeed.
/// </summary>
public sealed class NodeAvailabilityHealthCheck : IHealthCheck
{
    private readonly DocAgentServerOptions _options;
    private readonly Func<CancellationToken, Task<string?>>? _versionProvider;

    /// <param name="options">Server options providing the Node.js executable name.</param>
    /// <param name="versionProvider">
    /// Optional injectable version provider for testing. When null, the real process is invoked.
    /// </param>
    public NodeAvailabilityHealthCheck(
        IOptions<DocAgentServerOptions> options,
        Func<CancellationToken, Task<string?>>? versionProvider = null)
    {
        _options = options.Value;
        _versionProvider = versionProvider;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var versionOutput = _versionProvider is not null
                ? await _versionProvider(cancellationToken).ConfigureAwait(false)
                : await GetNodeVersionAsync(cancellationToken).ConfigureAwait(false);

            var check = NodeAvailabilityValidator.ParseNodeVersion(versionOutput);

            if (!check.IsAvailable)
                return HealthCheckResult.Degraded("Node.js is not installed or not in PATH. TypeScript ingestion will not be available.");

            if (!check.IsSupported)
                return HealthCheckResult.Degraded(
                    $"Node.js {check.ParsedVersion} detected but version >= 22.0.0 is required. TypeScript ingestion may fail.");

            return HealthCheckResult.Healthy($"Node.js {check.ParsedVersion}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Node.js check failed: {ex.Message}");
        }
    }

    private async Task<string?> GetNodeVersionAsync(CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.NodeExecutable,
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        if (process == null) return null;

        // Use a combined timeout: 3 seconds (health checks should be fast)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        return process.ExitCode == 0 ? output : null;
    }
}
