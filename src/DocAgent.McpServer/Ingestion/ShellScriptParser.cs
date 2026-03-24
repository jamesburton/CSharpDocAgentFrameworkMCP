using System.Text.RegularExpressions;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Lightweight regex-based parser for shell scripts (.sh / .bash).
/// Extracts file-level, function-level, and relationship symbols.
/// </summary>
internal static partial class ShellScriptParser
{
    // --- Regex patterns ---

    [GeneratedRegex(@"^#!\s*(/usr/bin/env\s+)?(bash|sh|/bin/bash|/bin/sh|/usr/bin/bash)\b.*$")]
    private static partial Regex ShebangRegex();

    // Matches: function name() { ... } or function name { ... }
    [GeneratedRegex(@"^\s*function\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*(\(\s*\))?\s*\{?")]
    private static partial Regex FunctionKeywordRegex();

    // Matches: name() { ... }
    [GeneratedRegex(@"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\(\s*\)\s*\{?")]
    private static partial Regex FunctionShortRegex();

    // Matches: source ./path or . ./path
    [GeneratedRegex(@"^\s*(?:source|\.) +[""']?([^\s""']+)[""']?")]
    private static partial Regex SourceImportRegex();

    // Matches: dotnet run/build/test ...
    [GeneratedRegex(@"\bdotnet\s+(run|build|test|publish|restore|clean)\b")]
    private static partial Regex DotnetInvocationRegex();

    // Matches: ./some-script.sh
    [GeneratedRegex(@"(?:^|\s|;|&&|\|\|)(\.\/[a-zA-Z0-9_\-/.]+\.sh)\b")]
    private static partial Regex ScriptInvocationRegex();

    /// <summary>
    /// Parses a shell script file and returns symbol nodes and edges.
    /// </summary>
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(
        string filePath,
        string? basePath = null)
    {
        if (!File.Exists(filePath))
            return ([], []);

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
            return ([], []);

        var relativePath = GetRelativePath(filePath, basePath);
        var scriptId = new SymbolId($"T:script/sh/{relativePath}");

        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        // --- Parse file-level symbol ---
        string? shebang = null;
        var commentLines = new List<string>();
        int headerEnd = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == 0 && ShebangRegex().IsMatch(line))
            {
                shebang = line.TrimStart();
                headerEnd = 1;
                continue;
            }

