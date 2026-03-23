using System.Linq;
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
/// Supports filesystem persistence: the Lucene index is stored at {artifactsDir}/{contentHash}.lucene/
/// and reloaded without re-indexing when the snapshot hash matches the committed index.
/// </summary>
public sealed class BM25SearchIndex : ISearchIndex, IDisposable
{
    private static readonly LuceneVersion LuceneVer = LuceneVersion.LUCENE_48;

    // Set when using an injected directory (test mode). Null when using FSDirectory.
    private readonly LuceneDirectory? _injectedDirectory;

    // Set when using filesystem mode (non-null artifactsDir constructor).
    private readonly string? _artifactsDir;

    private readonly Dictionary<SymbolId, SymbolNode> _nodes = new();

    // Tracks the active FSDirectory opened for searching (lifecycle managed by this class).
    private FSDirectory? _activeFsDirectory;
    private bool _hasIndex;

    /// <summary>Initialise with a filesystem-backed index. Each snapshot is stored at {artifactsDir}/{contentHash}.lucene/.</summary>
    public BM25SearchIndex(string artifactsDir)
    {
        System.IO.Directory.CreateDirectory(artifactsDir);
        _artifactsDir = artifactsDir;
    }

    /// <summary>Initialise with an injected Lucene directory (e.g. RAMDirectory for tests). Freshness check is skipped.</summary>
    public BM25SearchIndex(LuceneDirectory directory)
    {
        _injectedDirectory = directory;
    }

    // --- ISearchIndex ---------------------------------------------------------

