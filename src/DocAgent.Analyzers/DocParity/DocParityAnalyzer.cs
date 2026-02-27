using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DocAgent.Analyzers.DocParity;

/// <summary>
/// Reports DOCAGENT001 when a public symbol lacks an XML documentation &lt;summary&gt; tag.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocParityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DOCAGENT001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Public member lacks XML documentation",
        messageFormat: "'{0}' is public but has no <summary> documentation",
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
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Event,
            SymbolKind.Field);
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
        if (xml != null && xml.Contains("<summary>"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, symbol.Locations[0], symbol.Name));
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
