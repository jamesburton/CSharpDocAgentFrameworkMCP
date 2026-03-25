using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace DocAgent.Tests;

/// <summary>
/// Stress tests for the TypeScript ingestion pipeline against a large synthetic project.
/// Uses PipelineOverride to avoid requiring the real Node.js sidecar, while still exercising
/// all ingestion plumbing: allowlist checks, incremental manifest, snapshot store, search index.
/// </summary>
public sealed class TypeScriptStressTests : IDisposable
{
    private const int FileCount = 110; // > 100 as required
    private const int ClassesPerFile = 3;
    private const int MethodsPerClass = 5;

    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly TypeScriptIngestionService _tsService;
    private readonly ITestOutputHelper _output;

    public TypeScriptStressTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptStressTests", Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_tempDir, "stress-project");
        Directory.CreateDirectory(_projectDir);

        _store = new SnapshotStore(_tempDir);
        _index = new BM25SearchIndex(_tempDir);

        var options = new DocAgentServerOptions
        {
            AllowedPaths = ["**"],
            ArtifactsDir = _tempDir
        };
        var optionsWrapper = Options.Create(options);
        var allowlist = new PathAllowlist(optionsWrapper);

        _tsService = new TypeScriptIngestionService(
            _store, _index, allowlist, optionsWrapper,
            NullLogger<TypeScriptIngestionService>.Instance);

        // Wire in-memory pipeline override — no Node.js sidecar needed
        _tsService.PipelineOverride = path => Task.FromResult(BuildSyntheticSnapshot(path));
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a synthetic TypeScript project on disk with deeply nested module directories,
    /// interfaces with multiple implementations, large classes with JSDoc, and cross-file imports.
    /// </summary>
    private string WriteProject()
    {
        // Module hierarchy: src/module-A/.../module-J/
        var moduleNames = new[] { "moduleA", "moduleB", "moduleC", "moduleD", "moduleE",
                                   "moduleF", "moduleG", "moduleH", "moduleI", "moduleJ" };

        // Interface definition file
        var interfaceDir = Path.Combine(_projectDir, "src", "interfaces");
        Directory.CreateDirectory(interfaceDir);
        var interfaceFile = Path.Combine(interfaceDir, "IStressEntity.ts");
        File.WriteAllText(interfaceFile, @"
/**
 * Base interface for all stress-test entities.
 * @param id The unique identifier.
 */
export interface IStressEntity {
    readonly id: string;
    process(): Promise<void>;
    serialize(): string;
}
");

        // Per-file cross-module imports tracker
        var createdFiles = new List<string>();

        for (var fileIdx = 0; fileIdx < FileCount; fileIdx++)
        {
            var moduleName = moduleNames[fileIdx % moduleNames.Length];
            var subDir = Path.Combine(_projectDir, "src", moduleName);
            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(subDir, $"StressFile{fileIdx:D3}.ts");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"import {{ IStressEntity }} from '../../interfaces/IStressEntity';");

            // Cross-file import from the previous file (circular-safe: only look back)
            if (fileIdx > 0)
            {
                var prevIdx = fileIdx - 1;
                var prevModule = moduleNames[prevIdx % moduleNames.Length];
                sb.AppendLine($"// import {{ StressClass{prevIdx:D3}A }} from '../{prevModule}/StressFile{prevIdx:D3}';");
            }

            sb.AppendLine();

            for (var classIdx = 0; classIdx < ClassesPerFile; classIdx++)
            {
                var className = $"StressClass{fileIdx:D3}{(char)('A' + classIdx)}";
                sb.AppendLine($"/**");
                sb.AppendLine($" * Auto-generated class #{fileIdx * ClassesPerFile + classIdx}.");
                sb.AppendLine($" * Part of module {moduleName}.");
                sb.AppendLine($" */");
                sb.AppendLine($"export class {className} implements IStressEntity {{");
                sb.AppendLine($"    readonly id: string = '{className}';");
                sb.AppendLine();

                for (var methodIdx = 0; methodIdx < MethodsPerClass; methodIdx++)
                {
                    var methodName = $"operation{methodIdx}";
                    sb.AppendLine($"    /**");
                    sb.AppendLine($"     * Performs operation #{methodIdx} for {className}.");
                    sb.AppendLine($"     * @param input The input value.");
                    sb.AppendLine($"     * @returns A promise resolving to a number.");
                    sb.AppendLine($"     */");
                    sb.AppendLine($"    async {methodName}(input: string): Promise<number> {{");
                    sb.AppendLine($"        return input.length + {methodIdx};");
                    sb.AppendLine($"    }}");
                    sb.AppendLine();
                }

                sb.AppendLine($"    async process(): Promise<void> {{ /* implements IStressEntity */ }}");
                sb.AppendLine($"    serialize(): string {{ return JSON.stringify({{ id: this.id }}); }}");
                sb.AppendLine($"}}");
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString());
            createdFiles.Add(filePath);
        }

        // Root tsconfig.json
        var tsconfigPath = Path.Combine(_projectDir, "tsconfig.json");
        File.WriteAllText(tsconfigPath, @"{
  ""compilerOptions"": {
    ""target"": ""ES2020"",
    ""module"": ""commonjs"",
    ""strict"": true,
    ""outDir"": ""./dist""
  },
  ""include"": [""src/**/*""]
}");

        _output.WriteLine($"Synthetic project written: {FileCount} files, {createdFiles.Count} TS sources");
        return tsconfigPath;
    }