    public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct, bool forceReindex = false)
    {
        if (_injectedDirectory is not null)
        {
            // Test mode: always rebuild against injected directory.
            return IndexIntoDirectoryAsync(_injectedDirectory, snapshot);
        }

        // FSDirectory mode: require ContentHash for freshness tracking.
        if (string.IsNullOrEmpty(snapshot.ContentHash))
            throw new ArgumentException("Snapshot must have a ContentHash for persistent indexing.", nameof(snapshot));

        var indexPath = GetIndexPath(snapshot.ContentHash!);
        System.IO.Directory.CreateDirectory(indexPath);
        var dir = FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));

        // Freshness check: skip rebuild if index already contains this hash (unless forced).
        if (!forceReindex && IsIndexFresh(dir, snapshot.ContentHash!))
        {
            // Populate _nodes for GetAsync, reuse existing index directory.
            PopulateNodes(snapshot);
            SwapActiveFsDirectory(dir);
            _hasIndex = true;
            return Task.CompletedTask;
        }

        return IndexIntoFsDirectoryAsync(dir, snapshot);
    }

    /// <summary>
    /// Loads a previously persisted index from {artifactsDir}/{contentHash}.lucene/ without re-indexing.
    /// Populates _nodes from the snapshot for GetAsync. Throws if the persisted index is missing or stale.
    /// </summary>
    public Task LoadIndexAsync(string contentHash, SymbolGraphSnapshot snapshot, CancellationToken ct = default)
    {
        if (_artifactsDir is null)
            throw new InvalidOperationException("LoadIndexAsync requires a filesystem-backed BM25SearchIndex (use the artifactsDir constructor).");

        if (string.IsNullOrEmpty(contentHash))
            throw new ArgumentException("contentHash must not be null or empty.", nameof(contentHash));

        var indexPath = GetIndexPath(contentHash);
        if (!System.IO.Directory.Exists(indexPath))
            throw new DirectoryNotFoundException($"Persisted index directory not found: {indexPath}");

        var dir = FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));

        if (!IsIndexFresh(dir, contentHash))
        {
            dir.Dispose();
            throw new InvalidOperationException($"Persisted index at '{indexPath}' does not match content hash '{contentHash}'. Re-index required.");
        }

        PopulateNodes(snapshot);
        SwapActiveFsDirectory(dir);
        _hasIndex = true;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SearchHit> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default,
        string? projectFilter = null)
    {
        if (!_hasIndex || string.IsNullOrWhiteSpace(query))
            yield break;

        // Wildcard "*" → enumerate all indexed nodes without Lucene text search.
        if (query.Trim() == "*")
        {
            foreach (var (id, node) in _nodes)
            {
                ct.ThrowIfCancellationRequested();
                if (projectFilter is not null && node.ProjectOrigin != projectFilter)
                    continue;
                yield return new SearchHit(id, 1.0f, node.DisplayName ?? string.Empty);
            }
            yield break;
        }

        var directory = GetSearchDirectory();
        if (directory is null)
            yield break;

        var tokens = TokenizeQuery(query);
        if (tokens.Count == 0)
            yield break;

        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader)
        {
            Similarity = BuildSimilarity(),
        };

        // Build text relevance query
        var textQuery = new BooleanQuery();
        foreach (var token in tokens)
        {
            textQuery.Add(new TermQuery(new Term("symbolName",         token)), Occur.SHOULD);
            textQuery.Add(new TermQuery(new Term("fullyQualifiedName", token)), Occur.SHOULD);
            textQuery.Add(new TermQuery(new Term("docText",            token)), Occur.SHOULD);
        }

        // When a project filter is specified, wrap the text query with a MUST project clause
        // so the top-N results are already scoped to the target project (not dominated by framework types).
        Query finalQuery;
        if (projectFilter is not null)
        {
            var filtered = new BooleanQuery
            {
                { textQuery, Occur.MUST },
                { new TermQuery(new Term("projectName", projectFilter)), Occur.MUST },
            };
            finalQuery = filtered;
        }
        else
        {
            finalQuery = textQuery;
        }

        var topDocs = searcher.Search(finalQuery, 50);

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
        _activeFsDirectory?.Dispose();
        _activeFsDirectory = null;
    }

    // --- Private helpers ------------------------------------------------------

    private string GetIndexPath(string contentHash)
        => System.IO.Path.Combine(_artifactsDir!, $"{contentHash}.lucene");

    /// <summary>Returns true if the Lucene directory exists and its committed snapshotHash matches contentHash.</summary>
    private static bool IsIndexFresh(LuceneDirectory dir, string contentHash)
    {
        try
        {
            if (!DirectoryReader.IndexExists(dir))
                return false;

            using var reader = DirectoryReader.Open(dir);
            var userData = reader.IndexCommit.UserData;
            return userData.TryGetValue("snapshotHash", out var storedHash)
                   && storedHash == contentHash;
        }
        catch
        {
            return false;
        }
    }

    private Task IndexIntoDirectoryAsync(LuceneDirectory dir, SymbolGraphSnapshot snapshot)
    {
        PopulateNodes(snapshot);

        var config = new IndexWriterConfig(LuceneVer, BuildAnalyzer())
        {
            OpenMode = OpenMode.CREATE,
            Similarity = BuildSimilarity(),
        };

        using var writer = new IndexWriter(dir, config);
        WriteDocuments(writer, snapshot);
        writer.Commit();

        _hasIndex = true;
        return Task.CompletedTask;
    }

    private Task IndexIntoFsDirectoryAsync(FSDirectory dir, SymbolGraphSnapshot snapshot)
    {
        PopulateNodes(snapshot);

        var config = new IndexWriterConfig(LuceneVer, BuildAnalyzer())
        {
            OpenMode = OpenMode.CREATE,
            Similarity = BuildSimilarity(),
        };

        using var writer = new IndexWriter(dir, config);
        WriteDocuments(writer, snapshot);

        // First commit to flush documents.
        writer.Commit();

        // Store snapshot hash in commit metadata, then commit again.
        writer.SetCommitData(new Dictionary<string, string>
        {
            { "snapshotHash", snapshot.ContentHash! },
        });
        writer.Commit();

        SwapActiveFsDirectory(dir);
        _hasIndex = true;
        return Task.CompletedTask;
    }

    private static void WriteDocuments(IndexWriter writer, SymbolGraphSnapshot snapshot)
    {
        foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
        {
            var doc = new Document
            {
                new StringField("symbolId",           node.Id.Value,                              Field.Store.YES),
                new TextField  ("symbolName",         node.DisplayName          ?? string.Empty,  Field.Store.YES),
                new TextField  ("fullyQualifiedName", node.FullyQualifiedName   ?? string.Empty,  Field.Store.NO),
                new TextField  ("docText",            node.Docs?.Summary        ?? string.Empty,  Field.Store.NO),
                new StringField("projectName",        node.ProjectOrigin        ?? string.Empty,  Field.Store.YES),
            };
            writer.AddDocument(doc);
        }
    }

    private void PopulateNodes(SymbolGraphSnapshot snapshot)
    {
        _nodes.Clear();
        foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
            _nodes[node.Id] = node;
    }

    private void SwapActiveFsDirectory(FSDirectory dir)
    {
        var old = _activeFsDirectory;
        _activeFsDirectory = dir;
        old?.Dispose();
    }

    private LuceneDirectory? GetSearchDirectory()
    {
        // Prefer injected directory (test mode), then active FSDirectory.
        if (_injectedDirectory is not null)
            return _injectedDirectory;
        return _activeFsDirectory;
    }

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
        stream.Dispose();
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
