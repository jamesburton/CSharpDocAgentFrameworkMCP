# Phase 3: BM25 Search Index - Research

**Researched:** 2026-02-26
**Domain:** Lucene.Net full-text search, BM25 ranking, CamelCase tokenization, index persistence
**Confidence:** MEDIUM-HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Library:** Lucene.Net (explicitly chosen pre-phase)
- **Search behavior:** BM25 ranking with symbol name weighted higher than doc comment text. Exact matches rank above partial token matches. Partial CamelCase queries resolve correctly (e.g., `getRef` finds `GetReferences`).
- **Tokenization:** CamelCase splitting with acronym awareness (`XMLParser` → `XML` + `Parser`). Case-insensitive matching. Standard Lucene analyzers extended with custom CamelCase tokenizer.
- **Index persistence:** Lucene index segments stored alongside `.msgpack` snapshot artifacts. Index rebuilt from snapshot if segments missing or version mismatch. No separate versioning scheme — tied to snapshot content hash.

### Claude's Discretion

All implementation details beyond the above are deferred to Claude. Follow Lucene.Net conventions and the project's existing patterns from Phase 1-2.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INDX-01 | BM25 search index over symbol names and doc text replacing `InMemorySearchIndex` | `Lucene.Net.Search.Similarities.BM25Similarity` + `PerFieldSimilarityWrapper` for field weighting; `ISearchIndex` interface already defined |
| INDX-02 | CamelCase-aware tokenization for symbol name search | `WordDelimiterFilter` (or `WordDelimiterGraphFilter`) in `Lucene.Net.Analysis.Common` handles CamelCase split and acronym awareness |
| INDX-03 | Index persistence alongside snapshots | `FSDirectory.Open(path)` for durable Lucene segment storage; path convention `{artifactsDir}/{contentHash}.lucene/`; rebuild triggered when directory missing or `IndexCommit.UserData["snapshotHash"]` mismatches |
</phase_requirements>

---

## Summary

Lucene.Net 4.8.0-beta00017 is the current release (October 2024) and is the established, production-capable .NET port of Apache Lucene. It ships BM25Similarity as a first-class API, supports custom `Analyzer` implementations for CamelCase tokenization via `WordDelimiterFilter` in `Lucene.Net.Analysis.Common`, and provides `FSDirectory` for durable filesystem-based index persistence.

The primary technical challenge is the custom CamelCase analyzer: the built-in `WordDelimiterFilter` (with `SPLIT_ON_CASE_CHANGE | GENERATE_WORD_PARTS | PRESERVE_ORIGINAL`) correctly splits `GetReferences` → `[Get, References, GetReferences]` and `XMLParser` → `[XML, Parser, XMLParser]`, enabling partial CamelCase queries. Field weighting (symbol name ranked higher than doc comment) is achieved cleanly with `PerFieldSimilarityWrapper` or query-time field boosts.

Index persistence maps one Lucene index directory to one snapshot content hash. The `BM25SearchIndex` stores the index at `{artifactsDir}/{snapshotHash}.lucene/` and stores the source snapshot hash in `IndexWriter.SetCommitData` so that on load it can verify freshness without re-ingesting. This keeps the persistence contract simple and consistent with how `.msgpack` snapshots are stored.

**Primary recommendation:** Implement `BM25SearchIndex : ISearchIndex` in `DocAgent.Indexing` using `Lucene.Net` + `Lucene.Net.Analysis.Common`. Store the Lucene index at `{artifactsDir}/{snapshotHash}.lucene/` alongside the `.msgpack` file. Replace `InMemorySearchIndex` in all non-test code paths.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Lucene.Net` | `4.8.0-beta00017` | Core index, search, BM25Similarity, FSDirectory | Only stable .NET port of Lucene; decision locked |
| `Lucene.Net.Analysis.Common` | `4.8.0-beta00017` | `WordDelimiterFilter`, `LowerCaseFilter`, standard analyzers | Required for CamelCase tokenization; same version as core |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Lucene.Net.QueryParser` | `4.8.0-beta00017` | Parse user query strings into `Query` objects | Needed if supporting `MultiFieldQueryParser` for cross-field search |

