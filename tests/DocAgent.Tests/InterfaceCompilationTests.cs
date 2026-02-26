using System.Reflection;
using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests;

/// <summary>
/// CORE-03 compile-time contract verification.
/// These tests verify that all six domain interfaces exist and are usable.
/// If any interface has a breaking signature change, this file will not compile.
/// </summary>
public class InterfaceCompilationTests
{
    [Fact]
    public void All_six_interfaces_are_assignable()
    {
        // Compile-time check: if any interface is missing or renamed, this will not compile.
        IProjectSource? projectSource = null;
        IDocSource? docSource = null;
        ISymbolGraphBuilder? graphBuilder = null;
        ISearchIndex? searchIndex = null;
        IKnowledgeQueryService? queryService = null;
        IVectorIndex? vectorIndex = null;

        projectSource.Should().BeNull();
        docSource.Should().BeNull();
        graphBuilder.Should().BeNull();
        searchIndex.Should().BeNull();
        queryService.Should().BeNull();
        vectorIndex.Should().BeNull();
    }

    [Fact]
    public void SearchToListAsync_extension_method_exists()
    {
        var method = typeof(SearchIndexExtensions)
            .GetMethod(nameof(SearchIndexExtensions.SearchToListAsync),
                BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull("SearchToListAsync extension method must exist on SearchIndexExtensions");
        method!.ReturnType.Should().Be(typeof(Task<IReadOnlyList<SearchHit>>));
    }

    [Fact]
    public void IKnowledgeQueryService_has_GetReferencesAsync()
    {
        var method = typeof(IKnowledgeQueryService)
            .GetMethod(nameof(IKnowledgeQueryService.GetReferencesAsync));

        method.Should().NotBeNull("IKnowledgeQueryService must have GetReferencesAsync method");

        var returnType = method!.ReturnType;
        returnType.IsGenericType.Should().BeTrue();
        returnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
        returnType.GetGenericArguments()[0].Should().Be(typeof(SymbolEdge));
    }
}
