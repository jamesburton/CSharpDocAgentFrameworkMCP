using DocAgent.McpServer.Config;
using DocAgent.McpServer.Validation;
using FluentAssertions;
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
}
