# Guidance for GitHub Copilot (chat/agent)

## Repo intent
You are working in a codebase that builds a **documentation memory system** and exposes it via an **MCP server**.

## Rules
- Do not widen tool surface area without updating `docs/Security.md` and tests.
- All new features require tests.
- Keep Core layer pure (no IO).

## Expected workflow
1. Create a worktree + feature branch.
2. Implement smallest vertical slice.
3. Add tests first where possible.
4. Update docs (Plan/Architecture) if you changed boundaries.

## Output format
- Prefer PR-style summaries and commit-ready changes.
- Provide a checklist of tests run.
