using DocAgent.McpServer.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocAgent.McpServer.Cli;

/// <summary>
/// Builds a minimal DI service provider for CLI commands that require DocAgent services
/// (e.g. <see cref="IIngestionService"/>), without starting hosted services.
/// </summary>
public static class CliServiceProvider
{
    /// <summary>
    /// Builds a configured <see cref="IServiceProvider"/> with DocAgent services registered.
    /// <para>
    /// The host is built but NOT started — this intentionally avoids triggering
    /// <c>NodeAvailabilityValidator</c> (which probes for <c>node</c> on PATH) and other
    /// hosted-service side-effects that are inappropriate in a CLI context.
    /// </para>
    /// </summary>
    /// <param name="artifactsDir">
    /// Path to the artifacts directory. Resolved to an absolute path and created if absent.
    /// </param>
    public static IServiceProvider Build(string artifactsDir)
    {
        var absArtifactsDir = Config.PathExpander.Expand(artifactsDir) ?? Path.GetFullPath(artifactsDir);
        Directory.CreateDirectory(absArtifactsDir);

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                // Only write Warning+ to stderr so normal CLI stdout is clean
                logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Warning);
            })
            .ConfigureServices((_, services) =>
            {
                // Configure BEFORE AddDocAgent() — AddDocAgent captures ArtifactsDir lazily
                // via closure; the IOptions<DocAgentServerOptions> must be resolvable when
                // the closure first fires, which happens on first singleton resolution.
                services.Configure<DocAgentServerOptions>(opts =>
                {
                    opts.ArtifactsDir = absArtifactsDir;
                    opts.ExcludeTestFiles = true;
                });

                services.AddDocAgent();
            })
            .Build();

        // Do NOT call host.StartAsync() — avoids NodeAvailabilityValidator side-effects.
        return host.Services;
    }
}
