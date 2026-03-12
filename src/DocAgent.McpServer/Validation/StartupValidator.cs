using DocAgent.McpServer.Config;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.Validation;

/// <summary>Result of startup configuration validation.</summary>
public sealed record ValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True when no errors were found (warnings are non-fatal).</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates server configuration at startup and shuts down the host on fatal errors.
/// Runs in <see cref="IHostedLifecycleService.StartingAsync"/> — before any hosted service
/// starts, ensuring the MCP transport never accepts tool calls with invalid config.
/// </summary>
public sealed class StartupValidator : IHostedLifecycleService
{
    private readonly DocAgentServerOptions _options;
    private readonly ILogger<StartupValidator> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public StartupValidator(
        IOptions<DocAgentServerOptions> options,
        ILogger<StartupValidator> logger,
        IHostApplicationLifetime lifetime)
    {
        _options = options.Value;
        _logger = logger;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Pure validation function. No DI, no host — just options in, result out.
    /// </summary>
    public static ValidationResult Validate(DocAgentServerOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. AllowedPaths check — warning only (PathAllowlist defaults to cwd)
        if (options.AllowedPaths.Length == 0 &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS")))
        {
            warnings.Add(
                "AllowedPaths is empty and DOCAGENT_ALLOWED_PATHS is not set. PathAllowlist will default to cwd only.");
        }

        // 2. MSBuild check — fatal error if no instances found
        if (!MSBuildLocator.IsRegistered)
        {
            if (!MSBuildLocator.QueryVisualStudioInstances().Any())
            {
                errors.Add("No MSBuild instances found. Please install the .NET SDK or Visual Studio with build tools.");
            }
        }

        // 3. ArtifactsDir null/empty check — fatal error
        if (string.IsNullOrWhiteSpace(options.ArtifactsDir))
        {
            errors.Add(
                "ArtifactsDir is not configured. Set DocAgent:ArtifactsDir in appsettings.json or DOCAGENT_ARTIFACTS_DIR environment variable.");
        }
        else
        {
            // 4. ArtifactsDir writability probe — fatal error if not writable
            try
            {
                var artifactsDir = options.ArtifactsDir;
                Directory.CreateDirectory(artifactsDir);
                var probePath = Path.Combine(artifactsDir, ".startup-probe");
                File.WriteAllText(probePath, "probe");
                File.Delete(probePath);
            }
            catch (IOException ex)
            {
                errors.Add($"ArtifactsDir '{options.ArtifactsDir}' is not writable: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add($"ArtifactsDir '{options.ArtifactsDir}' is not writable: {ex.Message}");
            }
        }

        return new ValidationResult(errors, warnings);
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var result = Validate(_options);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("{Warning}", warning);
        }

        foreach (var error in result.Errors)
        {
            _logger.LogError("{Error}", error);
        }

        if (!result.IsValid)
        {
            _logger.LogCritical("Startup validation failed. Server will not accept tool calls.");
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
