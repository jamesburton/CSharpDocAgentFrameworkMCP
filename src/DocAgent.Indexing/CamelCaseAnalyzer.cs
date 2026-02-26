using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Util;

namespace DocAgent.Indexing;

/// <summary>
/// Lucene analyzer that splits CamelCase and acronym tokens, then lowercases them.
/// Example: "GetReferences" → [getreferences, get, references]; "XMLParser" → [xmlparser, xml, parser].
/// </summary>
public sealed class CamelCaseAnalyzer : Analyzer
{
    private static readonly LuceneVersion LuceneVer = LuceneVersion.LUCENE_48;

    protected override TokenStreamComponents CreateComponents(string fieldName, System.IO.TextReader reader)
    {
        var tokenizer = new WhitespaceTokenizer(LuceneVer, reader);

        var flags =
            WordDelimiterFlags.SPLIT_ON_CASE_CHANGE |
            WordDelimiterFlags.GENERATE_WORD_PARTS |
            WordDelimiterFlags.GENERATE_NUMBER_PARTS |
            WordDelimiterFlags.PRESERVE_ORIGINAL;

        TokenStream filter = new WordDelimiterFilter(LuceneVer, tokenizer, flags, null);
        filter = new LowerCaseFilter(LuceneVer, filter);

        return new TokenStreamComponents(tokenizer, filter);
    }
}
