using System.Text.Json.Serialization;

namespace DocAgent.Core;

public enum SymbolKind
{
    // ── C# language symbols (existing) ──────────────────────────────
    Namespace,
    Type,
    Method,
    Property,
    Field,
    Event,
    Parameter,
    Constructor,
    Delegate,
    Indexer,
    Operator,
    Destructor,
    EnumMember,
    TypeParameter,

    // ── Tools & scripts (Phase A–E) ─────────────────────────────────
    /// <summary>A CLI tool entry from dotnet-tools.json.</summary>
    Tool,
    /// <summary>An MSBuild target from .targets/.props files.</summary>
    BuildTarget,
    /// <summary>An MSBuild property definition.</summary>
    BuildProperty,
    /// <summary>An MSBuild custom task reference.</summary>
    BuildTask,
    /// <summary>A script file (.ps1, .sh, .bash).</summary>
    Script,
    /// <summary>A function defined within a script file.</summary>
    ScriptFunction,
    /// <summary>A parameter of a script or script function.</summary>
    ScriptParameter,
    /// <summary>A CI/CD workflow file (GitHub Actions, Azure Pipelines, etc.).</summary>
    CIWorkflow,
    /// <summary>A job within a CI/CD workflow.</summary>
    CIJob,
    /// <summary>A step within a CI/CD job.</summary>
    CIStep,
    /// <summary>A Dockerfile stage (FROM ... AS stage).</summary>
    DockerStage,
    /// <summary>A Dockerfile instruction (RUN, COPY, etc.).</summary>
    DockerInstruction
}

public enum Accessibility
{
    Public,
    Internal,
    Protected,
    Private,
    ProtectedInternal,
    PrivateProtected
}

public readonly record struct SymbolId([property: JsonPropertyName("value")] string Value);

public sealed record SourceSpan(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("startLine")] int StartLine,
    [property: JsonPropertyName("startColumn")] int StartColumn,
    [property: JsonPropertyName("endLine")] int EndLine,
    [property: JsonPropertyName("endColumn")] int EndColumn);

/// <summary>Structured parameter information for methods, indexers, and delegates.</summary>
public sealed record ParameterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeName")] string TypeName,
    [property: JsonPropertyName("defaultValue")] string? DefaultValue,
    [property: JsonPropertyName("isParams")] bool IsParams,
    [property: JsonPropertyName("isRef")] bool IsRef,
    [property: JsonPropertyName("isOut")] bool IsOut,
    [property: JsonPropertyName("isIn")] bool IsIn);

/// <summary>A generic type parameter constraint (e.g., "where T : class, IDisposable").</summary>
public sealed record GenericConstraint(
    [property: JsonPropertyName("typeParameterName")] string TypeParameterName,
    [property: JsonPropertyName("constraints")] IReadOnlyList<string> Constraints);

public sealed record DocComment(
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("remarks")] string? Remarks,
    [property: JsonPropertyName("params")] IReadOnlyDictionary<string, string> Params,
    [property: JsonPropertyName("typeParams")] IReadOnlyDictionary<string, string> TypeParams,
    [property: JsonPropertyName("returns")] string? Returns,
    [property: JsonPropertyName("examples")] IReadOnlyList<string> Examples,
    [property: JsonPropertyName("exceptions")] IReadOnlyList<(string Type, string Description)> Exceptions,
    [property: JsonPropertyName("seeAlso")] IReadOnlyList<string> SeeAlso);

/// <summary>Classifies whether a symbol node was discovered in source or synthesized as a stub for an external reference.</summary>
public enum NodeKind
{
    /// <summary>A real symbol discovered from project source. Default value for backward compatibility.</summary>
    Real = 0,
    /// <summary>A stub node synthesized from a NuGet package or external assembly reference.</summary>
    Stub = 1
}

/// <summary>Classifies the scope of a symbol edge with respect to project boundaries.</summary>
public enum EdgeScope
{
    /// <summary>Both endpoints belong to the same project. Default value for backward compatibility.</summary>
    IntraProject = 0,
    /// <summary>Endpoints span two different projects within the same solution.</summary>
    CrossProject = 1,
    /// <summary>One endpoint is a stub node from an external assembly.</summary>
    External = 2
}

public sealed record SymbolNode(
    [property: JsonPropertyName("id")] SymbolId Id,
    [property: JsonPropertyName("kind")] SymbolKind Kind,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("fullyQualifiedName")] string? FullyQualifiedName,
    [property: JsonPropertyName("previousIds")] IReadOnlyList<SymbolId> PreviousIds,
    [property: JsonPropertyName("accessibility")] Accessibility Accessibility,
    [property: JsonPropertyName("docComment")] DocComment? Docs,
    [property: JsonPropertyName("span")] SourceSpan? Span,
    [property: JsonPropertyName("returnType")] string? ReturnType,
    [property: JsonPropertyName("parameters")] IReadOnlyList<ParameterInfo> Parameters,
    [property: JsonPropertyName("genericConstraints")] IReadOnlyList<GenericConstraint> GenericConstraints,
    [property: JsonPropertyName("projectOrigin")] string? ProjectOrigin = null,
    [property: JsonPropertyName("nodeKind")] NodeKind NodeKind = NodeKind.Real);

public enum SymbolEdgeKind
{
    // ── Existing C# relationships ───────────────────────────────────
    Contains,
    Inherits,
    Implements,
    Calls,
    References,
    Overrides,
    Returns,

    // ── Cross-language / tooling relationships (Phase A–E) ──────────
    /// <summary>A script/tool invokes a C# type, method, or another script.</summary>
    Invokes,
    /// <summary>A script/build target configures a resource or setting.</summary>
    Configures,
    /// <summary>A build target or CI step depends on another target/step.</summary>
    DependsOn,
    /// <summary>A CI step triggers another workflow or job.</summary>
    Triggers,
    /// <summary>A script imports or sources another script/module.</summary>
    Imports
}

public sealed record SymbolEdge(
    [property: JsonPropertyName("sourceId")] SymbolId From,
    [property: JsonPropertyName("targetId")] SymbolId To,
    [property: JsonPropertyName("kind")] SymbolEdgeKind Kind,
    [property: JsonPropertyName("scope")] EdgeScope Scope = EdgeScope.IntraProject);

public sealed record SymbolGraphSnapshot(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("sourceFingerprint")] string SourceFingerprint,
    [property: JsonPropertyName("contentHash")] string? ContentHash,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("nodes")] IReadOnlyList<SymbolNode> Nodes,
    [property: JsonPropertyName("edges")] IReadOnlyList<SymbolEdge> Edges,
    [property: JsonPropertyName("ingestionMetadata")] IngestionMetadata? IngestionMetadata = null,
    [property: JsonPropertyName("solutionName")] string? SolutionName = null);

public enum SerializationFormat
{
    MessagePack,
    Json,
    Tron
}
