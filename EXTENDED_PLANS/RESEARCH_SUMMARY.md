# RESEARCH_SUMMARY.md
## Similar / adjacent tooling and how it relates

### Microsoft Agent Framework (orchestration)
- Purpose: build agents + workflows; integrates with MCP servers.
- It does not provide compiler-grade code understanding by itself.
- Use it to orchestrate tools built by CSharpDocAgentFrameworkMCP.

Links
- Overview: https://learn.microsoft.com/en-us/agent-framework/overview/
- MCP tools with agents: https://learn.microsoft.com/en-us/agent-framework/agents/tools/local-mcp-tools
- First agent: https://learn.microsoft.com/en-us/agent-framework/get-started/your-first-agent

---

### MCP (Model Context Protocol) + C# SDK (tool surface)
- MCP standardizes how tools/context are offered to models/agents.
- The C# SDK and examples show stdio-first MCP server patterns.

Links
- C# MCP server blog: https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/
- C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP on Windows overview: https://learn.microsoft.com/en-us/windows/ai/mcp/overview

---

### Language Server Protocol (live code intelligence)
- LSP provides go-to-def, hover, references, diagnostics, code actions.
- Great for “live interrogation” but not a persistent memory format.

Links
- LSP home: https://microsoft.github.io/language-server-protocol/
- Spec 3.17: https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/
- VS Code language server guide: https://code.visualstudio.com/api/language-extensions/language-server-extension-guide

---

### Tree-sitter (polyglot AST ingestion)
- Incremental parsing library with parsers for many languages.
- Syntax-level only; semantics require additional adapters.

Links
- Repo: https://github.com/tree-sitter/tree-sitter
- Docs: https://tree-sitter.github.io/
- Using parsers: https://tree-sitter.github.io/tree-sitter/using-parsers/

---

### Code intelligence platforms (conceptual peers)
#### Sourcegraph
- Cross-repo search + navigation, SCIP indexing for precise navigation.
- Often SaaS/enterprise product; your framework is a local/agent-native spine.

Links
- Cross-repo navigation blog (SCIP): https://sourcegraph.com/blog/cross-repository-code-navigation
- Sourcegraph home: https://sourcegraph.com/

---

### Semantic analysis & rewriting inspiration
#### CodeQL
- Semantic code analysis engine; query code as data.
Links:
- https://codeql.github.com/
- https://docs.github.com/code-security/code-scanning/introduction-to-code-scanning/about-code-scanning-with-codeql

#### Semgrep / ast-grep
- AST-based pattern matching and autofix (Semgrep) and migration-oriented AST tooling (ast-grep).
Links:
- Semgrep journey: https://semgrep.dev/blog/2021/semgrep-a-static-analysis-journey
- AST-based autofix: https://semgrep.dev/blog/2022/autofixing-code-with-semgrep
- ast-grep comparison: https://ast-grep.github.io/advanced/tool-comparison.html

---

### Documentation ecosystems (inputs, not memory formats)
- DocFX (.NET): https://dotnet.github.io/docfx/
- TypeDoc (TS): https://typedoc.org/
- Sphinx (Python): https://www.sphinx-doc.org/
- rustdoc (Rust): https://doc.rust-lang.org/rustdoc/
- godoc (Go): https://go.dev/blog/godoc
- Doxygen (C++ and more): https://www.doxygen.nl/
- DocC (Swift): https://www.swift.org/documentation/docc/
