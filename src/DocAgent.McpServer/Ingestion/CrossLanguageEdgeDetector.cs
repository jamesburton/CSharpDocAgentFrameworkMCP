using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Analyzes a <see cref="SymbolGraphSnapshot"/> containing both C# and script/tool symbols
/// and detects cross-language edges between them.
/// </summary>
public static class CrossLanguageEdgeDetector
{
    private static readonly SymbolKind[] ScriptKinds = [SymbolKind.Script, SymbolKind.Tool, SymbolKind.BuildTarget];

    /// <summary>
    /// Detects cross-language edges between script/tool nodes and C# symbols in the snapshot.
    /// Returns NEW edges only — does not modify the input snapshot.
    /// </summary>
    public static IReadOnlyList<SymbolEdge> DetectEdges(SymbolGraphSnapshot snapshot)
    {
        var scriptNodes = snapshot.Nodes
            .Where(n => ScriptKinds.Contains(n.Kind))
            .ToList();

        if (scriptNodes.Count == 0)
            return [];

        var csharpNodes = snapshot.Nodes
            .Where(n => !ScriptKinds.Contains(n.Kind))
            .ToList();

        if (csharpNodes.Count == 0)
            return [];

        // Build lookup structures for C# symbols
        var typesByFqn = csharpNodes
            .Where(n => n.Kind == SymbolKind.Type && n.FullyQualifiedName is not null)
            .ToDictionary(n => n.FullyQualifiedName!, n => n);

        var namespacesByProject = csharpNodes
            .Where(n => n.Kind == SymbolKind.Namespace && n.ProjectOrigin is not null)
            .GroupBy(n => n.ProjectOrigin!)
            .ToDictionary(g => g.Key, g => g.First());

        // Also index by project file path fragment for .csproj matching
        var projectsByPathFragment = BuildProjectPathIndex(csharpNodes, namespacesByProject);

        var existingEdges = new HashSet<(string From, string To, SymbolEdgeKind Kind)>(
            snapshot.Edges.Select(e => (e.From.Value, e.To.Value, e.Kind)));

        var newEdges = new List<SymbolEdge>();

        foreach (var scriptNode in scriptNodes)
        {
            // Strategy 1: Project path matching via existing Invokes edges
            DetectProjectPathEdges(scriptNode, snapshot.Edges, projectsByPathFragment, existingEdges, newEdges);

            // Strategy 2: Type name matching via existing References edges
            DetectTypeNameEdges(scriptNode, snapshot.Edges, typesByFqn, existingEdges, newEdges);

            // Strategy 4: Command-line heuristic matching from doc comments
            DetectCommandLineEdges(scriptNode, typesByFqn, projectsByPathFragment, existingEdges, newEdges);
        }

        return newEdges;
    }

