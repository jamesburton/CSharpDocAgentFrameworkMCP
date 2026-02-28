using System.Security.Cryptography;
using System.Text;
using DocAgent.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using CoreSymbolKind = DocAgent.Core.SymbolKind;

namespace DocAgent.Ingestion;

/// <summary>
/// Implements <see cref="ISymbolGraphBuilder"/> using Roslyn's MSBuildWorkspace and Compilation APIs.
/// Walks namespaces, types, and members recursively; extracts XML documentation; builds semantic edges;
/// and produces a deterministically-ordered <see cref="SymbolGraphSnapshot"/>.
/// </summary>
public sealed class RoslynSymbolGraphBuilder : ISymbolGraphBuilder
{
    private static readonly DocComment PlaceholderDoc = new(
        Summary: "No documentation provided.",
        Remarks: null,
        Params: new Dictionary<string, string>(),
        TypeParams: new Dictionary<string, string>(),
        Returns: null,
        Examples: Array.Empty<string>(),
        Exceptions: Array.Empty<(string, string)>(),
        SeeAlso: Array.Empty<string>());

    private readonly XmlDocParser _parser;
    private readonly InheritDocResolver _inheritDocResolver;
    private readonly Action<string>? _logWarning;

    public RoslynSymbolGraphBuilder(
        XmlDocParser parser,
        InheritDocResolver inheritDocResolver,
        Action<string>? logWarning = null)
    {
        _parser = parser;
        _inheritDocResolver = inheritDocResolver;
        _logWarning = logWarning;
    }

    /// <inheritdoc />
    public async Task<SymbolGraphSnapshot> BuildAsync(
        ProjectInventory inv,
        DocInputSet docs,
        CancellationToken ct)
    {
        var allNodes = new List<SymbolNode>();
        var allEdges = new List<SymbolEdge>();

        // Process projects one at a time to bound memory usage.
        // MSBuildWorkspace is created per-project and disposed immediately after use
        // so that Roslyn compilation objects are released (avoids unbounded memory retention).
        foreach (var projectFile in inv.ProjectFiles)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessProjectAsync(projectFile, allNodes, allEdges, ct).ConfigureAwait(false);
        }

        var fingerprint = ComputeSourceFingerprint(inv.ProjectFiles);
        var projectName = inv.SolutionFiles.Count > 0
            ? Path.GetFileNameWithoutExtension(inv.SolutionFiles[0])
            : inv.ProjectFiles.Count > 0
                ? Path.GetFileNameWithoutExtension(inv.ProjectFiles[0])
                : "Unknown";

