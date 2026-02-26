---
phase: 03-bm25-search-index
plan: 01
subsystem: indexing
tags: [lucene.net, bm25, search, camelcase, tokenization, full-text-search]

requires:
  - phase: 02-ingestion-pipeline
    provides: SymbolGraphSnapshot with SymbolNode records fed into the index

provides:
  - BM25SearchIndex implementing ISearchIndex with Lucene.Net BM25 similarity scoring
  - CamelCaseAnalyzer with regex-based CamelCase and acronym splitting
  - 7 BM25SearchIndexTests covering ranking, tokenization, lookup, and edge cases

affects:
  - 03-02 (query service wiring)
  - 05-mcp-server (search_symbols tool uses ISearchIndex)

tech-stack:
  added:
    - Lucene.Net 4.8.0-beta00017
    - Lucene.Net.Analysis.Common 4.8.0-beta00017
  patterns:
    - RAMDirectory injection for test isolation (same pattern as InMemorySearchIndex)
    - PerFieldAnalyzerWrapper with different analyzers per field (symbolName vs docText)
    - PerFieldSimilarityWrapper for field-specific BM25 parameters
    - Custom Tokenizer subclass for regex-based splitting (vs WordDelimiterFilter)

key-files:
  created:
    - src/DocAgent.Indexing/CamelCaseAnalyzer.cs
    - src/DocAgent.Indexing/BM25SearchIndex.cs
    - tests/DocAgent.Tests/BM25SearchIndexTests.cs
  modified:
    - Directory.Packages.props
    - src/DocAgent.Indexing/DocAgent.Indexing.csproj

key-decisions:
  - "Custom CamelCaseTokenizer (regex-based) instead of Lucene WordDelimiterFilter — WordDelimiterFilter in 4.8.0-beta00017 does not split upper→upper+lower boundaries (XMLParser stays whole)"
  - "Regex pattern (?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z]) handles both lower→upper and acronym→capitalized splits"
  - "PRESERVE_ORIGINAL emits the full token (xmlparser) plus sub-parts (xml, parser) for broad matching"
  - "BM25 k1=2.0, b=0.5 for symbolName field — higher k1 rewards repeated term frequency, lower b reduces field-length normalization"
  - "NoWarn NU1701 added to Indexing csproj to suppress Lucene.Net target framework compatibility advisory"
  - "TokenStream.Dispose() (not Close()) required after TokenizeQuery to satisfy Lucene token stream contract"

patterns-established:
  - "Inject LuceneDirectory via constructor for test isolation — FSDirectory for production, RAMDirectory for unit tests"
  - "IDisposable on BM25SearchIndex only disposes FSDirectory when _ownsDirectory=true (injected dirs left to caller)"

requirements-completed: [INDX-01, INDX-02]

duration: 45min
completed: 2026-02-26
---

# Phase 3 Plan 01: BM25 Search Index Summary

**Lucene.Net 4.8 BM25SearchIndex with regex-based CamelCase tokenizer replacing InMemorySearchIndex stub — "getRef" finds "GetReferences", "XML" finds "XMLParser", name matches rank above doc-text matches**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-02-26T14:00:00Z
- **Completed:** 2026-02-26T14:45:00Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Lucene.Net 4.8.0-beta00017 integrated via central package management; build is zero-warning
- BM25SearchIndex implements ISearchIndex with per-field similarity: BM25(k1=2.0, b=0.5) for symbolName/fullyQualifiedName, default BM25 for docText
- CamelCaseAnalyzer correctly splits CamelCase and acronym boundaries using regex lookaheads — both "GetReferences"→[get, references] and "XMLParser"→[xml, parser]
- All 7 new BM25SearchIndexTests pass; full 61-test suite green

## Task Commits

1. **Task 1: Add Lucene.Net packages and implement CamelCaseAnalyzer + BM25SearchIndex** - `806c2a7` (feat)
2. **Task 2: BM25SearchIndex unit tests and CamelCaseAnalyzer fix** - `f2c9c4e` (feat)

## Files Created/Modified

