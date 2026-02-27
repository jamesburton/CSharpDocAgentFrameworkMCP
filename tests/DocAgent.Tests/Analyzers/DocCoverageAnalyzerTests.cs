using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using DocAgent.Analyzers.Coverage;
using Xunit;

namespace DocAgent.Tests.Analyzers;

public class DocCoverageAnalyzerTests
{
    [Fact]
    public async Task HalfDocumented_BelowThreshold_ReportsDOCAGENT003()
    {
        var source = @"
/// <summary>Documented class.</summary>
public class DocumentedClass { }

public class UndocumentedClass { }
";
        var test = new CSharpAnalyzerTest<DocCoverageAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        // 1 of 2 documented = 50% < 80% threshold
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DocCoverageAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                .WithArguments(1, 2, 50.0, 80, "UndocumentedClass"));
        await test.RunAsync();
    }

    [Fact]
    public async Task AllDocumented_NoDiagnostic()
    {
        var source = @"
/// <summary>Documented class A.</summary>
public class ClassA { }

/// <summary>Documented class B.</summary>
public class ClassB { }
";
        var test = new CSharpAnalyzerTest<DocCoverageAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task CustomThreshold30_HalfDocumented_NoDiagnostic()
    {
        var source = @"
/// <summary>Documented class.</summary>
public class DocumentedClass { }

public class UndocumentedClass { }
";
        var test = new CSharpAnalyzerTest<DocCoverageAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", @"
is_global = true
build_property.DocCoverageThreshold = 30
"));
        // 50% > 30% threshold → no diagnostic
        await test.RunAsync();
    }
}
