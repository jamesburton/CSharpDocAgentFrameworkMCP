using System.Runtime.CompilerServices;
using DocAgent.Core;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace DocAgent.Indexing;

/// <summary>
/// ISearchIndex implementation backed by Lucene.Net with BM25 scoring and CamelCase-aware tokenization.
/// </summary>
public sealed class BM25SearchIndex : ISearchIndex, IDisposable
{
    private static readonly LuceneVersion LuceneVer = LuceneVersion.LUCENE_48;

    private readonly LuceneDirectory _directory;
    private readonly bool _ownsDirectory;
    private readonly Dictionary<SymbolId, SymbolNode> _nodes = new();
    private bool _hasIndex;

    /// <summary>Initialise with a filesystem-backed index at the given directory path.</summary>
    public BM25SearchIndex(string artifactsDir)
    {
        System.IO.Directory.CreateDirectory(artifactsDir);
        _directory = FSDirectory.Open(new System.IO.DirectoryInfo(artifactsDir));
        _ownsDirectory = true;
    }

    /// <summary>Initialise with an injected Lucene directory (e.g. RAMDirectory for tests).</summary>
    public BM25SearchIndex(LuceneDirectory directory)
    {
        _directory = directory;
        _ownsDirectory = false;
    }

    // --- ISearchIndex ---------------------------------------------------------

    public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct)
    {
        _nodes.Clear();
        foreach (var node in snapshot.Nodes)
            _nodes[node.Id] = node;

        var config = new IndexWriterConfig(LuceneVer, BuildAnalyzer())
        {
            OpenMode = OpenMode.CREATE,
            Similarity = BuildSimilarity(),
        };

        using var writer = new IndexWriter(_directory, config);

        foreach (var node in snapshot.Nodes)
        {
            var doc = new Document
            {
                new StringField("symbolId",           node.Id.Value,                              Field.Store.YES),
                new TextField  ("symbolName",         node.DisplayName          ?? string.Empty,  Field.Store.YES),
                new TextField  ("fullyQualifiedName", node.FullyQualifiedName   ?? string.Empty,  Field.Store.NO),
                new TextField  ("docText",            node.Docs?.Summary        ?? string.Empty,  Field.Store.NO),
            };
            writer.AddDocument(doc);
        }

        writer.Commit();
        _hasIndex = true;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SearchHit> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_hasIndex || string.IsNullOrWhiteSpace(query))
            yield break;

        var tokens = TokenizeQuery(query);
        if (tokens.Count == 0)
            yield break;

        using var reader = DirectoryReader.Open(_directory);
        var searcher = new IndexSearcher(reader)
        {
            Similarity = BuildSimilarity(),
        };

        var boolQuery = new BooleanQuery();
        foreach (var token in tokens)
        {
            boolQuery.Add(new TermQuery(new Term("symbolName",         token)), Occur.SHOULD);
            boolQuery.Add(new TermQuery(new Term("fullyQualifiedName", token)), Occur.SHOULD);
            boolQuery.Add(new TermQuery(new Term("docText",            token)), Occur.SHOULD);
        }

        var topDocs = searcher.Search(boolQuery, 50);

        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            ct.ThrowIfCancellationRequested();
            var doc  = searcher.Doc(scoreDoc.Doc);
            var id   = doc.Get("symbolId");
            var name = doc.Get("symbolName") ?? string.Empty;
            yield return new SearchHit(new SymbolId(id), scoreDoc.Score, name);
        }
    }

    public Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct)
        => Task.FromResult(_nodes.TryGetValue(id, out var node) ? node : null);

    // --- IDisposable ----------------------------------------------------------

    public void Dispose()
    {
        if (_ownsDirectory)
            _directory.Dispose();
    }

    // --- Helpers --------------------------------------------------------------

    private static Analyzer BuildAnalyzer()
    {
        var camel = new CamelCaseAnalyzer();
        var std   = new StandardAnalyzer(LuceneVer);
        var map   = new Dictionary<string, Analyzer>(StringComparer.Ordinal)
        {
            ["symbolName"]         = camel,
            ["fullyQualifiedName"] = camel,
            ["docText"]            = std,
        };
        return new PerFieldAnalyzerWrapper(std, map);
    }

    private static Similarity BuildSimilarity()
        => new DocAgentSimilarity();

    private static List<string> TokenizeQuery(string query)
    {
        var tokens = new List<string>();
        using var analyzer = new CamelCaseAnalyzer();
        using var stream   = analyzer.GetTokenStream("symbolName", query);
        var attr = stream.AddAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
        stream.Reset();
        while (stream.IncrementToken())
            tokens.Add(new string(attr.Buffer, 0, attr.Length));
        stream.End();
        return tokens;
    }

    // --- Similarity -----------------------------------------------------------

    private sealed class DocAgentSimilarity : PerFieldSimilarityWrapper
    {
        private static readonly BM25Similarity NameSimilarity    = new BM25Similarity(2.0f, 0.5f);
        private static readonly BM25Similarity DefaultSimilarity = new BM25Similarity();

        public override Similarity Get(string fieldName) =>
            fieldName is "symbolName" or "fullyQualifiedName"
                ? NameSimilarity
                : DefaultSimilarity;
    }
}
