using System.Text.RegularExpressions;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Regex-based parser for PowerShell (.ps1) scripts.
/// Extracts script-level, function-level, and parameter-level symbols plus cross-reference edges.
/// </summary>
public static partial class PowerShellScriptParser
{
    // --- Compiled regex patterns ---

    // Block comment-based help: <# ... #>
    private static readonly Regex BlockCommentHelpRegex = new(
        @"<#(.*?)#>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Line-based comment help: # .SYNOPSIS ...
    private static readonly Regex LineSynopsisRegex = new(
        @"(?m)^[ \t]*#[ \t]*\.SYNOPSIS[ \t]*\r?\n((?:[ \t]*#.*\r?\n)*)",
        RegexOptions.Compiled);

    // .SYNOPSIS inside a block comment
    private static readonly Regex BlockSynopsisRegex = new(
        @"\.SYNOPSIS[ \t]*\r?\n(.*?)(?=\.[A-Z]|\z)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // #Requires -Modules <name>
    private static readonly Regex RequiresModulesRegex = new(
        @"(?m)^[ \t]*#Requires\s+-Modules\s+(\S+)",
        RegexOptions.Compiled);

    // Dot-sourcing: . .\path\to\script.ps1
    private static readonly Regex DotSourceRegex = new(
        @"(?m)^[ \t]*\.\s+[""']?(\.[/\\][^\s""']+\.ps1)[""']?",
        RegexOptions.Compiled);

    // Function or filter declaration: function Name { ... } (with balanced braces)
    private static readonly Regex FunctionDeclRegex = new(
        @"(?m)^[ \t]*(?:function|filter)\s+([\w-]+)\s*(?:\(([^)]*)\))?\s*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Finds the start of a param block (the opening paren index): optional [CmdletBinding(...)], then param(
    private static readonly Regex ParamBlockStartRegex = new(
        @"(?:\[CmdletBinding\([^)]*\)\]\s*)?param\s*\(",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Individual parameter: optional [Parameter(...)], optional [type], then $Name
    // Uses a lookahead to ensure $Name is NOT preceded by = (avoids matching Mandatory=$true)
    private static readonly Regex ParamEntryRegex = new(
        @"((?:\[Parameter\([^)]*\)\]\s*)*)(?:\[(\w[\w.]*(?:\[[\w.]*\])?(?:\[\])?)\]\s*)?(?<!=)\$([A-Za-z_]\w*)",
        RegexOptions.Compiled);

    // Mandatory=$true inside [Parameter(...)]
    private static readonly Regex MandatoryRegex = new(
        @"Mandatory\s*=\s*\$true",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // dotnet invocations: dotnet build, dotnet test, dotnet run
    private static readonly Regex DotnetInvocationRegex = new(
        @"(?m)^[ \t]*(?:&\s*)?dotnet\s+(build|test|run|publish|pack)\b[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // .NET type reference: [System.Something]::Method or [System.Something] (including generics like [System.Collections.Generic.List[string]])
    private static readonly Regex DotNetTypeRefRegex = new(
        @"\[(System(?:\.\w+)+)(?:\[\w+\])?\](?:::(\w+))?",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a PowerShell .ps1 file and returns symbol nodes and edges.
    /// </summary>
    /// <param name="filePath">Absolute path to the .ps1 file.</param>
    /// <param name="basePath">Optional base path for computing relative paths in SymbolIds.</param>
    /// <returns>A tuple of symbol nodes and symbol edges.</returns>
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(
        string filePath, string? basePath = null)
    {
        if (!File.Exists(filePath))
            return (Array.Empty<SymbolNode>(), Array.Empty<SymbolEdge>());

        var content = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(content))
            return (Array.Empty<SymbolNode>(), Array.Empty<SymbolEdge>());

        var lines = content.Split('\n');
        var relativePath = ComputeRelativePath(filePath, basePath);
        var scriptIdValue = $"T:script/ps1/{relativePath}";
        var scriptId = new SymbolId(scriptIdValue);

        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        // --- Script-level node ---
        var scriptSummary = ExtractFileHelp(content);
        var scriptDoc = scriptSummary is not null
            ? new DocComment(scriptSummary, null, new Dictionary<string, string>(),
                new Dictionary<string, string>(), null, [], [], [])
            : null;

        var scriptNode = new SymbolNode(
            scriptId,
            SymbolKind.Script,
            Path.GetFileName(filePath),
            scriptIdValue,
            [],
            Accessibility.Public,
            scriptDoc,
            new SourceSpan(filePath, 1, 0, lines.Length, 0),
            null,
            [],
            []);
        nodes.Add(scriptNode);

        // --- #Requires -Modules edges ---
        foreach (Match m in RequiresModulesRegex.Matches(content))
        {
            var moduleName = m.Groups[1].Value;
            var targetId = new SymbolId($"T:module/{moduleName}");
            edges.Add(new SymbolEdge(scriptId, targetId, SymbolEdgeKind.Imports));
        }

        // --- Dot-sourcing edges ---
        foreach (Match m in DotSourceRegex.Matches(content))
        {
            var sourcedPath = m.Groups[1].Value.Replace('\\', '/');
            var targetId = new SymbolId($"T:script/ps1/{sourcedPath}");
            edges.Add(new SymbolEdge(scriptId, targetId, SymbolEdgeKind.Imports));
        }

        // --- Dotnet invocation edges ---
        foreach (Match m in DotnetInvocationRegex.Matches(content))
        {
            var verb = m.Groups[1].Value.ToLowerInvariant();
            var targetId = new SymbolId($"T:tool/dotnet/{verb}");
            var line = GetLineNumber(content, m.Index);
            edges.Add(new SymbolEdge(scriptId, targetId, SymbolEdgeKind.Invokes));
        }

        // --- .NET type references ---
        foreach (Match m in DotNetTypeRefRegex.Matches(content))
        {
            var typeName = m.Groups[1].Value;
            var targetId = new SymbolId($"T:{typeName}");
            edges.Add(new SymbolEdge(scriptId, targetId, SymbolEdgeKind.References));
        }

        // --- Function declarations ---
        ParseFunctions(content, lines, filePath, scriptIdValue, scriptId, nodes, edges);

        return (nodes, edges);
    }

    private static void ParseFunctions(
        string content, string[] lines, string filePath,
        string scriptIdValue, SymbolId scriptId,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        foreach (Match funcMatch in FunctionDeclRegex.Matches(content))
        {
            var funcName = funcMatch.Groups[1].Value;
            var funcIdValue = $"{scriptIdValue}::Function/{funcName}";
            var funcId = new SymbolId(funcIdValue);
            var funcStartLine = GetLineNumber(content, funcMatch.Index);

            // Find end of function body by brace matching
            var funcEndLine = FindClosingBraceLine(content, funcMatch.Index + funcMatch.Length - 1);

            var funcNode = new SymbolNode(
                funcId,
                SymbolKind.ScriptFunction,
                funcName,
                funcIdValue,
                [],
                Accessibility.Public,
                null,
                new SourceSpan(filePath, funcStartLine, 0, funcEndLine, 0),
                null,
                [],
                []);
            nodes.Add(funcNode);
            edges.Add(new SymbolEdge(scriptId, funcId, SymbolEdgeKind.Contains));

            // Extract function body to look for param block
            var bodyStart = funcMatch.Index + funcMatch.Length;
            var bodyEndIndex = FindClosingBraceIndex(content, funcMatch.Index + funcMatch.Length - 1);
            if (bodyEndIndex > bodyStart)
            {
                var funcBody = content[bodyStart..bodyEndIndex];
                ParseParameters(funcBody, funcIdValue, funcId, funcStartLine, filePath, nodes, edges);
            }

            // Also check inline params: function Name([type]$p) { }
            if (funcMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(funcMatch.Groups[2].Value))
            {
                ParseInlineParameters(funcMatch.Groups[2].Value, funcIdValue, funcId, funcStartLine, filePath, nodes, edges);
            }
        }
    }

    private static void ParseParameters(
        string funcBody, string funcIdValue, SymbolId funcId,
        int funcStartLine, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        var paramBlockMatch = ParamBlockStartRegex.Match(funcBody);
        if (!paramBlockMatch.Success) return;

        // The match ends just after the opening '(' — find the matching closing paren
        var openParenIndex = paramBlockMatch.Index + paramBlockMatch.Length - 1;
        var closeParenIndex = FindClosingParenIndex(funcBody, openParenIndex);
        if (closeParenIndex <= openParenIndex) return;

        var paramContent = funcBody[(openParenIndex + 1)..closeParenIndex];
        foreach (Match pm in ParamEntryRegex.Matches(paramContent))
        {
            var paramAttr = pm.Groups[1].Value;
            var typeName = pm.Groups[2].Success ? pm.Groups[2].Value : "object";
            var paramName = pm.Groups[3].Value;
            var isMandatory = !string.IsNullOrEmpty(paramAttr) && MandatoryRegex.IsMatch(paramAttr);

            var paramIdValue = $"{funcIdValue}::Param/{paramName}";
            var paramId = new SymbolId(paramIdValue);

            var paramInfo = new ParameterInfo(
                paramName, typeName, null, false, false, false, false);

            var paramDoc = isMandatory
                ? new DocComment("Mandatory parameter", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], [])
                : null;

            var paramNode = new SymbolNode(
                paramId,
                SymbolKind.ScriptParameter,
                paramName,
                paramIdValue,
                [],
                Accessibility.Public,
                paramDoc,
                new SourceSpan(filePath, funcStartLine, 0, funcStartLine, 0),
                typeName,
                [paramInfo],
                []);
            nodes.Add(paramNode);
            edges.Add(new SymbolEdge(funcId, paramId, SymbolEdgeKind.Contains));
        }
    }

    private static void ParseInlineParameters(
        string paramText, string funcIdValue, SymbolId funcId,
        int funcStartLine, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        foreach (Match pm in ParamEntryRegex.Matches(paramText))
        {
            var typeName = pm.Groups[2].Success ? pm.Groups[2].Value : "object";
            var paramName = pm.Groups[3].Value;

            var paramIdValue = $"{funcIdValue}::Param/{paramName}";
            var paramId = new SymbolId(paramIdValue);

            // Skip if already added via param block
            if (nodes.Any(n => n.Id == paramId)) continue;

            var paramInfo = new ParameterInfo(
                paramName, typeName, null, false, false, false, false);

            var paramNode = new SymbolNode(
                paramId,
                SymbolKind.ScriptParameter,
                paramName,
                paramIdValue,
                [],
                Accessibility.Public,
                null,
                new SourceSpan(filePath, funcStartLine, 0, funcStartLine, 0),
                typeName,
                [paramInfo],
                []);
            nodes.Add(paramNode);
            edges.Add(new SymbolEdge(funcId, paramId, SymbolEdgeKind.Contains));
        }
    }

    private static string? ExtractFileHelp(string content)
    {
        // Try block comment <# .SYNOPSIS ... #>
        var blockMatch = BlockCommentHelpRegex.Match(content);
        if (blockMatch.Success)
        {
            var blockContent = blockMatch.Groups[1].Value;
            var synMatch = BlockSynopsisRegex.Match(blockContent);
            if (synMatch.Success)
                return CleanHelpText(synMatch.Groups[1].Value);

            // If no .SYNOPSIS but there's text, use first meaningful line
            var trimmed = blockContent.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                return CleanHelpText(trimmed);
        }

        // Try line-based # .SYNOPSIS
        var lineMatch = LineSynopsisRegex.Match(content);
        if (lineMatch.Success)
        {
            var helpLines = lineMatch.Groups[1].Value;
            return CleanLineHelp(helpLines);
        }

        return null;
    }

    private static string CleanHelpText(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();
        return string.Join(" ", lines);
    }

    private static string CleanLineHelp(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.TrimStart().TrimStart('#').Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToArray();
        return string.Join(" ", lines);
    }

    private static string ComputeRelativePath(string filePath, string? basePath)
    {
        if (basePath is null)
            return Path.GetFileName(filePath);

        var normalizedFile = Path.GetFullPath(filePath).Replace('\\', '/');
        var normalizedBase = Path.GetFullPath(basePath).Replace('\\', '/').TrimEnd('/') + '/';

        if (normalizedFile.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return normalizedFile[normalizedBase.Length..];

        return Path.GetFileName(filePath);
    }

    private static int GetLineNumber(string content, int charIndex)
    {
        var line = 1;
        for (var i = 0; i < charIndex && i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
        }
        return line;
    }

    private static int FindClosingBraceLine(string content, int openBraceIndex)
    {
        var closeIndex = FindClosingBraceIndex(content, openBraceIndex);
        return GetLineNumber(content, closeIndex);
    }

    private static int FindClosingBraceIndex(string content, int openBraceIndex)
    {
        var depth = 1;
        for (var i = openBraceIndex + 1; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }
        return content.Length;
    }

    private static int FindClosingParenIndex(string content, int openParenIndex)
    {
        var depth = 1;
        for (var i = openParenIndex + 1; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }
        return -1;
    }
}
