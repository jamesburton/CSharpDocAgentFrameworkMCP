using DocAgent.McpServer.Config;
using DocAgent.McpServer.Validation;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

public class NodeAvailabilityHealthCheckTests
{
    private static NodeAvailabilityHealthCheck CreateHealthCheck(
        Func<CancellationToken, Task<string?>>? versionProvider = null,
        string nodeExecutable = "node")
    {
        var options = Options.Create(new DocAgentServerOptions
        {
            NodeExecutable = nodeExecutable
        });
        return new NodeAvailabilityHealthCheck(options, versionProvider);
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Degraded_when_node_not_available()
    {
        // Arrange: version provider returns null (node not found)
        var healthCheck = CreateHealthCheck(versionProvider: _ => Task.FromResult<string?>(null));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Degraded_when_node_version_unsupported()
    {
        // Arrange: version provider returns Node 18 (unsupported — requires >= 22)
        var healthCheck = CreateHealthCheck(versionProvider: _ => Task.FromResult<string?>("v18.0.0"));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("18");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Healthy_when_node_version_supported()
    {
        // Arrange: version provider returns Node 22.11.0 (supported)
        var healthCheck = CreateHealthCheck(versionProvider: _ => Task.FromResult<string?>("v22.11.0"));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("22");
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Degraded_when_version_provider_throws()
    {
        // Arrange: version provider throws (e.g., node binary not on PATH)
        var healthCheck = CreateHealthCheck(versionProvider: _ => throw new Exception("node not found"));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void SidecarDirEnvVar_WhenSet_BindsToDocAgentServerOptions()
    {
        // Arrange: use a path that is already absolute and platform-appropriate
        var expectedDir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar + "docagent-test-sidecar-dir";
        Environment.SetEnvironmentVariable("DOCAGENT_SIDECAR_DIR", expectedDir);
        try
        {
            // Act: replicate the env var pickup logic from McpServer/Program.cs using
            // ConfigurationBuilder + ServiceCollection (no WebApplication needed)
            var sidecarDirFromEnv = DocAgent.McpServer.Config.PathExpander.Expand(
                Environment.GetEnvironmentVariable("DOCAGENT_SIDECAR_DIR"));

            var configDict = new Dictionary<string, string?>();
            if (sidecarDirFromEnv is not null)
                configDict["DocAgent:SidecarDir"] = sidecarDirFromEnv;

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            var services = new ServiceCollection();
            services.Configure<DocAgentServerOptions>(config.GetSection("DocAgent"));
            var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;

            // Assert: env var value flows into DocAgentServerOptions.SidecarDir
            // PathExpander.Expand normalizes the path (resolves . and .. segments)
            options.SidecarDir.Should().NotBeNull();
            options.SidecarDir!.Should().EndWith("docagent-test-sidecar-dir");
        }
        finally
        {
            // Cleanup: always unset the env var
            Environment.SetEnvironmentVariable("DOCAGENT_SIDECAR_DIR", null);
        }
    }
}
