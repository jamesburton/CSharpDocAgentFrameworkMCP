using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using DocAgent.Analyzers.SuspiciousEdit;
using Xunit;

namespace DocAgent.Tests.Analyzers;

public class SuspiciousEditAnalyzerTests
{
    [Fact]
    public async Task ObsoleteMethodWithoutDocMention_ReportsDOCAGENT002()
    {
        var source = @"
using System;

public class MyClass
{
    /// <summary>Does something useful.</summary>
    [Obsolete]
    public void {|#0:DoWork|}() { }
}";
        var test = new CSharpAnalyzerTest<SuspiciousEditAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ExpectedDiagnostics =
            {
                new DiagnosticResult(SuspiciousEditAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithArguments("DoWork"),
            },
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task ObsoleteMethodWithDocMention_NoDiagnostic()
    {
        var source = @"
using System;

public class MyClass
{
    /// <summary>This method is obsolete. Use DoWorkV2 instead.</summary>
    [Obsolete]
    public void DoWork() { }
}";
        var test = new CSharpAnalyzerTest<SuspiciousEditAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task CleanPublicMethod_NoDiagnostic()
    {
        var source = @"
public class MyClass
{
    /// <summary>Does something useful.</summary>
    public void DoWork() { }
}";
        var test = new CSharpAnalyzerTest<SuspiciousEditAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        await test.RunAsync();
    }
}
