# DocAgentFramework (V1 scaffold)

A .NET 10 / C# solution blueprint for turning **code documentation + code structure** into **agent-consumable memory**, exposed via a **securable MCP server**, and orchestrated with **Microsoft Agent Framework**.

This repo is intentionally a **scaffold**: it focuses on architecture, contracts, flows, and tests-first scaffolding you can implement in parallel using **git worktrees**.

> Why this exists: XML documentation files are easy to generate in .NET, but they’re not the best *direct* “memory” format for agents. This project ingests XML docs + Roslyn symbol info, normalizes it into a stable “symbol graph” and a searchable index (BM25 + embeddings), then serves it as tools over MCP.


## What you get (in this archive)

- **Layered plan** with parallelizable worktrees and blockers: `docs/Plan.md`
- **Architecture** (interfaces, generics, DI, clean boundaries): `docs/Architecture.md`
- **Skill definition** for “unusual change review” + branch/worktree flow: `docs/Skills/UnusualChangeReview.skill.md`
- **Agent guidance** files for Copilot, Claude Code, Gemini, and GSD: `.ai/*`
- A minimal **solution layout** (projects + contracts, stub code): `src/*`
- **Test strategy + acceptance criteria** for every layer: `docs/Testing.md`
- Curated **References** with local links and external sources: `docs/References.md`
- A reasonable `.gitignore` that includes standard .NET ignores plus common “local agentic” artifacts


## Core idea

### Is C# XML documentation a good agent format?
It’s *workable*, but not ideal:

- XML docs are **per-assembly outputs** and often miss context (cross-file relationships, usage patterns, partials, generated code).
- Agents benefit more from **typed, normalized, queryable** representations:
  - `Symbol` → `Members` → `Docs` (summary/remarks/params/returns/examples)
  - `Relationships` (inherits/implements/calls/depends-on)
  - `Source anchors` (file path + span + commit hash)

So V1 treats XML docs as **one input**, not the final memory format.


## Quick start (once code is implemented)

```bash
# build + run tests
dotnet test

# start MCP server locally (stdio transport)
dotnet run --project src/DocAgent.McpServer

# optionally run under Aspire (app host)
dotnet run --project src/DocAgent.AppHost
```

> File-based apps in .NET 10 can run single `.cs` files directly via `dotnet run file.cs` or `dotnet file.cs`. See `tools/file-based/` for examples.


## Recommended repo workflow (parallel by default)

- One “integration” worktree for main: `wt/main`
- Multiple feature worktrees:
  - `wt/ingestion-xml`
  - `wt/roslyn-symbolgraph`
  - `wt/mcp-server`
  - `wt/agent-orchestration`
  - `wt/aspire-host`
  - `wt/tests-infra`

See `docs/Worktrees.md` for commands and conventions.


## Security notes (non-negotiable for MCP)

MCP servers become a high-trust bridge to local resources. Treat them like you would a CLI with filesystem access:

- default-deny toolset, explicit allowlists
- authn/authz for non-stdio transports
- audit logging for every tool call
- sandbox paths (repo-root only by default)
- defense-in-depth against prompt injection

See `docs/Security.md` and the MCP references.


## License

MIT (suggested). Replace as needed.
