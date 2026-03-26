var builder = DistributedApplication.CreateBuilder(args);

// Resolve sidecar directory relative to AppHost project directory
var sidecarDir = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "ts-symbol-extractor"));

// Register sidecar as Aspire resource — no WaitFor (parallel start, graceful degradation)
// The McpServer's /health endpoint reports Degraded (not Unhealthy) when Node.js is absent,
// so the Aspire dashboard health probe always returns HTTP 200.
var sidecar = builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js");

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR",
        builder.Configuration["DocAgent:ArtifactsDir"] ?? "./artifacts")
    .WithEnvironment("DOCAGENT_ALLOWED_PATHS",
        builder.Configuration["DocAgent:AllowlistPaths"] ?? "")
    .WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)
    .WithReference(sidecar)
    .WithHttpEndpoint(port: 8089, name: "health")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
