# LIVE_INTERROGATION_INTERFACE.md
## Programmable interrogation: “agents can question the live code tooling”

### Why this matters
Agents are best when they can verify claims.
A live interrogation surface lets an agent ask the compiler/language service directly.

---

## Approach 1: Roslyn-backed analysis service (C#)
Expose structured questions:
- “explain this diagnostic and propose fixes”
- “show reachable callers for this method”
- “what is the inferred nullability here”
- “where are allocations on this path”

Implementation options
- in-proc analyzers (fast, but host coupling)
- dedicated analysis process (safer isolation)

---

## Approach 2: LSP bridge (polyglot live intelligence)
Use LSP requests:
- `textDocument/hover`
- `textDocument/definition`
- `textDocument/references`
- diagnostics streams
- code actions metadata

Primary refs
- https://microsoft.github.io/language-server-protocol/
- https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/

---

## Approach 3: Query language over snapshots (offline / deterministic)
Provide a constrained query DSL (CodeQL-like inspiration) over SymbolGraphSnapshot:
- “find public APIs without docs”
- “find call paths from A to B”
- “list packages changed between snapshots”

Inspiration refs
- https://codeql.github.com/
