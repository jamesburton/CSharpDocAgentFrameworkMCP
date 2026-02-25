# POLYGLOT_STRATEGY.md
## Goal: polyglot with minimal re-engineering

### Key idea
Use a **single, versioned SymbolGraph schema** as the interchange format.
Add language adapters that map native tooling output into that schema.

---

## Adapter tiers

### Tier A: Syntax adapters (fastest)
Use Tree-sitter to extract:
- modules/namespaces (best-effort)
- functions/methods
- types/classes/interfaces
- doc comments / docstrings
- file spans

Pros
- wide language coverage quickly
- deterministic and fast
Cons
- limited semantic resolution (imports, overloads, types)

Tree-sitter references
- https://github.com/tree-sitter/tree-sitter
- https://tree-sitter.github.io/

---

### Tier B: Semantic adapters (select languages)
Add deeper integrations where RoI is highest:
- **C#**: Roslyn (gold standard)
- **TypeScript**: TS compiler API
- **Go**: go/packages + gopls (LSP)
- **Rust**: rust-analyzer (LSP + internal model)
- **Python**: pyright/pylance (LSP) + AST + import graph

---

## Unified “capabilities” model
Each adapter declares capabilities (feature flags):
- `HasSemanticTypes`
- `HasCallGraph`
- `HasDataFlow`
- `HasDiagnostics`
- `HasDocModel`
- `HasRewriteEngine`

The MCP tools can degrade gracefully depending on availability.

---

## Minimal re-engineering rule
Never bake a language’s quirks into the core schema.
Instead:
- keep core schema small and stable
- put language-specific fields into an `Extensions` bag keyed by language id
- version extensions independently

---

## LSP as a universal “live” backend
Even if you don’t ingest full semantics for a language, LSP can provide:
- definitions
- references
- hover docs
- diagnostics

LSP references
- https://microsoft.github.io/language-server-protocol/
- https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/
