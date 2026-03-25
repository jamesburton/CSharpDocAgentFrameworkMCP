using DocAgent.Core;
using DocAgent.Indexing;
using FluentAssertions;
using Lucene.Net.Store;
using Xunit;

namespace DocAgent.Tests;

public class CamelCaseAnalyzerTests
{
    [Theory]
    [InlineData("GetReferences", "get", "references")]
    [InlineData("getReferences", "get", "references")]
    [InlineData("XMLParser", "xml", "parser")]
    [InlineData("myMethodName", "my", "method", "name")]
    [InlineData("SimpleClass", "simple", "class")]
    [InlineData("createHTTPServer", "create", "http", "server")]
    public void CamelCaseAnalyzer_splits_correctly(string input, params string[] expectedTokens)
    {
        // Arrange
        using var analyzer = new CamelCaseAnalyzer();
        var tokens = new List<string>();

        // Act
        using var stream = analyzer.GetTokenStream("test", input);
        var attr = stream.AddAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken())
        {
            tokens.Add(new string(attr.Buffer, 0, attr.Length));
        }
        stream.End();

        // Assert
        // CamelCaseAnalyzer emits original token + parts if parts.Length > 1
        // So for "getReferences", it should emit ["getreferences", "get", "references"]
        tokens.Should().Contain(input.ToLowerInvariant());
        foreach (var expected in expectedTokens)
        {
            tokens.Should().Contain(expected);
        }
    }

    [Fact]
    public async Task BM25Search_FindsTypeScriptCamelCaseSymbol()
    {
        // Integration test: verify camelCase sub-word matching works end-to-end through the BM25 index.
        // This tests MCPI-04: BM25 search returns a TypeScript camelCase symbol when queried by sub-word.
        using var index = new BM25SearchIndex(new RAMDirectory());

        var createHttpServerId = new SymbolId("M:ts-test:index.ts:createHTTPServer");
        var node = new SymbolNode(
            Id: createHttpServerId,
            Kind: SymbolKind.Method,
            DisplayName: "createHTTPServer",
            FullyQualifiedName: "ts-test.createHTTPServer",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: new DocComment(
                "Creates an HTTP server",
                null,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                null, [], [], []),
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: [],
            ProjectOrigin: "ts-test");

        var snapshot = new SymbolGraphSnapshot(
            "1.0", "ts-test", "fp-camel", null, DateTimeOffset.UtcNow, [node], []);

        await index.IndexAsync(snapshot, CancellationToken.None);

        // Search by sub-word "create"
        var createHits = new List<SearchHit>();
        await foreach (var hit in index.SearchAsync("create", CancellationToken.None))
            createHits.Add(hit);

        createHits.Should().Contain(h => h.Id == createHttpServerId,
            "BM25 index should find 'createHTTPServer' when searching by sub-word 'create'");

        // Search by sub-word "http"
        var httpHits = new List<SearchHit>();
        await foreach (var hit in index.SearchAsync("http", CancellationToken.None))
            httpHits.Add(hit);

        httpHits.Should().Contain(h => h.Id == createHttpServerId,
            "BM25 index should find 'createHTTPServer' when searching by acronym sub-word 'http'");

        // Search by sub-word "server"
        var serverHits = new List<SearchHit>();
        await foreach (var hit in index.SearchAsync("server", CancellationToken.None))
            serverHits.Add(hit);

        serverHits.Should().Contain(h => h.Id == createHttpServerId,
            "BM25 index should find 'createHTTPServer' when searching by sub-word 'server'");
    }
}