    /// <summary>
    /// Strategy 1: Match script Invokes edges pointing to synthetic targets like "dotnet-build:./src/MyApp.csproj"
    /// against C# symbols' ProjectOrigin field.
    /// Confidence: HIGH — explicit project path reference.
    /// </summary>
    private static void DetectProjectPathEdges(
        SymbolNode scriptNode,
        IReadOnlyList<SymbolEdge> allEdges,
        Dictionary<string, SymbolNode> projectsByPathFragment,
        HashSet<(string From, string To, SymbolEdgeKind Kind)> existingEdges,
        List<SymbolEdge> newEdges)
    {
        var invokesEdges = allEdges
            .Where(e => e.From == scriptNode.Id && e.Kind == SymbolEdgeKind.Invokes)
            .ToList();

        foreach (var edge in invokesEdges)
        {
            var target = edge.To.Value;

            // Parse synthetic target like "dotnet-build:./src/MyApp.csproj"
            var colonIdx = target.IndexOf(':');
            if (colonIdx < 0) continue;

            var path = target[(colonIdx + 1)..];
            path = NormalizePath(path);

            // Try to match against project path fragments
            foreach (var (fragment, namespaceNode) in projectsByPathFragment)
            {
                if (path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    TryAddEdge(scriptNode.Id, namespaceNode.Id, SymbolEdgeKind.Invokes, existingEdges, newEdges);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Strategy 2: Match script References edges pointing to .NET type names
    /// (e.g., "[System.IO.Path]" in PowerShell) against C# Type nodes by FQN.
    /// Confidence: MEDIUM — type name matching may have false positives for common names.
    /// </summary>
    private static void DetectTypeNameEdges(
        SymbolNode scriptNode,
        IReadOnlyList<SymbolEdge> allEdges,
        Dictionary<string, SymbolNode> typesByFqn,
        HashSet<(string From, string To, SymbolEdgeKind Kind)> existingEdges,
        List<SymbolEdge> newEdges)
    {
        var referencesEdges = allEdges
            .Where(e => e.From == scriptNode.Id && e.Kind == SymbolEdgeKind.References)
            .ToList();

        foreach (var edge in referencesEdges)
        {
            var target = edge.To.Value;

            // The target might be a raw FQN or prefixed
            if (typesByFqn.TryGetValue(target, out var typeNode))
            {
                TryAddEdge(scriptNode.Id, typeNode.Id, SymbolEdgeKind.References, existingEdges, newEdges);
            }
        }
    }

    /// <summary>
    /// Strategy 4: Parse dotnet CLI commands from script node documentation and match
    /// against C# symbols (class names from --filter, project paths from --project).
    /// Confidence: LOW — heuristic parsing of command strings; may miss or false-match.
    /// </summary>
    private static void DetectCommandLineEdges(
        SymbolNode scriptNode,
        Dictionary<string, SymbolNode> typesByFqn,
        Dictionary<string, SymbolNode> projectsByPathFragment,
        HashSet<(string From, string To, SymbolEdgeKind Kind)> existingEdges,
        List<SymbolEdge> newEdges)
    {
        // Look for command strings in the node's doc comment
        var summary = scriptNode.Docs?.Summary;
        if (summary is null) return;

        var cmdInfo = DotnetCommandParser.ParseDotnetCommand(summary);
        if (cmdInfo is null) return;

        // If there's a filter expression, try to find the class name
        if (cmdInfo.FilterExpression is not null)
        {
            // Extract the class name from filter like "FullyQualifiedName~ClassName"
            var tildeIdx = cmdInfo.FilterExpression.IndexOf('~');
            if (tildeIdx >= 0)
            {
                var className = cmdInfo.FilterExpression[(tildeIdx + 1)..].Trim('"', '\'');
                // Search for type nodes containing this class name
                foreach (var (fqn, typeNode) in typesByFqn)
                {
                    if (fqn.EndsWith(className, StringComparison.Ordinal) ||
                        fqn.Contains("." + className, StringComparison.Ordinal))
                    {
                        TryAddEdge(scriptNode.Id, typeNode.Id, SymbolEdgeKind.References, existingEdges, newEdges);
                    }
                }
            }
        }

        // If there's a project path, try to match it
        if (cmdInfo.ProjectPath is not null)
        {
            var normalizedPath = NormalizePath(cmdInfo.ProjectPath);
            foreach (var (fragment, namespaceNode) in projectsByPathFragment)
            {
                if (normalizedPath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    TryAddEdge(scriptNode.Id, namespaceNode.Id, SymbolEdgeKind.Invokes, existingEdges, newEdges);
                    break;
                }
            }
        }
    }

    private static Dictionary<string, SymbolNode> BuildProjectPathIndex(
        List<SymbolNode> csharpNodes,
        Dictionary<string, SymbolNode> namespacesByProject)
    {
        var result = new Dictionary<string, SymbolNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var (projectName, nsNode) in namespacesByProject)
        {
            // Use project name as path fragment (e.g., "MyApp" matches "MyApp.csproj")
            result.TryAdd(projectName, nsNode);

            // Also try to find a .csproj-like pattern from source spans
            var nodeWithSpan = csharpNodes
                .FirstOrDefault(n => n.ProjectOrigin == projectName && n.Span is not null);
            if (nodeWithSpan?.Span is not null)
            {
                // Extract project directory from source path
                var dir = Path.GetDirectoryName(nodeWithSpan.Span.FilePath);
                if (dir is not null)
                {
                    var projectFile = projectName + ".csproj";
                    result.TryAdd(projectFile, nsNode);
                }
            }
        }

        return result;
    }

    private static void TryAddEdge(
        SymbolId from,
        SymbolId to,
        SymbolEdgeKind kind,
        HashSet<(string From, string To, SymbolEdgeKind Kind)> existingEdges,
        List<SymbolEdge> newEdges)
    {
        var key = (from.Value, to.Value, kind);
        if (existingEdges.Add(key))
        {
            newEdges.Add(new SymbolEdge(from, to, kind, EdgeScope.CrossProject));
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('.', '/');
    }
}

/// <summary>
/// Parses dotnet CLI command strings into structured information.
/// </summary>
public static class DotnetCommandParser
{
    /// <summary>
    /// Parses a dotnet CLI command string into its components.
    /// Returns null for non-dotnet commands.
    /// </summary>
    public static DotnetCommandInfo? ParseDotnetCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var trimmed = commandLine.Trim();

        // Must start with "dotnet"
        if (!trimmed.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = SplitCommandLine(trimmed);
        if (parts.Count < 2)
            return null;

        // parts[0] = "dotnet", parts[1] = verb
        var verb = parts[1].ToLowerInvariant();

        // Known verbs
        if (verb is not ("build" or "test" or "run" or "publish" or "pack" or "clean" or "restore"))
            return null;

        string? projectPath = null;
        string? filterExpression = null;
        var additionalArgs = new List<string>();

        for (int i = 2; i < parts.Count; i++)
        {
            var part = parts[i];

            if (part is "--filter" && i + 1 < parts.Count)
            {
                filterExpression = parts[++i].Trim('"', '\'');
            }
            else if (part is "--project" && i + 1 < parts.Count)
            {
                projectPath = parts[++i].Trim('"', '\'');
            }
            else if (!part.StartsWith('-') && projectPath is null)
            {
                // Positional argument — likely a project path
                projectPath = part.Trim('"', '\'');
            }
            else
            {
                additionalArgs.Add(part);
            }
        }

        return new DotnetCommandInfo(verb, projectPath, filterExpression, additionalArgs);
    }

    private static List<string> SplitCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        var quoteChar = '"';

        foreach (var ch in commandLine)
        {
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch is '"' or '\'')
            {
                inQuote = true;
                quoteChar = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }
}

/// <summary>
/// Result of parsing a dotnet CLI command.
/// </summary>
public sealed record DotnetCommandInfo(
    string Verb,
    string? ProjectPath,
    string? FilterExpression,
    IReadOnlyList<string> AdditionalArgs);
