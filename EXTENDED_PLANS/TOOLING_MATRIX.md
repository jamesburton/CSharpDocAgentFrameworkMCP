# TOOLING_MATRIX.md
## Feature matrix: what exists and how it composes with CSharpDocAgentFrameworkMCP

Legend:
- ✅ strong support
- 🟨 partial / requires glue
- ❌ not a focus

| Tooling | Persistent memory | Semantic model | Polyglot | Live interrogation | Safe tool surface | Notes |
|---|---:|---:|---:|---:|---:|---|
| Roslyn | 🟨 | ✅ | ❌ | ✅ | 🟨 | Great truth engine; needs serving + memory |
| Agent Framework | ✅ (orchestration) | ❌ | 🟨 | 🟨 | ✅ | Orchestrates tools; integrates with MCP |
| MCP (C# SDK) | 🟨 | ❌ | 🟨 | 🟨 | ✅ | Defines tool protocol; you supply intelligence |
| LSP | ❌ | 🟨 | ✅ | ✅ | 🟨 | Great “live” backend, not memory |
| Tree-sitter | ❌ | ❌ | ✅ | 🟨 | 🟨 | Syntax-only, very useful for polyglot on-ramp |
| Sourcegraph | 🟨 | ✅ (via indexing) | ✅ | 🟨 | 🟨 | Product platform; conceptual peer |
| CodeQL | 🟨 | ✅ | ✅ | 🟨 | 🟨 | Query engine; heavy but powerful |
| Semgrep/ast-grep | ❌ | 🟨 | ✅ | 🟨 | 🟨 | Pattern-based analysis/rewrites |

Key links:
- Agent Framework: https://learn.microsoft.com/en-us/agent-framework/overview/
- MCP server in C#: https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/
- MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- LSP: https://microsoft.github.io/language-server-protocol/
- Tree-sitter: https://github.com/tree-sitter/tree-sitter
- Sourcegraph: https://sourcegraph.com/
- CodeQL: https://codeql.github.com/
- Semgrep: https://semgrep.dev/blog/2021/semgrep-a-static-analysis-journey
- ast-grep: https://ast-grep.github.io/advanced/tool-comparison.html

---

## Suggested permutations (product “packs”)
### Pack 1 (highest RoI): C# compiler truth
Roslyn snapshot + semantic diffs + MCP tools + Agent Framework orchestration

### Pack 2: Polyglot quick win
Tree-sitter snapshot + MCP tools (search/anchors) + optional LSP live bridge

### Pack 3: Verified refactors
Snapshot diffs + rewrite engine + test gates (manual approval)

### Pack 4: Enterprise ops
Audit logs + allowlists + policy gates + storage hardening