        return new SymbolGraphSnapshot(
            SchemaVersion: "1.0",
            ProjectName: projectName,
            SourceFingerprint: fingerprint,
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: SymbolSorter.SortNodes(allNodes),
            Edges: SymbolSorter.SortEdges(allEdges));
    }

    // ── Per-project processing ───────────────────────────────────────────────

    private async Task ProcessProjectAsync(
        string projectFile,
        List<SymbolNode> allNodes,
        List<SymbolEdge> allEdges,
        CancellationToken ct)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
            _logWarning?.Invoke($"MSBuildWorkspace [{args.Diagnostic.Kind}]: {args.Diagnostic.Message}");

        Project project;
        try
        {
            project = await workspace.OpenProjectAsync(projectFile, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logWarning?.Invoke($"Failed to open project '{projectFile}': {ex.Message}");
            return;
        }

        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            _logWarning?.Invoke($"Could not obtain compilation for project: {projectFile}");
            return;
        }

        try
        {
            // Build a lookup table of doc-comment-id → raw XML for InheritDoc resolution
            var docXmlById = BuildDocXmlLookup(compilation);

            WalkNamespace(compilation.GlobalNamespace, allNodes, allEdges, docXmlById);
        }
        finally
        {
            // Release compilation reference to allow GC to reclaim memory
            compilation = null;
        }
    }

    // ── Namespace walker ─────────────────────────────────────────────────────

    private void WalkNamespace(
        INamespaceSymbol ns,
        List<SymbolNode> nodes,
        List<SymbolEdge> edges,
        IReadOnlyDictionary<string, string> docXmlById)
    {
        // Walk all members in deterministic doc-comment-id order
        var members = ns.GetMembers()
            .OrderBy(m => m.GetDocumentationCommentId() ?? m.ToDisplayString(), StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (member is INamespaceSymbol childNs)
            {
                // Namespace-level containment: add the child namespace node and edge
                var nsNode = CreateNamespaceNode(childNs, docXmlById);
                if (nsNode is not null)
                {
                    var parentId = GetSymbolId(ns);
                    // Only add Contains edge if parent is a non-global namespace
                    if (!ns.IsGlobalNamespace && IsKnownNode(nodes, parentId))
                    {
                        edges.Add(new SymbolEdge(parentId, nsNode.Id, SymbolEdgeKind.Contains));
                    }
                    nodes.Add(nsNode);
                }

                WalkNamespace(childNs, nodes, edges, docXmlById);
            }
            else if (member is INamedTypeSymbol typeSymbol)
            {
                if (!IsIncluded(typeSymbol.DeclaredAccessibility))
                    continue;

                var typeNode = CreateSymbolNode(typeSymbol, docXmlById);
                nodes.Add(typeNode);

                // Contains edge from namespace (skip global namespace)
                if (!ns.IsGlobalNamespace)
                {
                    var nsId = GetSymbolId(ns);
                    edges.Add(new SymbolEdge(nsId, typeNode.Id, SymbolEdgeKind.Contains));
                }

                WalkType(typeSymbol, typeNode.Id, nodes, edges, docXmlById);
            }
        }
    }

    // ── Type walker ──────────────────────────────────────────────────────────

    private void WalkType(
        INamedTypeSymbol type,
        SymbolId typeId,
        List<SymbolNode> nodes,
        List<SymbolEdge> edges,
        IReadOnlyDictionary<string, string> docXmlById)
    {
        // Inheritance edge
        if (type.BaseType is not null &&
            type.BaseType.SpecialType != SpecialType.System_Object &&
            type.BaseType.SpecialType != SpecialType.None || type.BaseType?.SpecialType == SpecialType.None)
        {
            if (type.BaseType is not null && type.BaseType.SpecialType != SpecialType.System_Object)
            {
                var baseId = GetSymbolId(type.BaseType);
                edges.Add(new SymbolEdge(typeId, baseId, SymbolEdgeKind.Inherits));
            }
        }

        // Interface implementation edges
        foreach (var iface in type.Interfaces)
        {
            var ifaceId = GetSymbolId(iface);
            edges.Add(new SymbolEdge(typeId, ifaceId, SymbolEdgeKind.Implements));
        }

        // Nested types (recursive, full depth)
        var nestedTypes = type.GetTypeMembers()
            .OrderBy(t => t.GetDocumentationCommentId() ?? t.ToDisplayString(), StringComparer.Ordinal);

        foreach (var nested in nestedTypes)
        {
            if (!IsIncluded(nested.DeclaredAccessibility))
                continue;

            var nestedNode = CreateSymbolNode(nested, docXmlById);
            nodes.Add(nestedNode);
            edges.Add(new SymbolEdge(typeId, nestedNode.Id, SymbolEdgeKind.Contains));
            WalkType(nested, nestedNode.Id, nodes, edges, docXmlById);
        }

        // Members: methods, properties, fields, events, constructors, operators, indexers
        var members = type.GetMembers()
            .Where(m => m is not INamedTypeSymbol) // nested types handled above
            .OrderBy(m => m.GetDocumentationCommentId() ?? m.ToDisplayString(), StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (!IsIncluded(member.DeclaredAccessibility))
                continue;

            // Skip auto-generated backing fields/methods (compiler-synthesized)
            if (member.IsImplicitlyDeclared)
                continue;

            var memberNode = CreateSymbolNode(member, docXmlById);
            nodes.Add(memberNode);
            edges.Add(new SymbolEdge(typeId, memberNode.Id, SymbolEdgeKind.Contains));

            // Additional semantic edges per member kind
            switch (member)
            {
                case IMethodSymbol method:
                    AddMethodEdges(method, memberNode.Id, edges);
                    break;

                case IPropertySymbol property:
                    AddTypeReferenceEdge(property.Type, memberNode.Id, edges);
                    break;

                case IFieldSymbol field:
                    AddTypeReferenceEdge(field.Type, memberNode.Id, edges);
                    break;

                case IEventSymbol evt:
                    if (evt.Type is not null)
                        AddTypeReferenceEdge(evt.Type, memberNode.Id, edges);
                    break;
            }
        }
    }

    // ── Method semantic edges ────────────────────────────────────────────────

    private void AddMethodEdges(IMethodSymbol method, SymbolId methodId, List<SymbolEdge> edges)
    {
        // Override edge
        if (method.OverriddenMethod is not null)
        {
            var overriddenId = GetSymbolId(method.OverriddenMethod);
            edges.Add(new SymbolEdge(methodId, overriddenId, SymbolEdgeKind.Overrides));
        }

        // Return type reference
        if (method.ReturnType.SpecialType != SpecialType.System_Void &&
            method.MethodKind != MethodKind.Constructor &&
            method.MethodKind != MethodKind.StaticConstructor)
        {
            AddTypeReferenceEdge(method.ReturnType, methodId, edges);
        }

        // Parameter type references
        foreach (var param in method.Parameters)
        {
            AddTypeReferenceEdge(param.Type, methodId, edges);
        }
    }

    private static void AddTypeReferenceEdge(ITypeSymbol type, SymbolId fromId, List<SymbolEdge> edges)
    {
        // Skip primitive/special types to avoid noise
        if (type.SpecialType != SpecialType.None)
            return;

        // Unwrap array types
        if (type is IArrayTypeSymbol arr)
        {
            AddTypeReferenceEdge(arr.ElementType, fromId, edges);
            return;
        }

        // Unwrap generic type arguments
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            foreach (var arg in named.TypeArguments)
                AddTypeReferenceEdge(arg, fromId, edges);
            return;
        }

        var toId = GetSymbolId(type);
        edges.Add(new SymbolEdge(fromId, toId, SymbolEdgeKind.References));
    }

    // ── Node creation ────────────────────────────────────────────────────────

    private SymbolNode? CreateNamespaceNode(INamespaceSymbol ns, IReadOnlyDictionary<string, string> docXmlById)
    {
        if (ns.IsGlobalNamespace)
            return null;

        var id = GetSymbolId(ns);
        return new SymbolNode(
            Id: id,
            Kind: CoreSymbolKind.Namespace,
            DisplayName: ns.Name,
            FullyQualifiedName: ns.ToDisplayString(),
            PreviousIds: Array.Empty<SymbolId>(),
            Accessibility: MapAccessibility(ns.DeclaredAccessibility),
            Docs: ResolveDoc(ns, docXmlById),
            Span: ExtractSpan(ns),
            ReturnType: null,
            Parameters: Array.Empty<Core.ParameterInfo>(),
            GenericConstraints: Array.Empty<Core.GenericConstraint>());
    }

    private SymbolNode CreateSymbolNode(ISymbol symbol, IReadOnlyDictionary<string, string> docXmlById)
    {
        var id = GetSymbolId(symbol);
        var (returnType, parameters, genericConstraints) = ExtractSignatureFields(symbol);
        return new SymbolNode(
            Id: id,
            Kind: MapKind(symbol),
            DisplayName: symbol.Name,
            FullyQualifiedName: symbol.ToDisplayString(),
            PreviousIds: Array.Empty<SymbolId>(),
            Accessibility: MapAccessibility(symbol.DeclaredAccessibility),
            Docs: ResolveDoc(symbol, docXmlById),
            Span: ExtractSpan(symbol),
            ReturnType: returnType,
            Parameters: parameters,
            GenericConstraints: genericConstraints);
    }

    private static (string? ReturnType, IReadOnlyList<Core.ParameterInfo> Parameters, IReadOnlyList<Core.GenericConstraint> GenericConstraints)
        ExtractSignatureFields(ISymbol symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol method:
            {
                string? returnType = null;
                if (method.ReturnType.SpecialType != SpecialType.System_Void &&
                    method.MethodKind != MethodKind.Constructor &&
                    method.MethodKind != MethodKind.StaticConstructor)
                {
                    returnType = method.ReturnType.ToDisplayString();
                }

                var parameters = method.Parameters
                    .Select(p => new Core.ParameterInfo(
                        Name: p.Name,
                        TypeName: p.Type.ToDisplayString(),
                        DefaultValue: p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null,
                        IsParams: p.IsParams,
                        IsRef: p.RefKind == RefKind.Ref,
                        IsOut: p.RefKind == RefKind.Out,
                        IsIn: p.RefKind == RefKind.In))
                    .ToList();

                var genericConstraints = ExtractTypeParameterConstraints(method.TypeParameters);

                return (returnType, parameters, genericConstraints);
            }

            case IPropertySymbol property:
            {
                var returnType = property.Type.ToDisplayString();
                IReadOnlyList<Core.ParameterInfo> parameters = property.IsIndexer
                    ? property.Parameters
                        .Select(p => new Core.ParameterInfo(
                            Name: p.Name,
                            TypeName: p.Type.ToDisplayString(),
                            DefaultValue: p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null,
                            IsParams: p.IsParams,
                            IsRef: p.RefKind == RefKind.Ref,
                            IsOut: p.RefKind == RefKind.Out,
                            IsIn: p.RefKind == RefKind.In))
                        .ToList()
                    : Array.Empty<Core.ParameterInfo>();
                return (returnType, parameters, Array.Empty<Core.GenericConstraint>());
            }

            case IFieldSymbol field:
                return (field.Type.ToDisplayString(), Array.Empty<Core.ParameterInfo>(), Array.Empty<Core.GenericConstraint>());

            case INamedTypeSymbol namedType:
            {
                var genericConstraints = ExtractTypeParameterConstraints(namedType.TypeParameters);
                return (null, Array.Empty<Core.ParameterInfo>(), genericConstraints);
            }

            default:
                return (null, Array.Empty<Core.ParameterInfo>(), Array.Empty<Core.GenericConstraint>());
        }
    }

    private static IReadOnlyList<Core.GenericConstraint> ExtractTypeParameterConstraints(
        System.Collections.Immutable.ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        var result = new List<Core.GenericConstraint>();
        foreach (var tp in typeParameters)
        {
            if (tp.ConstraintTypes.Length == 0 &&
                !tp.HasConstructorConstraint &&
                !tp.HasReferenceTypeConstraint &&
                !tp.HasValueTypeConstraint &&
                !tp.HasNotNullConstraint &&
                !tp.HasUnmanagedTypeConstraint)
                continue;

            var constraints = new List<string>();
            if (tp.HasReferenceTypeConstraint) constraints.Add("class");
            if (tp.HasValueTypeConstraint) constraints.Add("struct");
            if (tp.HasNotNullConstraint) constraints.Add("notnull");
            if (tp.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
            constraints.AddRange(tp.ConstraintTypes.Select(c => c.ToDisplayString()));
            if (tp.HasConstructorConstraint) constraints.Add("new()");

            result.Add(new Core.GenericConstraint(tp.Name, constraints));
        }
        return result;
    }

    // ── Documentation resolution ─────────────────────────────────────────────

    private DocComment ResolveDoc(ISymbol symbol, IReadOnlyDictionary<string, string> docXmlById)
    {
        var rawXml = symbol.GetDocumentationCommentXml();

        if (!string.IsNullOrWhiteSpace(rawXml))
        {
            var parsed = _parser.Parse(rawXml);
            if (parsed is not null)
                return parsed;
        }

        // Try InheritDoc resolution
        var inherited = _inheritDocResolver.Resolve(
            rawXml,
            cref =>
            {
                if (string.IsNullOrEmpty(cref))
                {
                    // No cref — look up natural override/interface target
                    var baseId = GetNaturalBaseDocId(symbol);
                    return baseId is not null && docXmlById.TryGetValue(baseId, out var baseXml) ? baseXml : null;
                }
                return docXmlById.TryGetValue(cref, out var xml) ? xml : null;
            },
            _parser);

        return inherited ?? PlaceholderDoc;
    }

    private static string? GetNaturalBaseDocId(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method && method.OverriddenMethod is not null)
            return method.OverriddenMethod.GetDocumentationCommentId();

        if (symbol is IPropertySymbol property && property.OverriddenProperty is not null)
            return property.OverriddenProperty.GetDocumentationCommentId();

        // Check interface implementations
        var containingType = symbol.ContainingType;
        if (containingType is not null)
        {
            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers())
                {
                    var impl = containingType.FindImplementationForInterfaceMember(member);
                    if (impl is not null && SymbolEqualityComparer.Default.Equals(impl, symbol))
                        return member.GetDocumentationCommentId();
                }
            }
        }

        return null;
    }

    // ── Generated code detection ─────────────────────────────────────────────

    private static bool IsGeneratedCode(ISymbol symbol)
    {
        // Check [GeneratedCode] attribute
        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName == "GeneratedCodeAttribute" || attrName == "GeneratedCode")
                return true;
        }

        // Check source file path for obj/ directory patterns
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is not null)
        {
            var filePath = location.SourceTree?.FilePath ?? string.Empty;
            if (filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                filePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ── Source span extraction ───────────────────────────────────────────────

    private static SourceSpan? ExtractSpan(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null)
            return null;

        var lineSpan = location.GetLineSpan();
        if (!lineSpan.IsValid)
            return null;

        // Convert 0-based Roslyn positions to 1-based line/column numbers
        return new SourceSpan(
            FilePath: lineSpan.Path,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1);
    }

    // ── Symbol ID ────────────────────────────────────────────────────────────

    private static SymbolId GetSymbolId(ISymbol symbol)
    {
        var id = symbol.GetDocumentationCommentId();
        return new SymbolId(id ?? symbol.ToDisplayString());
    }

    // ── Accessibility filter ─────────────────────────────────────────────────

    private static bool IsIncluded(Microsoft.CodeAnalysis.Accessibility accessibility)
        => accessibility is Microsoft.CodeAnalysis.Accessibility.Public
            or Microsoft.CodeAnalysis.Accessibility.Protected
            or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal;

    // ── Accessibility mapping ────────────────────────────────────────────────

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

    // ── Kind mapping ─────────────────────────────────────────────────────────

    private static CoreSymbolKind MapKind(ISymbol symbol)
        => symbol switch
        {
            INamespaceSymbol => CoreSymbolKind.Namespace,
            INamedTypeSymbol t => t.TypeKind switch
            {
                TypeKind.Delegate => CoreSymbolKind.Delegate,
                TypeKind.Enum => CoreSymbolKind.Type,
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
            ITypeParameterSymbol => CoreSymbolKind.TypeParameter,
            IParameterSymbol => CoreSymbolKind.Parameter,
            _ => CoreSymbolKind.Method
        };

    // ── Helper: lookup table ─────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> BuildDocXmlLookup(Compilation compilation)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectDocXml(compilation.GlobalNamespace, dict);
        return dict;
    }

    private static void CollectDocXml(INamespaceSymbol ns, Dictionary<string, string> dict)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    CollectDocXml(childNs, dict);
                    break;
                case INamedTypeSymbol type:
                    CollectTypeDocXml(type, dict);
                    break;
            }
        }
    }

    private static void CollectTypeDocXml(INamedTypeSymbol type, Dictionary<string, string> dict)
    {
        AddDocEntry(type, dict);
        foreach (var nested in type.GetTypeMembers())
            CollectTypeDocXml(nested, dict);
        foreach (var member in type.GetMembers().Where(m => m is not INamedTypeSymbol))
            AddDocEntry(member, dict);
    }

    private static void AddDocEntry(ISymbol symbol, Dictionary<string, string> dict)
    {
        var id = symbol.GetDocumentationCommentId();
        if (id is null) return;
        var xml = symbol.GetDocumentationCommentXml();
        if (!string.IsNullOrWhiteSpace(xml))
            dict[id] = xml;
    }

    // ── Helper: check if node already tracked ────────────────────────────────

    private static bool IsKnownNode(List<SymbolNode> nodes, SymbolId id)
        => nodes.Any(n => n.Id == id);

    // ── Source fingerprint ───────────────────────────────────────────────────

    private static string ComputeSourceFingerprint(IReadOnlyList<string> projectFiles)
    {
        // Sort files for determinism, then hash the concatenated file sizes/names
        var sorted = projectFiles.OrderBy(f => f, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var f in sorted)
        {
            sb.Append(f);
            if (File.Exists(f))
            {
                var info = new FileInfo(f);
                sb.Append(':');
                sb.Append(info.Length);
                sb.Append(':');
                sb.Append(info.LastWriteTimeUtc.Ticks);
            }
            sb.Append('|');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
