# Phase 27 Research: Documentation Refresh

**Date:** 2026-03-08
**Discovery Level:** 0 (pure internal work, no external dependencies)

## Tool Inventory

Extracted from `[McpServerTool]`-decorated methods across 4 tool files. Total: **14 tools**.

### DocTools.cs (7 tools)

| # | Tool Name | Parameters | Has projectFilter | Format Options |
|---|-----------|-----------|-------------------|----------------|
| 1 | `search_symbols` | query (string), kindFilter (string?), project (string?), offset (int=0), limit (int=20), fullDocs (bool=false), format (string="json") | Yes (`project`) | json, markdown, tron |
| 2 | `get_symbol` | symbolId (string), includeSourceSpans (bool=false), format (string="json") | No | json, markdown, tron |
| 3 | `get_references` | symbolId (string), crossProjectOnly (bool=false), includeContext (bool=false), format (string="json"), offset (int=0), limit (int=0) | No | json, markdown, tron |
| 4 | `find_implementations` | symbolId (string), format (string="json") | No | json, markdown, tron |
| 5 | `get_doc_coverage` | project (string?), format (string="json") | Yes (`project`) | json, markdown, tron |
| 6 | `diff_snapshots` | versionA (string), versionB (string), includeDiffs (bool=false), format (string="json") | No | json, markdown, tron |
| 7 | `explain_project` | chainedEntityDepth (int=1), includeSections (string?), excludeSections (string?), format (string="json") | No | json, markdown, tron |

### ChangeTools.cs (3 tools)

| # | Tool Name | Parameters | Has projectFilter | Format Options |
|---|-----------|-----------|-------------------|----------------|
| 8 | `review_changes` | versionA (string), versionB (string), verbose (bool=false), format (string="json") | No | json, markdown, tron |
| 9 | `find_breaking_changes` | versionA (string), versionB (string), format (string="json") | No | json, markdown, tron |
| 10 | `explain_change` | versionA (string), versionB (string), symbolId (string), format (string="json") | No | json, markdown, tron |

### IngestionTools.cs (2 tools)

| # | Tool Name | Parameters | Has projectFilter | Format Options |
|---|-----------|-----------|-------------------|----------------|
| 11 | `ingest_project` | path (string), include (string?), exclude (string?), forceReindex (bool=false) | No | N/A (JSON only) |
| 12 | `ingest_solution` | path (string) | No | N/A (JSON only) |

### SolutionTools.cs (2 tools)

| # | Tool Name | Parameters | Has projectFilter | Format Options |
|---|-----------|-----------|-------------------|----------------|
| 13 | `explain_solution` | snapshotHash (string) | No | N/A (JSON only) |
| 14 | `diff_solution_snapshots` | before (string), after (string) | No | N/A (JSON only) |

## Current CLAUDE.md State

The `### MCP Tools (planned surface)` section currently lists only 5 tools:
```
search_symbols, get_symbol, get_references, diff_snapshots, explain_project
```

This is the v1.0 tool set. Missing 9 tools added in v1.1, v1.2, and v1.5.

## Gaps

1. **Missing tools (9):** find_implementations, get_doc_coverage, review_changes, find_breaking_changes, explain_change, ingest_project, ingest_solution, explain_solution, diff_solution_snapshots
2. **No parameter documentation:** None of the 5 listed tools have parameters documented
3. **No format options documented:** All DocTools/ChangeTools support json/markdown/tron
4. **No projectFilter documentation:** search_symbols and get_doc_coverage accept project filter
5. **Section header says "(planned surface)":** Should reflect current shipped state
6. **No return shape documentation:** No indication of response envelope structure

## Plan

Single plan (27-01) with 1 task: rewrite the MCP Tools section of CLAUDE.md with complete, verified tool documentation.
