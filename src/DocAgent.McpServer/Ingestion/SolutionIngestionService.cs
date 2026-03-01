using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using CoreSymbolKind = DocAgent.Core.SymbolKind;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Implements <see cref="ISolutionIngestionService"/> by opening a .sln via MSBuildWorkspace,
/// processing each C# project sequentially (MSBuildWorkspace is not thread-safe), and producing
/// a flat merged <see cref="SymbolGraphSnapshot"/> stamped with per-project <c>ProjectOrigin</c>.
/// Also builds a <see cref="SolutionSnapshot"/> with the project dependency DAG, cross-project
/// edge classification, and stub nodes for external type references.
/// </summary>
public sealed class SolutionIngestionService : ISolutionIngestionService
{
    private readonly SnapshotStore _store;
    private readonly ISearchIndex _index;
    private readonly ILogger<SolutionIngestionService> _logger;

    // Optional injectable pipeline for testing. When set, bypasses real MSBuild entirely.
    // Receives (slnPath, warnings, ct) and returns the final SolutionIngestionResult.
    internal Func<string, List<string>, CancellationToken, Task<SolutionIngestionResult>>? PipelineOverride { get; set; }

    // ── Primitive filter ─────────────────────────────────────────────────────
    // Common types that should NOT generate stub nodes to avoid noise.
    private static readonly HashSet<string> s_primitiveTypeNames = new(StringComparer.Ordinal)
    {
        "System.Object",
        "System.String",
        "System.Boolean",
        "System.Byte",
        "System.SByte",
        "System.Int16",
        "System.UInt16",
        "System.Int32",
        "System.UInt32",
        "System.Int64",
        "System.UInt64",
        "System.Single",
        "System.Double",
        "System.Decimal",
        "System.Char",
        "System.Void",
        "System.IntPtr",
        "System.UIntPtr",
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IDictionary`2",
        "System.ValueType",
        "System.Enum",
        "System.Attribute",
        "System.Exception",
        "System.IDisposable",
        "System.EventArgs",
    };

    public SolutionIngestionService(
        SnapshotStore store,
        ISearchIndex index,
        ILogger<SolutionIngestionService> logger)
    {
        _store = store;
        _index = index;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SolutionIngestionResult> IngestAsync(
        string slnPath,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        // PipelineOverride seam: used in unit tests to avoid real MSBuild.
        if (PipelineOverride is not null)
        {
            return await PipelineOverride(slnPath, warnings, cancellationToken).ConfigureAwait(false);
        }

        var solutionName = Path.GetFileNameWithoutExtension(slnPath);
        var statuses = new List<ProjectIngestionStatus>();
        var allNodes = new List<SymbolNode>();
        var allEdges = new List<SymbolEdge>();

        // SolutionSnapshot accumulators
        var projectEntries = new List<ProjectEntry>();
        var projectEdges = new List<ProjectEdge>();
        var perProjectSnapshots = new List<SymbolGraphSnapshot>();

        // Stub node accumulators — shared across all projects to deduplicate across project boundaries
        var stubNodes = new List<SymbolNode>();
        var seenStubIds = new HashSet<string>(StringComparer.Ordinal);

        // Stage 1 — Open solution
        if (reportProgress is not null)
            await reportProgress(1, 4, "Opening solution...").ConfigureAwait(false);

        Solution solution;
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
            warnings.Add($"MSBuildWorkspace [{args.Diagnostic.Kind}]: {args.Diagnostic.Message}");

        try
        {
            solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open solution: {SlnPath}", slnPath);
            warnings.Add($"Failed to open solution '{slnPath}': {ex.Message}");
            sw.Stop();
            return new SolutionIngestionResult(
                SnapshotId: string.Empty,
                SolutionName: solutionName,
                TotalProjectCount: 0,
                IngestedProjectCount: 0,
                TotalNodeCount: 0,
                TotalEdgeCount: 0,
                Duration: sw.Elapsed,
                Projects: Array.Empty<ProjectIngestionStatus>(),
                Warnings: warnings.AsReadOnly());
        }

        // Stage 2 — Deduplicate multi-targeted projects and process
        if (reportProgress is not null)
            await reportProgress(2, 4, "Processing projects...").ConfigureAwait(false);

        // Group projects by FilePath (case-insensitive) to detect multi-targeted duplicates.
        // Projects with the same file path represent different TFMs (e.g., net9.0, net10.0).
        var projectGroups = solution.Projects
            .GroupBy(p => p.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosenProjects = new List<Project>();
        var tfmChoices = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in projectGroups)
        {
            var candidates = group.ToList();
            if (candidates.Count == 1)
            {
                chosenProjects.Add(candidates[0]);
                tfmChoices[candidates[0].FilePath ?? string.Empty] = null;
            }
            else
            {
                // Pick the project with the highest TFM version.
                var best = candidates
                    .OrderByDescending(p => ExtractTfmVersion(p.Name))
                    .First();
                chosenProjects.Add(best);

                // Record the chosen TFM (extract from trailing "(netX.Y)" in project name)
                var tfmMatch = Regex.Match(best.Name, @"\(([^)]+)\)$");
                tfmChoices[best.FilePath ?? string.Empty] = tfmMatch.Success ? tfmMatch.Groups[1].Value : null;
            }
        }

        // Build a lookup: ProjectId → projectName (using FilePath-based name for stability)
        var projectIdToName = new Dictionary<ProjectId, string>();
        foreach (var p in chosenProjects)
        {
            var filePath = p.FilePath ?? string.Empty;
            var name = string.IsNullOrEmpty(filePath)
                ? p.Name
                : Path.GetFileNameWithoutExtension(filePath);
            projectIdToName[p.Id] = name;
        }

        // Build the set of all solution project names for scope classification
        var solutionProjectNames = new HashSet<string>(
            projectIdToName.Values,
            StringComparer.OrdinalIgnoreCase);

        var parser = new XmlDocParser();
        var resolver = new InheritDocResolver();
        var builder = new RoslynSymbolGraphBuilder(parser, resolver, logWarning: w => warnings.Add(w));

        foreach (var project in chosenProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = project.FilePath ?? string.Empty;
            var projectName = string.IsNullOrEmpty(filePath)
                ? project.Name
                : Path.GetFileNameWithoutExtension(filePath);
            var chosenTfm = tfmChoices.GetValueOrDefault(filePath);

            // Filter: must have a file path
            if (string.IsNullOrEmpty(filePath))
            {
                statuses.Add(new ProjectIngestionStatus(
                    Name: project.Name,
                    FilePath: string.Empty,
                    Status: "skipped",
                    Reason: "No file path",
                    NodeCount: null,
                    ChosenTfm: chosenTfm));
                continue;
            }

            // Filter: must be a C# project
            if (project.Language != LanguageNames.CSharp)
            {
                statuses.Add(new ProjectIngestionStatus(
                    Name: projectName,
                    FilePath: filePath,
                    Status: "skipped",
                    Reason: $"Unsupported language: {project.Language}",
                    NodeCount: null,
                    ChosenTfm: chosenTfm));
                continue;
            }

            // Attempt to compile
            Compilation? compilation;
            try
            {
                compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to compile project: {Project}", projectName);
                statuses.Add(new ProjectIngestionStatus(
                    Name: projectName,
                    FilePath: filePath,
                    Status: "failed",
                    Reason: $"Compilation exception: {ex.Message}",
                    NodeCount: null,
                    ChosenTfm: chosenTfm));
                continue;
            }

            if (compilation is null)
            {
                statuses.Add(new ProjectIngestionStatus(
                    Name: projectName,
                    FilePath: filePath,
                    Status: "failed",
                    Reason: "Could not obtain compilation",
                    NodeCount: null,
                    ChosenTfm: chosenTfm));
                continue;
            }

            // Walk the namespace tree via the shared builder
            var projectNodes = new List<SymbolNode>();
            var projectEdgesLocal = new List<SymbolEdge>();
            var ctx = new ProjectWalkContext(projectName, solutionProjectNames, stubNodes, seenStubIds);
            await WalkCompilationAsync(builder, compilation, projectNodes, projectEdgesLocal, in ctx, cancellationToken)
                .ConfigureAwait(false);

            // Stamp ProjectOrigin on every node
            var stampedNodes = projectNodes
                .Select(n => n with { ProjectOrigin = projectName })
                .ToList();

            allNodes.AddRange(stampedNodes);
            allEdges.AddRange(projectEdgesLocal);

            // Build project dependency info from Roslyn ProjectReferences
            var directDeps = new List<string>();
            foreach (var projRef in project.ProjectReferences)
            {
                if (projectIdToName.TryGetValue(projRef.ProjectId, out var depName))
                {
                    directDeps.Add(depName);
                    projectEdges.Add(new ProjectEdge(projectName, depName));
                }
            }

            projectEntries.Add(new ProjectEntry(projectName, filePath, directDeps.AsReadOnly()));

            // Build per-project SymbolGraphSnapshot
            var perProjectSnapshot = new SymbolGraphSnapshot(
                SchemaVersion: "1.2",
                ProjectName: projectName,
                SourceFingerprint: ComputeFingerprint(filePath),
                ContentHash: null,
                CreatedAt: DateTimeOffset.UtcNow,
                Nodes: SymbolSorter.SortNodes(stampedNodes),
                Edges: SymbolSorter.SortEdges(projectEdgesLocal),
                IngestionMetadata: null,
                SolutionName: solutionName);
            perProjectSnapshots.Add(perProjectSnapshot);

            statuses.Add(new ProjectIngestionStatus(
                Name: projectName,
                FilePath: filePath,
                Status: "ok",
                Reason: null,
                NodeCount: stampedNodes.Count,
                ChosenTfm: chosenTfm));
        }

        // Detect circular references in project DAG
        var cycles = DetectCycles(projectEdges);
        foreach (var cycle in cycles)
        {
            var cycleStr = string.Join(" -> ", cycle);
            _logger.LogWarning("Circular project reference detected: {Cycle}", cycleStr);
            warnings.Add($"Circular project reference detected: {cycleStr}");
        }

        // Add stub nodes to the all-nodes collection
        allNodes.AddRange(stubNodes);

        // Stage 3 — Merge and save
        if (reportProgress is not null)
            await reportProgress(3, 4, "Saving snapshot...").ConfigureAwait(false);

        var fingerprint = ComputeFingerprint(slnPath);
        var snapshot = new SymbolGraphSnapshot(
            SchemaVersion: "1.2",
            ProjectName: solutionName,
            SourceFingerprint: fingerprint,
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: SymbolSorter.SortNodes(allNodes),
            Edges: SymbolSorter.SortEdges(allEdges),
            IngestionMetadata: null,
            SolutionName: solutionName);

        var saved = await _store.SaveAsync(snapshot, ct: cancellationToken).ConfigureAwait(false);

        // Stage 4 — Index (soft failure — snapshot already saved)
        if (reportProgress is not null)
            await reportProgress(4, 4, "Indexing...").ConfigureAwait(false);

        try
        {
            await _index.IndexAsync(saved, cancellationToken, forceReindex: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Index update failed for solution snapshot {SnapshotId} — snapshot was saved but index may be stale",
                saved.ContentHash);
            warnings.Add($"Index update failed: {ex.Message}");
        }

        sw.Stop();

        var ingestedCount = statuses.Count(s => s.Status == "ok");

        // Build SolutionSnapshot
        var solutionSnapshot = new SolutionSnapshot(
            SolutionName: solutionName,
            Projects: projectEntries.AsReadOnly(),
            ProjectDependencies: projectEdges.AsReadOnly(),
            ProjectSnapshots: perProjectSnapshots.AsReadOnly(),
            CreatedAt: DateTimeOffset.UtcNow);

        return new SolutionIngestionResult(
            SnapshotId: saved.ContentHash ?? saved.SourceFingerprint,
            SolutionName: solutionName,
            TotalProjectCount: projectGroups.Count,
            IngestedProjectCount: ingestedCount,
            TotalNodeCount: saved.Nodes.Count,
            TotalEdgeCount: saved.Edges.Count,
            Duration: sw.Elapsed,
            Projects: statuses.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            Snapshot: solutionSnapshot);
    }

    // ── Walk compilation namespace tree via builder reflection ───────────────
    // We invoke the internal walking logic by using the builder's ProcessProject seam.
    // Since RoslynSymbolGraphBuilder.WalkNamespace is private, we call BuildAsync on a
    // temporary single-project inventory — but for solution ingestion we already have a
    // compiled Compilation. We use a helper that directly walks the global namespace.

    private static Task WalkCompilationAsync(
        RoslynSymbolGraphBuilder builder,
        Compilation compilation,
        List<SymbolNode> nodes,
        List<SymbolEdge> edges,
        in ProjectWalkContext ctx,
        CancellationToken ct)
    {
        // Walk via public BuildAsync is not available per-compilation.
        // Use direct namespace walk by calling the builder on a dummy inventory.
        // Since we own the compilation, delegate to a local walk using reflection or
        // duplicate the walk inline. We duplicate the walk here to keep coupling minimal
        // and avoid reflection.
        WalkNamespaceInline(compilation.GlobalNamespace, nodes, edges, in ctx);
        return Task.CompletedTask;
    }

    private static void WalkNamespaceInline(
        INamespaceSymbol ns,
        List<SymbolNode> nodes,
        List<SymbolEdge> edges,
        in ProjectWalkContext ctx)
    {
        var members = ns.GetMembers()
            .OrderBy(m => m.GetDocumentationCommentId() ?? m.ToDisplayString(), StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (member is INamespaceSymbol childNs)
            {
                WalkNamespaceInline(childNs, nodes, edges, in ctx);
            }
            else if (member is INamedTypeSymbol typeSymbol)
            {
                if (!IsIncluded(typeSymbol.DeclaredAccessibility))
                    continue;

                var typeNode = CreateSimpleNode(typeSymbol);
                nodes.Add(typeNode);
                WalkTypeInline(typeSymbol, typeNode.Id, nodes, edges, in ctx);
            }
        }
    }

    private static void WalkTypeInline(
        INamedTypeSymbol type,
        SymbolId typeId,
        List<SymbolNode> nodes,
        List<SymbolEdge> edges,
        in ProjectWalkContext ctx)
    {
        // Inheritance edge
        if (type.BaseType is not null && type.BaseType.SpecialType != SpecialType.System_Object)
        {
            var baseId = GetSymbolId(type.BaseType);
            var scope = ClassifyScope(type.BaseType, ctx.ProjectName, ctx.SolutionProjectNames);
            edges.Add(new SymbolEdge(typeId, baseId, SymbolEdgeKind.Inherits, scope));
            if (scope == EdgeScope.External)
                MaybeAddStubNode(type.BaseType, in ctx);
        }

        // Interface edges
        foreach (var iface in type.Interfaces)
        {
            var scope = ClassifyScope(iface, ctx.ProjectName, ctx.SolutionProjectNames);
            edges.Add(new SymbolEdge(typeId, GetSymbolId(iface), SymbolEdgeKind.Implements, scope));
            if (scope == EdgeScope.External)
                MaybeAddStubNode(iface, in ctx);
        }

        // Nested types
        foreach (var nested in type.GetTypeMembers()
                     .Where(t => IsIncluded(t.DeclaredAccessibility))
                     .OrderBy(t => t.GetDocumentationCommentId() ?? t.ToDisplayString(), StringComparer.Ordinal))
        {
            var nestedNode = CreateSimpleNode(nested);
            nodes.Add(nestedNode);
            edges.Add(new SymbolEdge(typeId, nestedNode.Id, SymbolEdgeKind.Contains));
            WalkTypeInline(nested, nestedNode.Id, nodes, edges, in ctx);
        }

        // Members
        foreach (var member in type.GetMembers()
                     .Where(m => m is not INamedTypeSymbol && IsIncluded(m.DeclaredAccessibility) && !m.IsImplicitlyDeclared)
                     .OrderBy(m => m.GetDocumentationCommentId() ?? m.ToDisplayString(), StringComparer.Ordinal))
        {
            var memberNode = CreateSimpleNode(member);
            nodes.Add(memberNode);
            edges.Add(new SymbolEdge(typeId, memberNode.Id, SymbolEdgeKind.Contains));
        }
    }

    // ── Edge scope classification ────────────────────────────────────────────

    private static EdgeScope ClassifyScope(
        ITypeSymbol targetType,
        string currentProjectName,
        HashSet<string> solutionProjectNames)
    {
        var assemblyName = targetType.ContainingAssembly?.Name;
        if (assemblyName is null)
            return EdgeScope.External;
        if (!solutionProjectNames.Contains(assemblyName))
            return EdgeScope.External;
        if (!string.Equals(assemblyName, currentProjectName, StringComparison.OrdinalIgnoreCase))
            return EdgeScope.CrossProject;
        return EdgeScope.IntraProject;
    }

    // ── Stub node synthesis ──────────────────────────────────────────────────

    private static bool IsPrimitive(ITypeSymbol type)
    {
        var fqn = type.OriginalDefinition.ToDisplayString();
        return s_primitiveTypeNames.Contains(fqn);
    }

    private static void MaybeAddStubNode(ITypeSymbol externalType, in ProjectWalkContext ctx)
    {
        if (IsPrimitive(externalType))
            return;

        var originalDef = externalType.OriginalDefinition;
        var id = GetSymbolId(originalDef);

        // Deduplicate across projects
        if (!ctx.SeenStubIds.Add(id.Value))
            return;

        var stubNode = new SymbolNode(
            Id: id,
            Kind: CoreSymbolKind.Type,
            DisplayName: originalDef.Name,
            FullyQualifiedName: originalDef.ToDisplayString(),
            PreviousIds: Array.Empty<SymbolId>(),
            Accessibility: Core.Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            ProjectOrigin: externalType.ContainingAssembly?.Name,
            NodeKind: NodeKind.Stub);

        ctx.StubNodes.Add(stubNode);
    }

    // ── Cycle detection ──────────────────────────────────────────────────────

    private static List<List<string>> DetectCycles(IReadOnlyList<ProjectEdge> edges)
    {
        // Build adjacency list
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!graph.TryGetValue(edge.From, out var neighbors))
            {
                neighbors = new List<string>();
                graph[edge.From] = neighbors;
            }
            neighbors.Add(edge.To);
        }

        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (!visited.Contains(node))
                DfsCycleDetect(node, graph, visited, inStack, path, cycles);
        }

        return cycles;
    }

    private static void DfsCycleDetect(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        List<List<string>> cycles)
    {
        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DfsCycleDetect(neighbor, graph, visited, inStack, path, cycles);
                }
                else if (inStack.Contains(neighbor))
                {
                    // Found a cycle — extract the cycle path
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = new List<string>(path.Skip(cycleStart));
                        cycle.Add(neighbor); // close the cycle
                        cycles.Add(cycle);
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SymbolNode CreateSimpleNode(ISymbol symbol)
    {
        var id = GetSymbolId(symbol);
        return new SymbolNode(
            Id: id,
            Kind: MapKind(symbol),
            DisplayName: symbol.Name,
            FullyQualifiedName: symbol.ToDisplayString(),
            PreviousIds: Array.Empty<SymbolId>(),
            Accessibility: MapAccessibility(symbol.DeclaredAccessibility),
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());
    }

    private static SymbolId GetSymbolId(ISymbol symbol)
        => new(symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString());

    private static bool IsIncluded(Microsoft.CodeAnalysis.Accessibility accessibility)
        => accessibility is Microsoft.CodeAnalysis.Accessibility.Public
            or Microsoft.CodeAnalysis.Accessibility.Protected
            or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal;

    private static Core.Accessibility MapAccessibility(Microsoft.CodeAnalysis.Accessibility accessibility)
        => accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => Core.Accessibility.Public,
            Microsoft.CodeAnalysis.Accessibility.Protected => Core.Accessibility.Protected,
            Microsoft.CodeAnalysis.Accessibility.Internal => Core.Accessibility.Internal,
            Microsoft.CodeAnalysis.Accessibility.Private => Core.Accessibility.Private,
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => Core.Accessibility.ProtectedInternal,
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => Core.Accessibility.PrivateProtected,
            _ => Core.Accessibility.Private
        };

    private static CoreSymbolKind MapKind(ISymbol symbol)
        => symbol switch
        {
            INamespaceSymbol => CoreSymbolKind.Namespace,
            INamedTypeSymbol t => t.TypeKind switch
            {
                TypeKind.Delegate => CoreSymbolKind.Delegate,
                _ => CoreSymbolKind.Type
            },
            IMethodSymbol m => m.MethodKind switch
            {
                MethodKind.Constructor or MethodKind.StaticConstructor => CoreSymbolKind.Constructor,
                MethodKind.Destructor => CoreSymbolKind.Destructor,
                MethodKind.UserDefinedOperator or MethodKind.Conversion => CoreSymbolKind.Operator,
                _ => CoreSymbolKind.Method
            },
            IPropertySymbol p => p.IsIndexer ? CoreSymbolKind.Indexer : CoreSymbolKind.Property,
            IFieldSymbol f => f.ContainingType?.TypeKind == TypeKind.Enum
                ? CoreSymbolKind.EnumMember
                : CoreSymbolKind.Field,
            IEventSymbol => CoreSymbolKind.Event,
            _ => CoreSymbolKind.Method
        };

    // ── TFM version extraction ───────────────────────────────────────────────

    /// <summary>
    /// Extracts a <see cref="Version"/> from a Roslyn project name that may contain a TFM suffix
    /// such as "MyLib (net10.0)" or "MyLib (net48)".
    /// Modern TFMs (net{major}.{minor}) are ordered higher than legacy TFMs (net{nn}).
    /// Projects without a TFM suffix return Version(0,0) and are ordered last.
    /// </summary>
    internal static Version ExtractTfmVersion(string projectName)
    {
        // Modern TFM: "(net{major}.{minor})" e.g. "(net10.0)", "(net9.0)"
        var match = Regex.Match(projectName, @"\(net(\d+)\.(\d+)\)$");
        if (match.Success
            && int.TryParse(match.Groups[1].Value, out var major)
            && int.TryParse(match.Groups[2].Value, out var minor))
        {
            // Bias major by +100 so any modern TFM sorts above any legacy TFM.
            return new Version(major + 100, minor);
        }

        // Legacy TFM: "(net{nn})" e.g. "(net48)", "(net472)"
        // net48 = 4.8.0 → compare as (0, raw_number) but we need 48 > 472 to be false —
        // actually in .NET Framework: net48 = 4.8 > net472 = 4.7.2 → 480 > 472.
        // Normalize: treat raw number as hundred-scaled major.minor (e.g. 48 → 480, 472 → 472).
        var legacy = Regex.Match(projectName, @"\(net(\d+)\)$");
        if (legacy.Success && int.TryParse(legacy.Groups[1].Value, out var lv))
        {
            // If lv < 100, it's a "short" form like net48 = 4.8 → normalize to 480 for comparison.
            // net472 = 4.7.2 → 472. net48 → 480 > 472 ✓
            var normalized = lv < 100 ? lv * 10 : lv;
            return new Version(0, normalized);
        }

        // No TFM suffix — least preferred
        return new Version(0, 0);
    }

    // ── Fingerprint ──────────────────────────────────────────────────────────

    private static string ComputeFingerprint(string slnPath)
    {
        var sb = new StringBuilder();
        sb.Append(slnPath);
        sb.Append(':');
        sb.Append(DateTimeOffset.UtcNow.Ticks);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Inner types ──────────────────────────────────────────────────────────

    /// <summary>
    /// Context passed through the namespace/type walk for a single project.
    /// Carries the project name and solution-level state for edge classification
    /// and stub node accumulation.
    /// </summary>
    private readonly record struct ProjectWalkContext(
        string ProjectName,
        HashSet<string> SolutionProjectNames,
        List<SymbolNode> StubNodes,
        HashSet<string> SeenStubIds);
}