- `src/DocAgent.Indexing/CamelCaseAnalyzer.cs` — Custom Lucene Tokenizer using regex to split CamelCase and acronyms, then LowerCaseFilter
- `src/DocAgent.Indexing/BM25SearchIndex.cs` — ISearchIndex with Lucene.Net BM25, PerFieldAnalyzerWrapper, DocAgentSimilarity, RAMDirectory constructor for tests
- `tests/DocAgent.Tests/BM25SearchIndexTests.cs` — 7 tests: display-name lookup, name-over-doc ranking, getRef→GetReferences, XML→XMLParser, GetAsync, null GetAsync, empty index
- `Directory.Packages.props` — Added Lucene.Net and Lucene.Net.Analysis.Common 4.8.0-beta00017
- `src/DocAgent.Indexing/DocAgent.Indexing.csproj` — Added PackageReferences and NoWarn NU1701

## Decisions Made

- **WordDelimiterFilter replaced with custom regex tokenizer:** WordDelimiterFilter in beta00017 does not split upper→upper+lower transitions (e.g., `XMLParser` remains a single token). Custom `CamelCaseTokenizer` uses lookbehind/lookahead regex to correctly split both patterns.
- **BM25 parameters for symbolName:** k1=2.0 (saturates term frequency slower, rewards multiple matches), b=0.5 (reduced field-length normalization, fairer across short method names and long qualified names).
- **RAMDirectory injection pattern:** Constructor overload accepts `LuceneDirectory` — callers control lifecycle. Tests use `RAMDirectory`, production uses `FSDirectory` via `artifactsDir` string constructor.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] WordDelimiterFilter does not split XMLParser-style acronym boundaries**
- **Found during:** Task 2 (BM25SearchIndex unit tests)
- **Issue:** `CamelCase_splits_acronyms` test failed — "XMLParser" produced only `[xmlparser]` instead of `[xmlparser, xml, parser]`. WordDelimiterFilter 4.8.0-beta00017 does not split at uppercase-run→capitalized-word boundaries.
- **Fix:** Replaced `WordDelimiterFilter`-based pipeline with a custom `CamelCaseTokenizer` using regex `(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])` to split on both lower→upper and upper→upper+lower transitions. Emit original token plus sub-parts.
- **Files modified:** `src/DocAgent.Indexing/CamelCaseAnalyzer.cs`
- **Verification:** Tokenizer diagnostic test confirmed `XMLParser => [xmlparser, xml, parser]`; `CamelCase_splits_acronyms` test passes.
- **Committed in:** f2c9c4e (Task 2 commit)

**2. [Rule 1 - Bug] TokenStream contract violation — missing Dispose() call in TokenizeQuery**
- **Found during:** Task 2 (tokenizer diagnostic investigation)
- **Issue:** Lucene.Net requires `TokenStream.Dispose()` (which calls `End()` + closes the stream) before reusing the same Analyzer instance. Missing call caused `InvalidOperationException: TokenStream contract violation: Close() call missing`.
- **Fix:** Added `stream.Dispose()` after `stream.End()` in `TokenizeQuery` method.
- **Files modified:** `src/DocAgent.Indexing/BM25SearchIndex.cs`
- **Verification:** No exceptions; all 7 BM25 tests pass.
- **Committed in:** f2c9c4e (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 - Bug)
**Impact on plan:** Both were necessary for correctness. No scope creep; same public interface, same test coverage scope.

## Issues Encountered

- `WordDelimiterFilter` behavior in Lucene.Net 4.8.0-beta00017 differs from documentation expectations. Investigated via in-process diagnostic test (TokenizerDiagTests, removed after use). Custom tokenizer is simpler and more maintainable than relying on WordDelimiterFilter flag combinations.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `BM25SearchIndex` ready to be wired as the production `ISearchIndex` implementation
- `IKnowledgeQueryService` can now delegate to `BM25SearchIndex` for `SearchAsync` and `GetSymbolAsync`
- `InMemorySearchIndex` remains in codebase for non-production use (e.g., test doubles); plan says it should not be used in non-test code paths

---
*Phase: 03-bm25-search-index*
*Completed: 2026-02-26*
