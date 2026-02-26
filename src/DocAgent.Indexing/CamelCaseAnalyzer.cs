using System.Text.RegularExpressions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace DocAgent.Indexing;

/// <summary>
/// Lucene analyzer that splits CamelCase and acronym tokens, then lowercases them.
/// Handles both CamelCase (GetReferences → get, references) and acronyms (XMLParser → xml, parser).
/// </summary>
public sealed class CamelCaseAnalyzer : Analyzer
{
    private static readonly LuceneVersion LuceneVer = LuceneVersion.LUCENE_48;

    protected override TokenStreamComponents CreateComponents(string fieldName, System.IO.TextReader reader)
    {
        var tokenizer = new CamelCaseTokenizer(LuceneVer, reader);
        TokenStream filter = new LowerCaseFilter(LuceneVer, tokenizer);
        return new TokenStreamComponents(tokenizer, filter);
    }

    // -- Inner tokenizer -------------------------------------------------------

    internal sealed class CamelCaseTokenizer : Tokenizer
    {
        // Splits on:
        //   - boundary between an uppercase run and a new uppercase+lowercase sequence (XML|Parser)
        //   - boundary between lowercase and uppercase (get|Ref)
        private static readonly Regex SplitRegex = new(
            @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled);

        private readonly ICharTermAttribute _termAtt;
        private string[] _parts = [];
        private int      _partIndex;

        public CamelCaseTokenizer(LuceneVersion ver, System.IO.TextReader reader) : base(reader)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            ClearAttributes();
            while (_partIndex < _parts.Length)
            {
                var part = _parts[_partIndex++];
                if (part.Length == 0) continue;
                _termAtt.SetEmpty().Append(part);
                return true;
            }
            return false;
        }

        public override void Reset()
        {
            base.Reset();
            // Read all input from the reader (which is m_input in Lucene.Net Tokenizer)
            var sb = new System.Text.StringBuilder();
            var buffer = new char[1024];
            int read;
            while ((read = m_input.Read(buffer, 0, buffer.Length)) > 0)
                sb.Append(buffer, 0, read);

            var text = sb.ToString();

            // Split into whitespace-delimited tokens, then for each token emit:
            //  1. the original token
            //  2. the CamelCase sub-parts (if different from original)
            var raw = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>();
            foreach (var token in raw)
            {
                parts.Add(token); // preserve original
                var subParts = SplitRegex.Split(token)
                                          .Where(s => s.Length > 0)
                                          .ToArray();
                if (subParts.Length > 1)
                    parts.AddRange(subParts);
            }
            _parts     = parts.ToArray();
            _partIndex = 0;
        }
    }
}
