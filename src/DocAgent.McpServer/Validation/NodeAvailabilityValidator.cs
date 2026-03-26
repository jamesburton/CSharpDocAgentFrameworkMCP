using System.Diagnostics;
using DocAgent.McpServer.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Validation;

public record NodeCheckResult(bool IsAvailable, bool IsSupported, Version? ParsedVersion);
public record SidecarBuildResult(bool NeedsBuild, string DistPath);

/// <summary>
/// Checks if Node.js is available at startup and builds the TypeScript sidecar if needed.
/// Non-fatal: logs warnings if Node.js is missing but doesn't stop the application.
/// </summary>
public sealed class NodeAvailabilityValidator : IHostedLifecycleService
{
    private readonly DocAgentServerOptions _options;
    private readonly ILogger<NodeAvailabilityValidator> _logger;

    public NodeAvailabilityValidator(
        IOptions<DocAgentServerOptions> options,
        ILogger<NodeAvailabilityValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public static NodeCheckResult ParseNodeVersion(string? versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput))
            return new NodeCheckResult(false, false, null);

        var versionString = versionOutput.Trim().TrimStart('v');
        if (Version.TryParse(versionString, out var version))
        {
            return new NodeCheckResult(true, version.Major >= 22, version);
        }

        return new NodeCheckResult(false, false, null);
    }

    internal static SidecarBuildResult CheckSidecarBuild(string sidecarDir)
    {
        var distPath = Path.Combine(sidecarDir, "dist", "index.js");
        return new SidecarBuildResult(!File.Exists(distPath), distPath);
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var nodeVersionOutput = await GetNodeVersionAsync(cancellationToken).ConfigureAwait(false);
        var nodeCheck = ParseNodeVersion(nodeVersionOutput);

        if (!nodeCheck.IsAvailable)
        {
            _logger.LogWarning("Node.js is not installed or not in PATH. TypeScript ingestion will not be available.");
            return;
        }

        if (!nodeCheck.IsSupported)
        {
            _logger.LogWarning("Node.js {Version} detected, but version >= 22.0.0 is required. TypeScript ingestion may fail.", nodeCheck.ParsedVersion);
        }
        else
        {
            _logger.LogInformation("Node.js {Version} detected.", nodeCheck.ParsedVersion);
        }

        var sidecarDir = _options.SidecarDir ?? Path.Combine(AppContext.BaseDirectory, "src", "ts-symbol-extractor");
        if (!Directory.Exists(sidecarDir))
        {
            var devPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "ts-symbol-extractor"));
            if (Directory.Exists(devPath)) sidecarDir = devPath;
        }

        if (Directory.Exists(sidecarDir))
        {
            var buildCheck = CheckSidecarBuild(sidecarDir);
            if (buildCheck.NeedsBuild)
            {
                _logger.LogInformation("Building TypeScript sidecar (first run)...");
                try
                {
                    await BuildSidecarAsync(sidecarDir, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("TypeScript sidecar built successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to build TypeScript sidecar. TypeScript ingestion will not be available until 'npm install && npm run build' is run manually in {SidecarDir}.", sidecarDir);
                }
            }
        }
        else
        {
            _logger.LogWarning("TypeScript sidecar directory not found at {SidecarDir}. TypeScript ingestion will not be available.", sidecarDir);
        }
    }

    private async Task<string?> GetNodeVersionAsync(CancellationToken ct)
    {
        try
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
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task BuildSidecarAsync(string sidecarDir, CancellationToken ct)
    {
        var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        
        // npm install
        var installInfo = new ProcessStartInfo
        {
            FileName = npm,
            Arguments = "install --silent",
            WorkingDirectory = sidecarDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var installProcess = Process.Start(installInfo);
        if (installProcess == null) throw new Exception("Failed to start npm install.");
        await installProcess.WaitForExitAsync(ct).ConfigureAwait(false);
        if (installProcess.ExitCode != 0) throw new Exception($"npm install failed with exit code {installProcess.ExitCode}.");

        // npm run build
        var buildInfo = new ProcessStartInfo
        {
            FileName = npm,
            Arguments = "run build",
            WorkingDirectory = sidecarDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var buildProcess = Process.Start(buildInfo);
        if (buildProcess == null) throw new Exception("Failed to start npm run build.");
        await buildProcess.WaitForExitAsync(ct).ConfigureAwait(false);
        if (buildProcess.ExitCode != 0) throw new Exception($"npm run build failed with exit code {buildProcess.ExitCode}.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
