using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DocAgent.Analyzers.Coverage;

/// <summary>
/// Reports DOCAGENT003 when documentation coverage of public symbols falls below
/// a configurable threshold (default 80%).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocCoverageAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "DOCAGENT003";
    private const int DefaultThreshold = 80;

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Documentation coverage below threshold",
        messageFormat: "Documentation coverage is {0}/{1} ({2}% of public symbols documented, threshold is {3}%). Undocumented: {4}",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var totalPublic = 0;
            var undocumented = new ConcurrentBag<string>();

            compilationContext.RegisterSymbolAction(symbolContext =>
            {
                var symbol = symbolContext.Symbol;

                if (symbol.IsImplicitlyDeclared)
                    return;

                if (!IsPubliclyVisible(symbol))
                    return;

                if (HasExcludeAttribute(symbol))
                    return;

                Interlocked.Increment(ref totalPublic);

                var xml = symbol.GetDocumentationCommentXml();
                if (xml == null || !xml.Contains("<summary>"))
                {
                    undocumented.Add(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                }
            },
            SymbolKind.NamedType,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Event,
            SymbolKind.Field);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                if (totalPublic == 0)
                    return;

                var threshold = DefaultThreshold;
                if (endContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                    .TryGetValue("build_property.DocCoverageThreshold", out var thresholdStr)
                    && int.TryParse(thresholdStr, out var parsed))
                {
                    threshold = parsed;
                }

                var documented = totalPublic - undocumented.Count;
                var coverage = documented * 100.0 / totalPublic;

                if (coverage < threshold)
                {
                    var names = string.Join(", ", undocumented.Take(20));
                    endContext.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        Location.None,
                        documented,
                        totalPublic,
                        Math.Round(coverage, 1),
                        threshold,
                        names));
                }
            });
        });
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
