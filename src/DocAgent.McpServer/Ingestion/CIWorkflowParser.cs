using System.Text.RegularExpressions;
using DocAgent.Core;
using YamlDotNet.RepresentationModel;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Parses CI/CD workflow YAML files (GitHub Actions and Azure Pipelines) into
/// <see cref="SymbolNode"/> and <see cref="SymbolEdge"/> collections for the symbol graph.
/// </summary>
internal static partial class CIWorkflowParser
{
    private static readonly Regex DotnetCommandRegex = CreateDotnetCommandRegex();

    internal enum WorkflowType { GitHubActions, AzurePipelines, Unknown }

    /// <summary>
    /// Parses a CI/CD YAML file and returns the resulting symbol nodes and edges.
    /// </summary>
    /// <param name="filePath">Absolute path to the YAML file.</param>
    /// <param name="basePath">Optional base path for computing relative file names.</param>
    /// <returns>Tuple of nodes and edges extracted from the workflow file.</returns>
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges) Parse(
        string filePath, string? basePath = null)
    {
        var nodes = new List<SymbolNode>();
        var edges = new List<SymbolEdge>();

        string yamlContent;
        try
        {
            yamlContent = File.ReadAllText(filePath);
        }
        catch
        {
            return (nodes, edges);
        }

        if (string.IsNullOrWhiteSpace(yamlContent))
            return (nodes, edges);

        var workflowType = DetectWorkflowType(filePath, yamlContent);
        if (workflowType == WorkflowType.Unknown)
            return (nodes, edges);

        YamlStream yaml;
        try
        {
            yaml = new YamlStream();
            using var reader = new StringReader(yamlContent);
            yaml.Load(reader);
        }
        catch
        {
            // Malformed YAML — return empty.
            return (nodes, edges);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            return (nodes, edges);

        var fileName = Path.GetFileName(filePath);

        switch (workflowType)
        {
            case WorkflowType.GitHubActions:
                ParseGitHubActions(root, fileName, filePath, nodes, edges);
                break;
            case WorkflowType.AzurePipelines:
                ParseAzurePipelines(root, fileName, filePath, nodes, edges);
                break;
        }

        return (nodes, edges);
    }

    /// <summary>Detects whether a YAML file is GitHub Actions or Azure Pipelines.</summary>
    internal static WorkflowType DetectWorkflowType(string filePath, string content)
    {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains(".github/workflows/") &&
            (normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
             normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            return WorkflowType.GitHubActions;
        }

        var fileName = Path.GetFileName(filePath);
        if (fileName.Equals("azure-pipelines.yml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("azure-pipelines.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowType.AzurePipelines;
        }

        // Heuristic: file contains both trigger: and pool: keys
        if (content.Contains("trigger:") && content.Contains("pool:"))
            return WorkflowType.AzurePipelines;

        return WorkflowType.Unknown;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  GitHub Actions
    // ──────────────────────────────────────────────────────────────────────

    private static void ParseGitHubActions(
        YamlMappingNode root, string fileName, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        var workflowId = new SymbolId($"T:ci/github/{fileName}");

        // Parse name
        var displayName = GetScalarValue(root, "name") ?? fileName;

        // Parse triggers from 'on:'
        string? triggerSummary = null;
        if (TryGetValue(root, "on", out var onNode) || TryGetValue(root, "true", out onNode))
        {
            triggerSummary = ParseGitHubTriggers(onNode);
        }

        var workflowNode = CreateSymbolNode(
            workflowId, SymbolKind.CIWorkflow, displayName,
            filePath, GetStartLine(root),
            summary: triggerSummary);
        nodes.Add(workflowNode);

        // Parse jobs
        if (!TryGetValue(root, "jobs", out var jobsNode) || jobsNode is not YamlMappingNode jobsMapping)
            return;

        foreach (var jobEntry in jobsMapping.Children)
        {
            if (jobEntry.Key is not YamlScalarNode jobKeyNode || jobEntry.Value is not YamlMappingNode jobMapping)
                continue;

            var jobName = jobKeyNode.Value ?? "unknown";
            var jobId = new SymbolId($"T:ci/github/{fileName}::Job/{jobName}");
            var jobDisplayName = GetScalarValue(jobMapping, "name") ?? jobName;
            var runsOn = GetScalarValue(jobMapping, "runs-on");

            var jobNode = CreateSymbolNode(
                jobId, SymbolKind.CIJob, jobDisplayName,
                filePath, GetStartLine(jobKeyNode),
                remarks: runsOn != null ? $"runs-on: {runsOn}" : null);
            nodes.Add(jobNode);

            // Workflow Contains Job
            edges.Add(new SymbolEdge(workflowId, jobId, SymbolEdgeKind.Contains));

            // Parse needs → DependsOn edges
            if (TryGetValue(jobMapping, "needs", out var needsNode))
            {
                foreach (var need in GetScalarList(needsNode))
                {
                    var depId = new SymbolId($"T:ci/github/{fileName}::Job/{need}");
                    edges.Add(new SymbolEdge(jobId, depId, SymbolEdgeKind.DependsOn));
                }
            }

            // Parse steps
            if (TryGetValue(jobMapping, "steps", out var stepsNode) && stepsNode is YamlSequenceNode stepsSeq)
            {
                ParseGitHubSteps(stepsSeq, fileName, jobName, jobId, filePath, nodes, edges);
            }
        }
    }

    private static void ParseGitHubSteps(
        YamlSequenceNode stepsSeq, string fileName, string jobName, SymbolId jobId,
        string filePath, List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        for (var i = 0; i < stepsSeq.Children.Count; i++)
        {
            if (stepsSeq.Children[i] is not YamlMappingNode stepMapping)
                continue;

            var stepName = GetScalarValue(stepMapping, "name");
            var uses = GetScalarValue(stepMapping, "uses");
            var run = GetScalarValue(stepMapping, "run");

            var stepLabel = stepName ?? uses ?? $"step{i}";
            var safeName = SanitizeName(stepLabel);
            var stepId = new SymbolId($"T:ci/github/{fileName}::Job/{jobName}::Step/{i}_{safeName}");

            var stepNode = CreateSymbolNode(
                stepId, SymbolKind.CIStep, stepLabel,
                filePath, GetStartLine(stepMapping));
            nodes.Add(stepNode);

            // Job Contains Step
            edges.Add(new SymbolEdge(jobId, stepId, SymbolEdgeKind.Contains));

            // uses: → Invokes edge
            if (uses != null)
            {
                var targetId = new SymbolId($"T:ci/action/{uses}");
                edges.Add(new SymbolEdge(stepId, targetId, SymbolEdgeKind.Invokes));
            }

            // run: → scan for dotnet commands
            if (run != null)
            {
                foreach (var cmd in ExtractDotnetCommands(run))
                {
                    var cmdId = new SymbolId($"T:ci/cmd/{cmd}");
                    edges.Add(new SymbolEdge(stepId, cmdId, SymbolEdgeKind.Invokes));
                }
            }
        }
    }

    private static string? ParseGitHubTriggers(YamlNode onNode)
    {
        var triggers = new List<string>();

        switch (onNode)
        {
            case YamlScalarNode scalar:
                if (scalar.Value != null) triggers.Add(scalar.Value);
                break;
            case YamlSequenceNode seq:
                foreach (var item in seq.Children)
                {
                    if (item is YamlScalarNode s && s.Value != null)
                        triggers.Add(s.Value);
                }
                break;
            case YamlMappingNode map:
                foreach (var kv in map.Children)
                {
                    if (kv.Key is YamlScalarNode key && key.Value != null)
                        triggers.Add(key.Value);
                }
                break;
        }

        return triggers.Count > 0 ? $"Triggers: {string.Join(", ", triggers)}" : null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Azure Pipelines
    // ──────────────────────────────────────────────────────────────────────

    private static void ParseAzurePipelines(
        YamlMappingNode root, string fileName, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        var workflowId = new SymbolId($"T:ci/azure/{fileName}");

        var displayName = GetScalarValue(root, "name") ?? fileName;

        // Parse trigger
        string? triggerSummary = null;
        if (TryGetValue(root, "trigger", out var triggerNode))
        {
            triggerSummary = ParseAzureTrigger(triggerNode);
        }

        var workflowNode = CreateSymbolNode(
            workflowId, SymbolKind.CIWorkflow, displayName,
            filePath, GetStartLine(root),
            summary: triggerSummary);
        nodes.Add(workflowNode);

        // Parse stages (if present)
        if (TryGetValue(root, "stages", out var stagesNode) && stagesNode is YamlSequenceNode stagesSeq)
        {
            ParseAzureStages(stagesSeq, fileName, workflowId, filePath, nodes, edges);
            return;
        }

        // Parse jobs directly under root
        if (TryGetValue(root, "jobs", out var jobsNode) && jobsNode is YamlSequenceNode jobsSeq)
        {
            ParseAzureJobs(jobsSeq, fileName, workflowId, filePath, nodes, edges);
            return;
        }

        // Parse steps directly under root (single-job shorthand)
        if (TryGetValue(root, "steps", out var stepsNode) && stepsNode is YamlSequenceNode stepsSeq)
        {
            var jobName = "default";
            var jobId = new SymbolId($"T:ci/azure/{fileName}::Job/{jobName}");
            var jobNode = CreateSymbolNode(jobId, SymbolKind.CIJob, jobName, filePath, GetStartLine(root));
            nodes.Add(jobNode);
            edges.Add(new SymbolEdge(workflowId, jobId, SymbolEdgeKind.Contains));

            ParseAzureSteps(stepsSeq, fileName, jobName, jobId, filePath, nodes, edges);
        }
    }

    private static void ParseAzureStages(
        YamlSequenceNode stagesSeq, string fileName, SymbolId workflowId, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        foreach (var stageItem in stagesSeq.Children)
        {
            if (stageItem is not YamlMappingNode stageMapping)
                continue;

            var stageName = GetScalarValue(stageMapping, "stage") ?? "unknown";
            var stageId = new SymbolId($"T:ci/azure/{fileName}::Job/{stageName}");
            var stageDisplayName = GetScalarValue(stageMapping, "displayName") ?? stageName;

            var stageNode = CreateSymbolNode(
                stageId, SymbolKind.CIJob, stageDisplayName,
                filePath, GetStartLine(stageMapping));
            nodes.Add(stageNode);

            // Workflow Contains Stage
            edges.Add(new SymbolEdge(workflowId, stageId, SymbolEdgeKind.Contains));

            // dependsOn
            if (TryGetValue(stageMapping, "dependsOn", out var depNode))
            {
                foreach (var dep in GetScalarList(depNode))
                {
                    var depId = new SymbolId($"T:ci/azure/{fileName}::Job/{dep}");
                    edges.Add(new SymbolEdge(stageId, depId, SymbolEdgeKind.DependsOn));
                }
            }

            // Jobs within stage
            if (TryGetValue(stageMapping, "jobs", out var jobsNode) && jobsNode is YamlSequenceNode jobsSeq)
            {
                ParseAzureJobs(jobsSeq, fileName, stageId, filePath, nodes, edges);
            }
        }
    }

    private static void ParseAzureJobs(
        YamlSequenceNode jobsSeq, string fileName, SymbolId parentId, string filePath,
        List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        foreach (var jobItem in jobsSeq.Children)
        {
            if (jobItem is not YamlMappingNode jobMapping)
                continue;

            var jobName = GetScalarValue(jobMapping, "job") ?? "unknown";
            var jobId = new SymbolId($"T:ci/azure/{fileName}::Job/{jobName}");
            var jobDisplayName = GetScalarValue(jobMapping, "displayName") ?? jobName;

            var jobNode = CreateSymbolNode(
                jobId, SymbolKind.CIJob, jobDisplayName,
                filePath, GetStartLine(jobMapping));
            nodes.Add(jobNode);

            // Parent Contains Job
            edges.Add(new SymbolEdge(parentId, jobId, SymbolEdgeKind.Contains));

            // dependsOn
            if (TryGetValue(jobMapping, "dependsOn", out var depNode))
            {
                foreach (var dep in GetScalarList(depNode))
                {
                    var depId = new SymbolId($"T:ci/azure/{fileName}::Job/{dep}");
                    edges.Add(new SymbolEdge(jobId, depId, SymbolEdgeKind.DependsOn));
                }
            }

            // Steps
            if (TryGetValue(jobMapping, "steps", out var stepsNode) && stepsNode is YamlSequenceNode stepsSeq)
            {
                ParseAzureSteps(stepsSeq, fileName, jobName, jobId, filePath, nodes, edges);
            }
        }
    }

    private static void ParseAzureSteps(
        YamlSequenceNode stepsSeq, string fileName, string jobName, SymbolId jobId,
        string filePath, List<SymbolNode> nodes, List<SymbolEdge> edges)
    {
        for (var i = 0; i < stepsSeq.Children.Count; i++)
        {
            if (stepsSeq.Children[i] is not YamlMappingNode stepMapping)
                continue;

            var stepDisplayName = GetScalarValue(stepMapping, "displayName");
            var task = GetScalarValue(stepMapping, "task");
            var script = GetScalarValue(stepMapping, "script")
                         ?? GetScalarValue(stepMapping, "bash")
                         ?? GetScalarValue(stepMapping, "powershell");

            var stepLabel = stepDisplayName ?? task ?? $"step{i}";
            var safeName = SanitizeName(stepLabel);
            var stepId = new SymbolId($"T:ci/azure/{fileName}::Job/{jobName}::Step/{i}_{safeName}");

            var stepNode = CreateSymbolNode(
                stepId, SymbolKind.CIStep, stepLabel,
                filePath, GetStartLine(stepMapping));
            nodes.Add(stepNode);

            // Job Contains Step
            edges.Add(new SymbolEdge(jobId, stepId, SymbolEdgeKind.Contains));

            // task: → Invokes
            if (task != null)
            {
                var targetId = new SymbolId($"T:ci/task/{task}");
                edges.Add(new SymbolEdge(stepId, targetId, SymbolEdgeKind.Invokes));
            }

            // script/bash/powershell → scan for dotnet commands
            if (script != null)
            {
                foreach (var cmd in ExtractDotnetCommands(script))
                {
                    var cmdId = new SymbolId($"T:ci/cmd/{cmd}");
                    edges.Add(new SymbolEdge(stepId, cmdId, SymbolEdgeKind.Invokes));
                }
            }
        }
    }

    private static string? ParseAzureTrigger(YamlNode triggerNode)
    {
        var triggers = new List<string>();

        switch (triggerNode)
        {
            case YamlScalarNode scalar:
                if (scalar.Value != null) triggers.Add(scalar.Value);
                break;
            case YamlSequenceNode seq:
                foreach (var item in seq.Children)
                {
                    if (item is YamlScalarNode s && s.Value != null)
                        triggers.Add(s.Value);
                }
                break;
            case YamlMappingNode map:
                // e.g., trigger: { branches: { include: [main] } }
                triggers.Add("complex");
                break;
        }

        return triggers.Count > 0 ? $"Triggers: {string.Join(", ", triggers)}" : null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static SymbolNode CreateSymbolNode(
        SymbolId id, SymbolKind kind, string displayName,
        string filePath, int startLine,
        string? summary = null, string? remarks = null)
    {
        DocComment? docs = (summary != null || remarks != null)
            ? new DocComment(
                Summary: summary,
                Remarks: remarks,
                Params: new Dictionary<string, string>(),
                TypeParams: new Dictionary<string, string>(),
                Returns: null,
                Examples: [],
                Exceptions: [],
                SeeAlso: [])
            : null;

        var span = new SourceSpan(filePath, startLine, 0, startLine, 0);

        return new SymbolNode(
            Id: id,
            Kind: kind,
            DisplayName: displayName,
            FullyQualifiedName: id.Value,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: docs,
            Span: span,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: []);
    }

    private static bool TryGetValue(YamlMappingNode map, string key, out YamlNode value)
    {
        foreach (var kv in map.Children)
        {
            if (kv.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                value = kv.Value;
                return true;
            }
        }
        value = null!;
        return false;
    }

    private static string? GetScalarValue(YamlMappingNode map, string key)
    {
        return TryGetValue(map, key, out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }

    private static List<string> GetScalarList(YamlNode node)
    {
        var result = new List<string>();
        switch (node)
        {
            case YamlScalarNode scalar when scalar.Value != null:
                result.Add(scalar.Value);
                break;
            case YamlSequenceNode seq:
                foreach (var item in seq.Children)
                {
                    if (item is YamlScalarNode s && s.Value != null)
                        result.Add(s.Value);
                }
                break;
        }
        return result;
    }

    private static int GetStartLine(YamlNode node)
    {
        // YamlDotNet uses 1-based line numbers in the Mark.
        return node.Start.Line > 0 ? (int)node.Start.Line : 1;
    }

    private static IEnumerable<string> ExtractDotnetCommands(string script)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DotnetCommandRegex.Matches(script))
        {
            var cmd = match.Groups[1].Value.Trim();
            if (seen.Add(cmd))
                yield return $"dotnet {cmd}";
        }
    }

    private static string SanitizeName(string name)
    {
        // Replace characters that would be problematic in SymbolIds.
        return name
            .Replace(' ', '_')
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace(':', '_');
    }

    [GeneratedRegex(@"dotnet\s+(build|test|restore|publish|run|pack|clean|format|tool)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreateDotnetCommandRegex();
}
