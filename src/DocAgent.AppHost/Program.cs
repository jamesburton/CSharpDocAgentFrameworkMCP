var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR",
        builder.Configuration["DocAgent:ArtifactsDir"] ?? "./artifacts")
    .WithEnvironment("DOCAGENT_ALLOWED_PATHS",
        builder.Configuration["DocAgent:AllowlistPaths"] ?? "")
    .WithHttpEndpoint(port: 8089, name: "health")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