            if (line.TrimStart().StartsWith('#') && !ShebangRegex().IsMatch(line))
            {
                var commentText = line.TrimStart();
                commentText = commentText.Length > 1 ? commentText[1..].TrimStart() : "";
                commentLines.Add(commentText);
                headerEnd = i + 1;
            }
            else if (string.IsNullOrWhiteSpace(line) && commentLines.Count > 0 && i < 5)
            {
                // Allow blank lines in the leading comment block (within first few lines)
                headerEnd = i + 1;
                continue;
            }
            else
            {
                break;
            }
        }

        var fileSummary = commentLines.Count > 0 ? string.Join("\n", commentLines) : null;
        var fileDoc = new DocComment(
            Summary: fileSummary,
            Remarks: shebang,
            Params: new Dictionary<string, string>(),
            TypeParams: new Dictionary<string, string>(),
            Returns: null,
            Examples: [],
            Exceptions: [],
            SeeAlso: []);

        var scriptNode = new SymbolNode(
            Id: scriptId,
            Kind: SymbolKind.Script,
            DisplayName: Path.GetFileName(filePath),
            FullyQualifiedName: $"script/sh/{relativePath}",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: fileDoc,
            Span: new SourceSpan(filePath, 1, 0, lines.Length, 0),
            ReturnType: null,
            Parameters: [],
            GenericConstraints: []);

        nodes.Add(scriptNode);

        // --- Parse functions and relationships ---
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Check for function declarations
            string? funcName = null;
            var funcMatch = FunctionKeywordRegex().Match(line);
            if (funcMatch.Success)
            {
                funcName = funcMatch.Groups[1].Value;
            }
            else
            {
                var shortMatch = FunctionShortRegex().Match(line);
                if (shortMatch.Success)
                {
                    // Exclude common keywords that look like functions
                    var candidate = shortMatch.Groups[1].Value;
                    if (candidate is not ("if" or "while" or "for" or "until" or "case" or "select" or "then" or "else" or "elif" or "do" or "done" or "fi" or "esac"))
                    {
                        funcName = candidate;
                    }
                }
            }

            if (funcName is not null)
            {
                // Collect comments above the function
                var funcComments = new List<string>();
                for (int j = i - 1; j >= 0; j--)
                {
                    var prevLine = lines[j].TrimStart();
                    if (prevLine.StartsWith('#'))
                    {
                        var cText = prevLine.Length > 1 ? prevLine[1..].TrimStart() : "";
                        funcComments.Insert(0, cText);
                    }
                    else if (string.IsNullOrWhiteSpace(prevLine))
                    {
                        break;
                    }
                    else
                    {
                        break;
                    }
                }

                var funcSummary = funcComments.Count > 0 ? string.Join("\n", funcComments) : null;
                var funcDoc = new DocComment(
                    Summary: funcSummary,
                    Remarks: null,
                    Params: new Dictionary<string, string>(),
                    TypeParams: new Dictionary<string, string>(),
                    Returns: null,
                    Examples: [],
                    Exceptions: [],
                    SeeAlso: []);

                // Find end of function (count braces)
                int endLine = FindFunctionEnd(lines, i);

                var funcId = new SymbolId($"T:script/sh/{relativePath}::Function/{funcName}");
                var funcNode = new SymbolNode(
                    Id: funcId,
                    Kind: SymbolKind.ScriptFunction,
                    DisplayName: $"{funcName}()",
                    FullyQualifiedName: $"script/sh/{relativePath}::Function/{funcName}",
                    PreviousIds: [],
                    Accessibility: Accessibility.Public,
                    Docs: funcDoc,
                    Span: new SourceSpan(filePath, i + 1, 0, endLine + 1, 0),
                    ReturnType: null,
                    Parameters: [],
                    GenericConstraints: []);

                nodes.Add(funcNode);
                edges.Add(new SymbolEdge(scriptId, funcId, SymbolEdgeKind.Contains));
            }

            // Check for source/import
            var sourceMatch = SourceImportRegex().Match(line);
            if (sourceMatch.Success)
            {
                var importedPath = sourceMatch.Groups[1].Value;
                var importedRelative = NormalizeImportPath(importedPath, relativePath);
                var importedId = new SymbolId($"T:script/sh/{importedRelative}");
                edges.Add(new SymbolEdge(scriptId, importedId, SymbolEdgeKind.Imports));
            }

            // Check for dotnet invocations
            var dotnetMatch = DotnetInvocationRegex().Match(line);
            if (dotnetMatch.Success)
            {
                var command = dotnetMatch.Groups[1].Value;
                var invokedId = new SymbolId($"T:tool/dotnet/{command}");
                edges.Add(new SymbolEdge(scriptId, invokedId, SymbolEdgeKind.Invokes));
            }

            // Check for script invocations
            var scriptInvokeMatch = ScriptInvocationRegex().Match(line);
            if (scriptInvokeMatch.Success)
            {
                var invokedScript = scriptInvokeMatch.Groups[1].Value;
                var invokedRelative = NormalizeImportPath(invokedScript, relativePath);
                var invokedId = new SymbolId($"T:script/sh/{invokedRelative}");
                edges.Add(new SymbolEdge(scriptId, invokedId, SymbolEdgeKind.Invokes));
            }
        }

        return (nodes, edges);
    }

    private static int FindFunctionEnd(string[] lines, int startLine)
    {
        int braceCount = 0;
        bool foundOpen = false;

        for (int i = startLine; i < lines.Length; i++)
        {
            foreach (char c in lines[i])
            {
                if (c == '{')
                {
                    braceCount++;
                    foundOpen = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (foundOpen && braceCount == 0)
                        return i;
                }
            }
        }

        return startLine; // Fallback: single line
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

    private static string NormalizeImportPath(string importPath, string currentRelativePath)
    {
        // Strip leading ./ if present
        if (importPath.StartsWith("./"))
            importPath = importPath[2..];

        // If the current file is in a subdirectory, resolve relative to it
        var dir = Path.GetDirectoryName(currentRelativePath);
        if (!string.IsNullOrEmpty(dir))
            return $"{dir}/{importPath}".Replace('\\', '/');

        return importPath.Replace('\\', '/');
    }
}
