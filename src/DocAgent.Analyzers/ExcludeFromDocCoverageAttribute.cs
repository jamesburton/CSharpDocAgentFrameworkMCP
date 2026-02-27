using System;
using System.Diagnostics;

namespace DocAgent.Analyzers;

/// <summary>
/// Excludes the annotated symbol from all DocAgent analyzer diagnostics
/// (DOCAGENT001, DOCAGENT002, DOCAGENT003).
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property |
    AttributeTargets.Event | AttributeTargets.Field,
    Inherited = false)]
[Conditional("DOCAGENT_ANALYZERS")]
public sealed class ExcludeFromDocCoverageAttribute : Attribute
{
}