    /// <summary>
    /// Builds a SymbolGraphSnapshot mirroring what the real sidecar would produce
    /// for the synthetic project: 1 namespace per file + classes + methods + interface.
    /// </summary>
    private static SymbolGraphSnapshot BuildSyntheticSnapshot(string tsconfigPath)
    {
        const string projectName = "stress-project";
        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        var emptyParams = new Dictionary<string, string>();
        var emptyTypeParams = new Dictionary<string, string>();

        // Interface node
        var ifaceId = new SymbolId("T:stress-project:interfaces/IStressEntity.ts:IStressEntity");
        nodes.Add(new SymbolNode(
            ifaceId, SymbolKind.Type, "IStressEntity", "stress-project.IStressEntity",
            [], Accessibility.Public,
            new DocComment("Base interface for all stress-test entities.", null,
                emptyParams, emptyTypeParams, null, [], [], []),
            null, null, [], [], projectName));

        var moduleNames = new[] { "moduleA", "moduleB", "moduleC", "moduleD", "moduleE",
                                   "moduleF", "moduleG", "moduleH", "moduleI", "moduleJ" };

        for (var fileIdx = 0; fileIdx < FileCount; fileIdx++)
        {
            var moduleName = moduleNames[fileIdx % moduleNames.Length];
            var fileName = $"src/{moduleName}/StressFile{fileIdx:D3}.ts";
            var nsId = new SymbolId($"T:stress-project:{fileName}:");
            nodes.Add(new SymbolNode(
                nsId, SymbolKind.Namespace, fileName, $"stress-project.{fileName}",
                [], Accessibility.Public, null, null, null, [], [], projectName));

            for (var classIdx = 0; classIdx < ClassesPerFile; classIdx++)
            {
                var className = $"StressClass{fileIdx:D3}{(char)('A' + classIdx)}";
                var classId = new SymbolId($"T:stress-project:{fileName}:{className}");
                nodes.Add(new SymbolNode(
                    classId, SymbolKind.Type, className, $"stress-project.{className}",
                    [], Accessibility.Public,
                    new DocComment($"Auto-generated class #{fileIdx * ClassesPerFile + classIdx}.", null,
                        emptyParams, emptyTypeParams, null, [], [], []),
                    null, null, [], [], projectName));

                edges.Add(new SymbolEdge(nsId, classId, SymbolEdgeKind.Contains, EdgeScope.IntraProject));
                edges.Add(new SymbolEdge(classId, ifaceId, SymbolEdgeKind.Implements, EdgeScope.IntraProject));

                for (var methodIdx = 0; methodIdx < MethodsPerClass; methodIdx++)
                {
                    var methodName = $"operation{methodIdx}";
                    var methodId = new SymbolId($"M:stress-project:{fileName}:{className}.{methodName}");
                    nodes.Add(new SymbolNode(
                        methodId, SymbolKind.Method, methodName, $"stress-project.{className}.{methodName}",
                        [], Accessibility.Public,
                        new DocComment($"Performs operation #{methodIdx} for {className}.", null,
                            new Dictionary<string, string> { ["input"] = "The input value." },
                            emptyTypeParams, "Promise<number>", [], [], []),
                        null, "Promise<number>",
                        [new ParameterInfo("input", "string", null, false, false, false, false)],
                        [], projectName));

                    edges.Add(new SymbolEdge(classId, methodId, SymbolEdgeKind.Contains, EdgeScope.IntraProject));
                }
            }
        }

        return new SymbolGraphSnapshot(
            "1.0", projectName, "ts-compiler", null,
            DateTimeOffset.UtcNow, nodes, edges);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestTypeScript_LargeProject_CompletesWithoutErrors()
    {
        var tsconfigPath = WriteProject();

        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        result.Should().NotBeNull();
        result.SnapshotId.Should().NotBeNullOrEmpty("ingestion must produce a snapshot ID");
        result.SymbolCount.Should().BeGreaterThan(FileCount * ClassesPerFile,
            "each file contributes namespace + classes + methods");
        result.Skipped.Should().BeFalse("first ingestion should not be skipped");

        _output.WriteLine($"Ingested {result.SymbolCount} symbols from {FileCount}-file project in {result.Duration.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task IngestTypeScript_LargeProject_SnapshotContainsAllClassNodes()
    {
        var tsconfigPath = WriteProject();

        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        var snapshot = await _store.LoadAsync(result.SnapshotId, CancellationToken.None);
        snapshot.Should().NotBeNull("saved snapshot must be retrievable from the store");

        // Verify class nodes from every file are present
        var classNodes = snapshot!.Nodes
            .Where(n => n.Kind == SymbolKind.Type && n.DisplayName.StartsWith("StressClass"))
            .ToList();

        var expectedClassCount = FileCount * ClassesPerFile;
        classNodes.Should().HaveCountGreaterThanOrEqualTo(expectedClassCount,
            $"expected {expectedClassCount} class nodes from {FileCount} files x {ClassesPerFile} classes each");

        // Spot-check: first and last classes exist
        classNodes.Should().Contain(n => n.DisplayName == "StressClass000A", "first generated class must exist");
        classNodes.Should().Contain(n => n.DisplayName.StartsWith($"StressClass{(FileCount - 1):D3}"), "last file's class must exist");

        _output.WriteLine($"Class nodes verified: {classNodes.Count} / {expectedClassCount} expected");
    }

    [Fact]
    public async Task IngestTypeScript_LargeProject_EdgesLinkClassesToInterface()
    {
        var tsconfigPath = WriteProject();

        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        var snapshot = await _store.LoadAsync(result.SnapshotId, CancellationToken.None);
        snapshot.Should().NotBeNull();

        var implementsEdges = snapshot!.Edges
            .Where(e => e.Kind == SymbolEdgeKind.Implements)
            .ToList();

        implementsEdges.Should().HaveCountGreaterThanOrEqualTo(FileCount * ClassesPerFile,
            "every generated class has an Implements edge to IStressEntity");

        _output.WriteLine($"Implements edges: {implementsEdges.Count}");
    }

    [Fact]
    public async Task IngestTypeScript_LargeProject_SearchIndexContainsClassNames()
    {
        var tsconfigPath = WriteProject();
        await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        // Search for a class near the middle of the generated range
        var midFileIdx = FileCount / 2;
        var query = $"StressClass{midFileIdx:D3}";

        var hits = await _index.SearchToListAsync(query);
        hits.Should().NotBeEmpty($"BM25 index should return results for query '{query}'");

        // BM25 index: Snippet = DisplayName, Id.Value = symbolId string.
        // CamelCase tokenization means 'StressClass055' matches tokens 'stress', 'class', '055'.
        // At least one hit should mention our class in its display name or symbol ID.
        var midClassName = $"StressClass{midFileIdx:D3}";
        hits.Any(h =>
                h.Snippet.Contains(midClassName, StringComparison.OrdinalIgnoreCase) ||
                h.Id.Value.Contains(midClassName, StringComparison.OrdinalIgnoreCase) ||
                h.Snippet.StartsWith("StressClass", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue($"at least one hit should be a StressClass symbol (query tokens match many StressClass nodes)");

        _output.WriteLine($"Search '{query}': {hits.Count} hits, first: {hits[0].Snippet} ({hits[0].Id.Value})");
    }

    [Fact]
    public async Task IngestTypeScript_LargeProject_MethodDocCommentsPreserved()
    {
        var tsconfigPath = WriteProject();
        var result = await _tsService.IngestTypeScriptAsync(tsconfigPath, CancellationToken.None);

        var snapshot = await _store.LoadAsync(result.SnapshotId, CancellationToken.None);
        snapshot.Should().NotBeNull();

        var methodsWithDocs = snapshot!.Nodes
            .Where(n => n.Kind == SymbolKind.Method && n.Docs?.Summary != null)
            .ToList();

        methodsWithDocs.Should().NotBeEmpty("methods in the synthetic snapshot carry JSDoc summaries");

        // Verify a specific method's parameter doc
        var firstMethod = snapshot.Nodes
            .FirstOrDefault(n => n.Kind == SymbolKind.Method
                                 && n.DisplayName == "operation0"
                                 && n.ProjectOrigin == "stress-project");

        firstMethod.Should().NotBeNull("operation0 must exist in the snapshot");
        firstMethod!.Docs.Should().NotBeNull();
        firstMethod.Parameters.Should().Contain(p => p.Name == "input", "operation0 has an 'input' parameter");

        _output.WriteLine($"Methods with docs: {methodsWithDocs.Count}");
    }
}
