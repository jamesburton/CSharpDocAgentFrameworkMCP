using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer;

public static class DocAgentServiceCollectionExtensions
{
    public static IServiceCollection AddDocAgent(
        this IServiceCollection services,
        Action<DocAgentServerOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        // Resolve path once via closure — shared between both singletons
        string? resolvedDir = null;
        string GetDir(IServiceProvider sp)
        {
            if (resolvedDir is not null) return resolvedDir;
            var opts = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;
            var raw = string.IsNullOrWhiteSpace(opts.ArtifactsDir) ? "./artifacts" : opts.ArtifactsDir;
            resolvedDir = Path.GetFullPath(raw);
            Directory.CreateDirectory(resolvedDir);  // eager validation — throws on permission errors
            return resolvedDir;
        }

        services.AddSingleton<SnapshotStore>(sp => new SnapshotStore(GetDir(sp)));
        services.AddSingleton<ISearchIndex>(sp => new BM25SearchIndex(GetDir(sp)));
        services.AddScoped<IKnowledgeQueryService, KnowledgeQueryService>();
        services.AddSingleton<IIngestionService, IngestionService>();
        services.AddSingleton<TypeScriptIngestionService>();
        services.AddHostedService<NodeAvailabilityValidator>();
        services.AddSingleton<SolutionIngestionService>();
        services.AddSingleton<ISolutionIngestionService, IncrementalSolutionIngestionService>();

        return services;
    }
}
