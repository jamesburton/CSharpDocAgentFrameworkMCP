using System.Text.RegularExpressions;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Lightweight regex-based parser for Dockerfiles.
/// Extracts stage, instruction, and relationship symbols.
/// </summary>
internal static partial class DockerfileParser
{
    // --- Regex patterns ---

    [GeneratedRegex(@"^\s*FROM\s+(\S+?)(?::(\S+))?\s+AS\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromNamedRegex();

    [GeneratedRegex(@"^\s*FROM\s+(\S+?)(?::(\S+))?(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FromUnnamedRegex();

    [GeneratedRegex(@"^\s*COPY\s+--from=(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex CopyFromRegex();

    [GeneratedRegex(@"^\s*(RUN|COPY|EXPOSE|ENTRYPOINT|CMD|WORKDIR|ENV|ARG|ADD|LABEL|VOLUME|USER|HEALTHCHECK|SHELL|STOPSIGNAL|ONBUILD)\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionRegex();

    [GeneratedRegex(@"\bdotnet\s+(run|build|test|publish|restore|clean)\b")]
    private static partial Regex DotnetCommandRegex();

    /// <summary>
    /// Parses a Dockerfile and returns symbol nodes and edges.
    /// </summary>
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(
        string filePath,
        string? basePath = null)
    {
        if (!File.Exists(filePath))
            return ([], []);

        var rawLines = File.ReadAllLines(filePath);
        if (rawLines.Length == 0)
            return ([], []);

        // Handle line continuations: merge lines ending with \
        var lines = MergeLineContinuations(rawLines);

        var relativePath = GetRelativePath(filePath, basePath);
        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        // Track stages
        var stages = new List<(string Name, SymbolId Id, int StartLine, int EndLine)>();
        int stageIndex = 0;

        // First pass: identify stages
        for (int i = 0; i < lines.Count; i++)
        {
            var (text, originalLine) = lines[i];
            var trimmed = text.TrimStart();

            var namedMatch = FromNamedRegex().Match(trimmed);
            if (namedMatch.Success)
            {
                var stageName = namedMatch.Groups[3].Value;
                var stageId = new SymbolId($"T:docker/{relativePath}::Stage/{stageName}");
                stages.Add((stageName, stageId, originalLine, 0));
                stageIndex++;
                continue;
            }

            var unnamedMatch = FromUnnamedRegex().Match(trimmed);
            if (unnamedMatch.Success)
            {
                var stageName = stageIndex.ToString();
                var stageId = new SymbolId($"T:docker/{relativePath}::Stage/{stageName}");
                stages.Add((stageName, stageId, originalLine, 0));
                stageIndex++;
            }
        }

        // Set end lines for stages
        for (int i = 0; i < stages.Count; i++)
        {
            var endLine = i + 1 < stages.Count
                ? stages[i + 1].StartLine - 1
                : rawLines.Length;
            stages[i] = stages[i] with { EndLine = endLine };
        }

        // Create stage nodes
        foreach (var stage in stages)
        {
            var stageNode = new SymbolNode(
                Id: stage.Id,
                Kind: SymbolKind.DockerStage,
                DisplayName: $"Stage: {stage.Name}",
                FullyQualifiedName: $"docker/{relativePath}::Stage/{stage.Name}",
                PreviousIds: [],
                Accessibility: Accessibility.Public,
                Docs: null,
                Span: new SourceSpan(filePath, stage.StartLine, 0, stage.EndLine, 0),
                ReturnType: null,
                Parameters: [],
                GenericConstraints: []);

            nodes.Add(stageNode);
        }

        // Second pass: parse instructions within each stage
        for (int i = 0; i < lines.Count; i++)
        {
            var (text, originalLine) = lines[i];
            var trimmed = text.TrimStart();

            // Skip FROM lines (already handled as stages)
            if (trimmed.StartsWith("FROM", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip blank lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Find which stage this line belongs to
            var currentStage = FindStage(stages, originalLine);
            if (currentStage is null)
                continue;

            var instrMatch = InstructionRegex().Match(trimmed);
            if (!instrMatch.Success)
                continue;

            var instruction = instrMatch.Groups[1].Value.ToUpperInvariant();
            var args = instrMatch.Groups[2].Value.Trim();

            // Only emit nodes for significant instructions
            if (instruction is "RUN" or "COPY" or "EXPOSE" or "ENTRYPOINT" or "CMD" or "WORKDIR")
            {
                var instrId = new SymbolId(
                    $"T:docker/{relativePath}::Stage/{currentStage.Value.Name}::Instruction/{originalLine}_{instruction}");

                var displayName = instruction switch
                {
                    "EXPOSE" => $"EXPOSE {args}",
                    "WORKDIR" => $"WORKDIR {args}",
                    "ENTRYPOINT" => $"ENTRYPOINT {Truncate(args, 60)}",
                    "CMD" => $"CMD {Truncate(args, 60)}",
                    "RUN" => $"RUN {Truncate(args, 60)}",
                    "COPY" => $"COPY {Truncate(args, 60)}",
                    _ => $"{instruction} {Truncate(args, 60)}"
                };

                var docComment = instruction == "COPY"
                    ? new DocComment(
                        Summary: $"COPY {args}",
                        Remarks: null,
                        Params: new Dictionary<string, string>(),
                        TypeParams: new Dictionary<string, string>(),
                        Returns: null,
                        Examples: [],
                        Exceptions: [],
                        SeeAlso: [])
                    : null;

                var instrNode = new SymbolNode(
                    Id: instrId,
                    Kind: SymbolKind.DockerInstruction,
                    DisplayName: displayName,
                    FullyQualifiedName: $"docker/{relativePath}::Stage/{currentStage.Value.Name}::Instruction/{originalLine}_{instruction}",
                    PreviousIds: [],
                    Accessibility: Accessibility.Public,
                    Docs: docComment,
                    Span: new SourceSpan(filePath, originalLine, 0, originalLine, 0),
                    ReturnType: null,
                    Parameters: [],
                    GenericConstraints: []);

                nodes.Add(instrNode);
                edges.Add(new SymbolEdge(currentStage.Value.Id, instrId, SymbolEdgeKind.Contains));

                // Check for COPY --from (cross-stage dependency)
                if (instruction == "COPY")
                {
                    var copyFromMatch = CopyFromRegex().Match(trimmed);
                    if (copyFromMatch.Success)
                    {
                        var fromStageName = copyFromMatch.Groups[1].Value;
                        var fromStage = stages.Find(s =>
                            s.Name.Equals(fromStageName, StringComparison.OrdinalIgnoreCase));
                        if (fromStage != default)
                        {
                            edges.Add(new SymbolEdge(
                                currentStage.Value.Id, fromStage.Id, SymbolEdgeKind.DependsOn));
                        }
                    }
                }

                // Check for dotnet invocations in RUN
                if (instruction == "RUN")
                {
                    var dotnetMatches = DotnetCommandRegex().Matches(args);
                    foreach (Match dm in dotnetMatches)
                    {
                        var command = dm.Groups[1].Value;
                        var invokedId = new SymbolId($"T:tool/dotnet/{command}");
                        edges.Add(new SymbolEdge(instrId, invokedId, SymbolEdgeKind.Invokes));
                    }
                }
            }
        }

        return (nodes, edges);
    }

    /// <summary>
    /// Merges lines ending with backslash continuation into single logical lines,
    /// tracking the original line number (1-based) for each logical line.
    /// </summary>
    private static List<(string Text, int OriginalLine)> MergeLineContinuations(string[] rawLines)
    {
        var result = new List<(string Text, int OriginalLine)>();
        int i = 0;

        while (i < rawLines.Length)
        {
            int startLine = i + 1; // 1-based
            var merged = rawLines[i];

            while (merged.TrimEnd().EndsWith('\\') && i + 1 < rawLines.Length)
            {
                // Remove trailing backslash
                merged = merged.TrimEnd();
                merged = merged[..^1] + " " + rawLines[i + 1].TrimStart();
                i++;
            }

            result.Add((merged, startLine));
            i++;
        }

        return result;
    }

    private static (string Name, SymbolId Id, int StartLine, int EndLine)? FindStage(
        List<(string Name, SymbolId Id, int StartLine, int EndLine)> stages,
        int line)
    {
        for (int i = stages.Count - 1; i >= 0; i--)
        {
            if (line >= stages[i].StartLine)
                return stages[i];
        }

        return null;
    }

    private static string GetRelativePath(string filePath, string? basePath)
    {
        if (basePath is null)
            return Path.GetFileName(filePath);

        var fullFile = Path.GetFullPath(filePath);
        var fullBase = Path.GetFullPath(basePath);
        if (!fullBase.EndsWith(Path.DirectorySeparatorChar))
            fullBase += Path.DirectorySeparatorChar;

        if (fullFile.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return fullFile[fullBase.Length..].Replace('\\', '/');

        return Path.GetFileName(filePath);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