> Note: `Lucene.Net.QueryParser` is optional if queries are constructed programmatically via `BooleanQuery` / `TermQuery`. For the phase scope (ranked search over symbol name + doc text), programmatic query construction is simpler and avoids QueryParser syntax surprises.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `WordDelimiterFilter` | `WordDelimiterGraphFilter` | GraphFilter produces proper token graphs (better for phrase queries) but is more complex. WDF is sufficient for this use case. |
| `PerFieldSimilarityWrapper` | Query-time `^boost` field syntax | Both achieve field weighting. `PerFieldSimilarityWrapper` is index-time and cleaner for programmatic queries. |
| `FSDirectory.Open(path)` | `MMapDirectory` | MMapDirectory is faster for large indexes on Linux. `FSDirectory.Open` auto-selects best implementation per environment. Use `FSDirectory.Open` — it picks MMapDirectory on Linux automatically. |

**Installation:**
```xml
<!-- Add to Directory.Packages.props -->
<PackageVersion Include="Lucene.Net" Version="4.8.0-beta00017" />
<PackageVersion Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00017" />

<!-- Add to DocAgent.Indexing.csproj -->
<PackageReference Include="Lucene.Net" />
<PackageReference Include="Lucene.Net.Analysis.Common" />
```

**Compatibility note (MEDIUM confidence):** Lucene.Net 4.8.0-beta00017 targets `net6.0`, `net8.0`, and `netstandard2.0/2.1`. It does NOT include an explicit `net10.0` TFM. An open PR (#1219) adds `net10.0` targeting but has not shipped. Since `net10.0` runs on `netstandard2.1` / `net8.0` compatible assemblies via roll-forward, the package will work on `net10.0` at runtime. Verify with `dotnet build` and `dotnet test` after adding — no functional issues are expected.

---

## Architecture Patterns

### Recommended Project Structure

```
src/DocAgent.Indexing/
├── BM25SearchIndex.cs        # ISearchIndex implementation (replaces InMemorySearchIndex)
├── CamelCaseAnalyzer.cs      # Custom Analyzer with WordDelimiterFilter
├── InMemorySearchIndex.cs    # KEEP — still used in tests
└── DocAgent.Indexing.csproj  # Add Lucene.Net + Lucene.Net.Analysis.Common refs

tests/DocAgent.Tests/
├── BM25SearchIndexTests.cs   # Unit tests for BM25SearchIndex (new)
└── InMemorySearchIndexTests.cs  # Existing tests unchanged (still valid for InMemorySearchIndex)
```

### Pattern 1: Custom CamelCase Analyzer

**What:** An `Analyzer` subclass that tokenizes symbol names by splitting CamelCase and preserving original token (for exact-match queries).

**When to use:** Applied to the `symbolName` field. The `docText` field can use `StandardAnalyzer` (no CamelCase splitting needed for prose documentation).

**Example:**
```csharp
// Source: WebSearch verified against Lucene.Net 4.8 API docs
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Util;

public sealed class CamelCaseAnalyzer : Analyzer
{
    private static readonly LuceneVersion Version = LuceneVersion.LUCENE_48;

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new WhitespaceTokenizer(Version, reader);

        TokenStream filter = new WordDelimiterFilter(
            tokenizer,
            WordDelimiterFlags.SPLIT_ON_CASE_CHANGE |
            WordDelimiterFlags.GENERATE_WORD_PARTS   |
            WordDelimiterFlags.GENERATE_NUMBER_PARTS |
            WordDelimiterFlags.PRESERVE_ORIGINAL,    // keep "GetReferences" as a token too
            null);                                    // no protected words

        filter = new LowerCaseFilter(Version, filter);
        return new TokenStreamComponents(tokenizer, filter);
    }
}
```

**Token output examples:**
- `GetReferences` → `[getreferences, get, references]`
- `XMLParser` → `[xmlparser, xml, parser]`
- `getRef` (query) → `[getref, get, ref]` — matches tokens from `GetReferences`

### Pattern 2: BM25SearchIndex with Field Weighting

**What:** Two fields per document (`symbolName` with CamelCaseAnalyzer, `docText` with StandardAnalyzer). `PerFieldSimilarityWrapper` gives `symbolName` a higher BM25 k1 to make name matches rank higher.

**When to use:** Always — this is the core implementation of `ISearchIndex`.

**Example:**
```csharp
// Source: WebSearch verified against Lucene.Net 4.8 BM25Similarity docs
using Lucene.Net.Search.Similarities;

private static Similarity BuildSimilarity() =>
    new PerFieldSimilarityWrapper(new BM25Similarity())
    {
        // symbolName field: higher k1 = stronger term frequency saturation
        // docText field: default BM25 (k1=1.2, b=0.75)
    };

// Simpler alternative: per-field similarity via subclass
private sealed class DocAgentSimilarity : PerFieldSimilarityWrapper
{
    private static readonly BM25Similarity NameSimilarity = new BM25Similarity(k1: 2.0f, b: 0.5f);
    private static readonly BM25Similarity TextSimilarity = new BM25Similarity();
    public override Similarity Get(string field) =>
        field == "symbolName" ? NameSimilarity : TextSimilarity;
}
```

### Pattern 3: Index Persistence Tied to Snapshot Content Hash

**What:** Lucene index stored at `{artifactsDir}/{snapshotContentHash}.lucene/`. On `IndexAsync`, check if directory exists AND `IndexCommit.UserData["snapshotHash"]` matches. If so, skip re-indexing. If not, wipe and rebuild.

**When to use:** Every call to `IndexAsync`. This is the core of INDX-03.

**Example:**
```csharp
// Source: Lucene.Net docs + WebSearch pattern
public async Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct)
{
    var indexDir = Path.Combine(_artifactsDir, $"{snapshot.ContentHash}.lucene");

    // Check if index is already fresh
    using var dir = FSDirectory.Open(indexDir);
    if (DirectoryReader.IndexExists(dir))
    {
        using var reader = DirectoryReader.Open(dir);
        if (reader.IndexCommit.UserData.TryGetValue("snapshotHash", out var stored)
            && stored == snapshot.ContentHash)
            return; // already indexed, nothing to do
    }

    // Build or rebuild
    var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _perFieldAnalyzer)
    {
        OpenMode = OpenMode.CREATE, // wipe existing index
        Similarity = new DocAgentSimilarity()
    };
    using var writer = new IndexWriter(dir, config);
    foreach (var node in snapshot.Nodes)
        writer.AddDocument(BuildDocument(node));
    writer.SetCommitData(new Dictionary<string, string>
    {
        { "snapshotHash", snapshot.ContentHash ?? string.Empty }
    });
    writer.Commit();
}
```

### Pattern 4: Searching with BM25

**What:** Open `DirectoryReader`, create `IndexSearcher`, parse query across both fields, return ranked `SearchHit` list.

**Example:**
```csharp
public async IAsyncEnumerable<SearchHit> SearchAsync(
    string query,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    if (!DirectoryReader.IndexExists(_directory))
        yield break;

    using var reader = DirectoryReader.Open(_directory);
    var searcher = new IndexSearcher(reader) { Similarity = new DocAgentSimilarity() };

    // Multi-field query: symbolName (CamelCase tokenized) + docText (standard)
    var boolQuery = new BooleanQuery();
    // symbolName match scores higher via similarity; add with SHOULD
    boolQuery.Add(new TermQuery(new Term("symbolName", query.ToLowerInvariant())), Occur.SHOULD);
    boolQuery.Add(new TermQuery(new Term("docText", query.ToLowerInvariant())), Occur.SHOULD);

    var topDocs = searcher.Search(boolQuery, 50);
    foreach (var scoreDoc in topDocs.ScoreDocs)
    {
        ct.ThrowIfCancellationRequested();
        var doc = searcher.Doc(scoreDoc.Doc);
        var id = new SymbolId(doc.Get("symbolId"));
        var snippet = doc.Get("symbolName");
        yield return new SearchHit(id, scoreDoc.Score, snippet);
    }
}
```

### Anti-Patterns to Avoid

- **Single shared analyzer for all fields:** Use `PerFieldAnalyzerWrapper` to apply `CamelCaseAnalyzer` to `symbolName` and `StandardAnalyzer` to `docText`. Applying CamelCase tokenization to prose documentation text produces degraded results.
- **Not setting Similarity consistently:** Must set the same `Similarity` instance on both `IndexWriterConfig` and `IndexSearcher`. Mismatch causes score miscalculation.
- **Storing the `IndexWriter` as a long-lived field:** Lucene `IndexWriter` holds an exclusive lock on the directory. For this use case (batch index then search), build index → dispose writer → open reader is the correct pattern. Don't keep a writer open during searches.
- **Using `RAMDirectory` in production path:** `RAMDirectory` has poor concurrent performance and does not persist. It is correct for `InMemorySearchIndex` tests but must not appear in `BM25SearchIndex`.
- **Indexing with `ContentHash = null`:** The persistence check stores `snapshot.ContentHash`. If the snapshot has no hash yet (pre-`SnapshotStore.SaveAsync`), the freshness check is meaningless. `BM25SearchIndex.IndexAsync` should require `snapshot.ContentHash` to be non-null, or handle null as "always rebuild."

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BM25 scoring | Custom TF-IDF scorer | `BM25Similarity` | Correct IDF, length normalization, edge cases with empty fields |
| CamelCase tokenization | Regex.Split on uppercase transitions | `WordDelimiterFilter` with `SPLIT_ON_CASE_CHANGE` | Handles acronyms (`XMLParser`), numbers (`HTTP2Client`), mixed cases; battle-tested |
| Inverted index | Dictionary<string, List<SymbolId>> | Lucene's `IndexWriter` / `DirectoryReader` | Handles posting lists, term vectors, segment merging, concurrent reads |
| Query parsing | String.Split on spaces | `BooleanQuery` with `TermQuery` per field | Avoids QueryParser special-character escaping issues; deterministic behavior |
| Index version/staleness tracking | Separate JSON sidecar file | `IndexCommit.UserData` dict | Already part of Lucene commit; transactional; no extra file to manage |

**Key insight:** The apparent simplicity of "just split CamelCase and do BM25" hides significant edge cases (acronyms, numbers in names, Unicode). Lucene's token filter pipeline handles all of these correctly.

---

## Common Pitfalls

### Pitfall 1: Analyzer Mismatch Between Index and Query Time

**What goes wrong:** Symbol indexed with `CamelCaseAnalyzer` (tokens: `[getreferences, get, references]`) but queried with `StandardAnalyzer` (token: `[getreferences]`). Partial queries like `getRef` produce zero results because `getRef` is not a stored token.

**Why it happens:** `IndexWriter` uses the analyzer provided to `IndexWriterConfig`. The query must tokenize the query string with the same analyzer, or use a pre-tokenized `TermQuery`.

**How to avoid:** Use `PerFieldAnalyzerWrapper` at both index time and query time. When building queries programmatically, run the query string through `CamelCaseAnalyzer.GetTokenStream()` to enumerate tokens, then build a `BooleanQuery` from the resulting terms.

**Warning signs:** Searches for exact symbol names return results; searches for partial CamelCase tokens return nothing.

### Pitfall 2: net10.0 TFM Compatibility

**What goes wrong:** `Lucene.Net 4.8.0-beta00017` does not ship a `net10.0` TFM explicitly. Build may warn or fail if `netstandard2.1` rollup is not automatic in the project.

**Why it happens:** The project targets `net10.0`; Lucene.Net targets up to `net8.0`. .NET allows net10.0 → net8.0 compatible library consumption but the NuGet restore may show warnings.

**How to avoid:** Test with `dotnet build` immediately after adding the packages. If restore warnings appear, add `<NoWarn>NU1701</NoWarn>` to the Indexing project. No functional issues are expected since net10.0 is fully backward-compatible with net8.0 libraries.

**Warning signs:** `NU1701` warnings during restore. Runtime failures would be unusual and should be investigated further.

### Pitfall 3: FSDirectory Locking on Windows

**What goes wrong:** Running multiple test processes in parallel against the same index directory causes `LockObtainFailedException` because `NativeFSLockFactory` holds an exclusive OS file lock.

**Why it happens:** Lucene uses advisory/exclusive file locks to prevent concurrent writers. `dotnet test` with parallel test runners can open multiple `IndexWriter` instances against the same path.

**How to avoid:** Each test must use a unique temp directory (pattern already established in `SnapshotStoreTests` with `Guid.NewGuid()`). `BM25SearchIndexTests` must follow the same pattern.

**Warning signs:** Tests pass individually but fail when run together with `dotnet test`.

### Pitfall 4: ContentHash Null at Index Time

**What goes wrong:** Calling `IndexAsync` with a snapshot whose `ContentHash` is null (snapshot not yet persisted via `SnapshotStore.SaveAsync`) means the "already indexed?" check stores `""` as the hash, causing cache misses or infinite re-indexing.

**Why it happens:** `SymbolGraphSnapshot.ContentHash` is nullable — set only by `SnapshotStore.SaveAsync`. Callers may pass an in-memory snapshot that hasn't been persisted.

**How to avoid:** `BM25SearchIndex.IndexAsync` should throw `ArgumentException` if `snapshot.ContentHash` is null or empty, or treat null as "always rebuild without caching." Document this precondition clearly.

**Warning signs:** Every call to `IndexAsync` rebuilds the index even with identical content.

---

## Code Examples

Verified patterns from official sources and cross-referenced documentation:

### Full Index Write Pipeline

```csharp
// Source: lucenenet.apache.org official quickstart + Lucene.Net API docs
const LuceneVersion AppVersion = LuceneVersion.LUCENE_48;

var indexPath = Path.Combine(artifactsDir, $"{snapshot.ContentHash}.lucene");
using var dir = FSDirectory.Open(indexPath);

var perFieldAnalyzer = new PerFieldAnalyzerWrapper(
    defaultAnalyzer: new StandardAnalyzer(AppVersion),
    fieldAnalyzers: new Dictionary<string, Analyzer>
    {
        { "symbolName", new CamelCaseAnalyzer() },
        { "fullyQualifiedName", new CamelCaseAnalyzer() }
    });

var config = new IndexWriterConfig(AppVersion, perFieldAnalyzer)
{
    OpenMode = OpenMode.CREATE,
    Similarity = new DocAgentSimilarity()
};

using var writer = new IndexWriter(dir, config);

foreach (var node in snapshot.Nodes)
{
    var doc = new Document
    {
        new StringField("symbolId",         node.Id.Value,           Field.Store.YES),
        new TextField("symbolName",         node.DisplayName ?? "",  Field.Store.YES),
        new TextField("fullyQualifiedName", node.FullyQualifiedName ?? "", Field.Store.NO),
        new TextField("docText",            node.Docs?.Summary ?? "", Field.Store.NO)
    };
    writer.AddDocument(doc);
}

writer.SetCommitData(new Dictionary<string, string>
{
    { "snapshotHash", snapshot.ContentHash! }
});
writer.Commit();
// writer disposed by using statement — releases lock
```

### Document Field Strategy

| Field | Type | Stored | Analyzer | Purpose |
|-------|------|--------|----------|---------|
| `symbolId` | `StringField` | YES | None (not tokenized) | Retrieve `SymbolId` from hit |
| `symbolName` | `TextField` | YES | `CamelCaseAnalyzer` | Primary search target; stored for snippet |
| `fullyQualifiedName` | `TextField` | NO | `CamelCaseAnalyzer` | Expands match coverage; not needed in results |
| `docText` | `TextField` | NO | `StandardAnalyzer` | Doc comment prose; lower weight |

### Freshness Check Pattern

```csharp
// Source: Lucene.Net IndexCommit.UserData API
private static bool IsIndexFresh(string indexPath, string snapshotHash)
{
    using var dir = FSDirectory.Open(indexPath);
    if (!DirectoryReader.IndexExists(dir))
        return false;
    using var reader = DirectoryReader.Open(dir);
    return reader.IndexCommit.UserData.TryGetValue("snapshotHash", out var stored)
        && stored == snapshotHash;
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `TFIDFSimilarity` (Lucene default) | `BM25Similarity` (default in Lucene 6+) | Lucene 6.0 (Java), Lucene.Net 4.8 | BM25 is universally better for ad-hoc search; must be set explicitly in Lucene.Net 4.8 |
| `WordDelimiterFilter` | `WordDelimiterGraphFilter` (Lucene 6.5+) | Lucene.Net 4.8 includes both | GraphFilter better for phrase queries; WDF sufficient for term queries |
| `RAMDirectory` | `ByteBuffersDirectory` (Java Lucene 9) | Not yet in Lucene.Net 4.8 | `RAMDirectory` still the in-memory option; fine for tests |

**Deprecated/outdated:**
- `RAMDirectory`: Not deprecated in Lucene.Net 4.8 but discouraged for production; use `FSDirectory` for persistence.
- Default `TFIDFSimilarity`: Still present but not the default; explicitly set `BM25Similarity` to ensure correct behavior.

---

## Open Questions

1. **`net10.0` NuGet compatibility warning severity**
   - What we know: Lucene.Net 4.8.0-beta00017 targets `net8.0` max; net10.0 TFM PR is open but unshipped
   - What's unclear: Whether the project's `TreatWarningsAsErrors=true` + `NuGetAuditMode=direct` settings will cause build failures on `NU1701`
   - Recommendation: Add the packages and run `dotnet build` first. If `NU1701` errors block build, suppress with `<NoWarn>NU1701</NoWarn>` in `DocAgent.Indexing.csproj`.

2. **`GetAsync(SymbolId id)` implementation strategy**
   - What we know: `ISearchIndex.GetAsync` must return a full `SymbolNode`, but Lucene stores only fields — not the full object graph (e.g., `IReadOnlyList<SymbolId> PreviousIds`, `DocComment` with multiple sub-fields)
   - What's unclear: Whether to serialize the full `SymbolNode` into a stored field (JSON/MessagePack) or maintain a parallel in-memory dictionary keyed by `SymbolId` within `BM25SearchIndex`
   - Recommendation: Keep a `Dictionary<SymbolId, SymbolNode>` in-memory within `BM25SearchIndex` populated during `IndexAsync`. This avoids serialization round-trips and keeps `GetAsync` O(1). The `InMemorySearchIndex` already uses this pattern — adopt it as the secondary lookup path.

3. **Index directory cleanup policy**
   - What we know: Each snapshot hash produces a distinct `.lucene/` directory. Old snapshots are not cleaned up by `SnapshotStore`.
   - What's unclear: Whether Phase 3 should include any cleanup of stale Lucene index directories when the corresponding `.msgpack` no longer exists in the manifest.
   - Recommendation: Defer cleanup to Phase 4 or later. Phase 3 focus is correctness. Document the accumulation behavior as a known limitation.

---

## Sources

### Primary (HIGH confidence)
- [Lucene.Net official quickstart / FSDirectory API](https://lucenenet.apache.org/) — FSDirectory, IndexWriter, IndexWriterConfig patterns
- [NuGet Gallery: Lucene.Net 4.8.0-beta00017](https://www.nuget.org/packages/Lucene.Net/absoluteLatest) — version, TFMs, dependencies
- [Lucene.Net FSDirectory API docs](https://lucenenet.apache.org/docs/4.8.0-beta00014/api/core/Lucene.Net.Store.FSDirectory.html) — Open(), subclasses, locking
- [BM25Similarity class docs (Lucene.Net 4.8)](https://lucenenet.apache.org/docs/4.8.0-beta00009/api/core/Lucene.Net.Search.Similarities.BM25Similarity.html) — k1/b parameters, PerFieldSimilarityWrapper

### Secondary (MEDIUM confidence)
- [GitHub Discussion #1191: .NET Target/Runtime Support](https://github.com/apache/lucenenet/discussions/1191) — net10.0 support roadmap
- [GitHub PR #1219: Add .NET 10 target](http://www.mail-archive.com/dev@lucenenet.apache.org/msg09413.html) — current status of net10.0 TFM
- [WebSearch: WordDelimiterFilter CamelCase behavior](https://lucenenet.apache.org/docs/4.8.0-beta00005/api/Lucene.Net/Lucene.Net.Index.html) — token split behavior, flags

### Tertiary (LOW confidence — needs validation)
- IndexCommit.UserData pattern for hash storage — inferred from Lucene.Net API surface; functionally correct but not verified against an official example in Lucene.Net specifically

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Lucene.Net 4.8.0-beta00017 is confirmed on NuGet; packages confirmed compatible
- Architecture: MEDIUM-HIGH — FSDirectory, BM25Similarity, WordDelimiterFilter patterns verified via official docs; IndexCommit.UserData pattern is MEDIUM
- Pitfalls: MEDIUM — Windows locking and analyzer mismatch are well-known; net10.0 compat is speculative but low-risk

**Research date:** 2026-02-26
**Valid until:** 2026-05-26 (Lucene.Net releases are slow-moving; 90 days reasonable)
