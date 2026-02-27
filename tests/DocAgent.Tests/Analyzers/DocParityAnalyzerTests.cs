using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using DocAgent.Analyzers.DocParity;
using Xunit;

namespace DocAgent.Tests.Analyzers;

public class DocParityAnalyzerTests
{
    [Fact]
    public async Task PublicClassWithoutSummary_ReportsDOCAGENT001()
    {
        var source = @"
public class {|#0:MyClass|}
{
}";
        var test = new CSharpAnalyzerTest<DocParityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(DocParityAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("MyClass"),
            },
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PublicClassWithSummary_NoDiagnostic()
    {
        var source = @"
/// <summary>A documented class.</summary>
public class MyClass
{
}";
        var test = new CSharpAnalyzerTest<DocParityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task InternalClassWithoutSummary_NoDiagnostic()
    {
        var source = @"
internal class MyClass
{
}";
        var test = new CSharpAnalyzerTest<DocParityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PublicClassWithExcludeAttribute_NoDiagnostic()
    {
        var source = @"
using System;
using System.Diagnostics;

/// <summary>Excludes from doc coverage.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Field, Inherited = false)]
[Conditional(""DOCAGENT_ANALYZERS"")]
public sealed class ExcludeFromDocCoverageAttribute : Attribute { }

[ExcludeFromDocCoverage]
public class MyClass
{
}";
        var test = new CSharpAnalyzerTest<DocParityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }
}
