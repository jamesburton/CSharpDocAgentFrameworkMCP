using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DocAgent.Analyzers.SuspiciousEdit;

/// <summary>
/// Reports DOCAGENT002 when a public symbol has indicators of semantic change
/// but the XML documentation appears not to reflect those changes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SuspiciousEditAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DOCAGENT002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Semantic change without documentation update",
        messageFormat: "'{0}' has a semantic change indicator but XML documentation appears unchanged",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeSymbol,
            SymbolKind.Method,
            SymbolKind.Property);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        if (symbol.IsImplicitlyDeclared)
            return;

        if (!IsPubliclyVisible(symbol))
            return;

        if (HasExcludeAttribute(symbol))
            return;

        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
            return; // No docs at all — DocParityAnalyzer handles that

        // Heuristic 1: [Obsolete] attribute but doc doesn't mention "obsolete" or "deprecated"
        if (HasObsoleteWithoutDocMention(symbol, xml!))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, symbol.Locations[0], symbol.Name));
            return;
        }

        // Heuristic 2: Nullability attributes on parameters without doc mention
        if (symbol is IMethodSymbol method && HasNullabilityMismatch(method, xml!))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, symbol.Locations[0], symbol.Name));
            return;
        }
    }

    private static bool HasObsoleteWithoutDocMention(ISymbol symbol, string xml)
    {
        var hasObsolete = symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "ObsoleteAttribute" or "Obsolete");

        if (!hasObsolete)
            return false;

        var lowerXml = xml.ToLowerInvariant();
        return !lowerXml.Contains("obsolete") && !lowerXml.Contains("deprecated");
    }

    private static bool HasNullabilityMismatch(IMethodSymbol method, string xml)
    {
        var nullabilityAttrs = new[] { "NotNullAttribute", "MaybeNullAttribute", "AllowNullAttribute" };
        var lowerXml = xml.ToLowerInvariant();

        foreach (var param in method.Parameters)
        {
            var hasNullAttr = param.GetAttributes().Any(a =>
                a.AttributeClass != null && nullabilityAttrs.Contains(a.AttributeClass.Name));

            if (!hasNullAttr)
                continue;

            // Check if <param name="paramName"> text mentions null/nullable
            if (!lowerXml.Contains("null"))
                return true;
        }

        return false;
    }

    private static bool IsPubliclyVisible(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility is
            Accessibility.Public or
            Accessibility.Protected or
            Accessibility.ProtectedOrInternal;
    }

    private static bool HasExcludeAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "ExcludeFromDocCoverageAttribute");
    }
}
