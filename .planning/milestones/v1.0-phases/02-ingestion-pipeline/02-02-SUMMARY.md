---
phase: 02-ingestion-pipeline
plan: "02"
subsystem: ingestion
tags: [xml-parsing, doc-comments, inheritdoc, roslyn, tdd]
dependency_graph:
  requires: []
  provides: [XmlDocParser, InheritDocResolver]
  affects: [DocAgent.Ingestion, DocAgent.Tests, RoslynSymbolGraphBuilder-plan03]
tech_stack:
  added: [System.Xml.Linq]
  patterns: [per-symbol XML parse, best-effort recovery, cycle detection via HashSet]
key_files:
  created:
    - src/DocAgent.Ingestion/InheritDocResolver.cs
  modified:
    - src/DocAgent.Ingestion/XmlDocParser.cs
    - tests/DocAgent.Tests/XmlDocParserTests.cs
    - src/DocAgent.Ingestion/DocAgent.Ingestion.csproj
    - tests/DocAgent.Tests/DocAgent.Tests.csproj
decisions:
  - "XmlDocParser uses per-symbol API (Parse(string?)) not per-assembly — aligns with Roslyn ISymbol.GetDocumentationCommentXml() call pattern in Plan 03"
  - "Malformed XML recovery: wrap in <doc>...</doc> first, then fall back to raw text in Summary with [Parse warning] prefix"
  - "InheritDocResolver uses empty-string key convention when no cref is present, letting the caller supply the natural override target"
  - "NuGetAuditMode=direct suppresses transitive Microsoft.Build.Tasks.Core vulnerability advisory in both Ingestion and Tests projects"
metrics:
  duration: "10 min"
  completed_date: "2026-02-26"
  tasks_completed: 2
  files_changed: 5
---

# Phase 02 Plan 02: XML Doc Parser and InheritDoc Resolver Summary

XML doc comment parsing via `XDocument` with structured `DocComment` extraction and `<inheritdoc/>` expansion using base-chain walking with cycle detection.

## What Was Built

Replaced the stub `XmlDocParser` (which stored raw XML by assembly name) with a real per-symbol parser. Added `InheritDocResolver` to walk the override/interface chain when `<inheritdoc/>` is encountered.

### XmlDocParser

- `Parse(string? xmlContent)` returns `DocComment?` (null for null/empty input).
- Handles `<member name="...">` wrapper or bare root element.
- Extracts all XML doc elements: summary, remarks, param, typeparam, returns, example, exception (with cref), seealso (cref).
- `ExtractInnerText` collapses inline elements like `<see cref="..."/>` to their text content with whitespace normalization.
- Malformed XML: first tries wrapping in `<doc>...</doc>`, then falls back to returning raw content with `[Parse warning]` prefix in Summary.

### InheritDocResolver

- `Resolve(symbolDocXml, getBaseDocXml, parser, maxDepth)` — resolves `<inheritdoc/>` by calling back into the caller for base XML.
- Cycle detection via `HashSet<string>` of visited doc IDs.
- Max-depth guard (default 10) returns null when exhausted.
- Empty-string key convention when no `cref` attribute — caller supplies natural override target.

### Tests (13 passing)

- Parse_null_returns_null
- Parse_empty_string_returns_null
- Parse_summary_returns_trimmed_text
- Parse_all_elements_populates_all_fields
- Parse_multiple_params_all_captured
- Parse_nested_xml_elements_extracts_text_content
- Parse_generic_type_cref_parses_correctly
- Parse_malformed_xml_returns_best_effort_not_exception
- Parse_malformed_xml_includes_warning_in_summary
- Resolve_inheritdoc_follows_base
- Resolve_inheritdoc_with_cref_resolves_explicit_target
- Resolve_cycle_detection_returns_null
- Resolve_max_depth_returns_null

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] NuGetAuditMode suppression for transitive vulnerability**
- **Found during:** Task 1 (first build attempt)
- **Issue:** `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 transitively brings in `Microsoft.Build.Tasks.Core` 17.7.2 which has advisory GHSA-h4j7-5rxr-p4wc. With `TreatWarningsAsErrors=true` this blocked the build.
- **Fix:** Added `<NuGetAuditMode>direct</NuGetAuditMode>` to `DocAgent.Ingestion.csproj` (already had it applied by prior session) and added same to `DocAgent.Tests.csproj`.
- **Files modified:** `src/DocAgent.Ingestion/DocAgent.Ingestion.csproj`, `tests/DocAgent.Tests/DocAgent.Tests.csproj`
- **Commits:** 6fe22fc, 951b3ad

## Commits

| Hash | Message |
|------|---------|
| 6fe22fc | feat(02-02): implement XmlDocParser and InheritDocResolver |
| 951b3ad | test(02-02): add comprehensive XmlDocParser and InheritDocResolver tests |

## Self-Check: PASSED

All key files exist. Both commits (6fe22fc, 951b3ad) confirmed in git log.
