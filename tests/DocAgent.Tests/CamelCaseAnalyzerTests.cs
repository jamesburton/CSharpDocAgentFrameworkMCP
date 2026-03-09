using DocAgent.Indexing;
using FluentAssertions;
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
}
