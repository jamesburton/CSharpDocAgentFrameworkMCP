# Worktrees and parallel delivery

This repo is designed to be built in parallel. Use git worktrees so each agent (or human) works isolated.

## Suggested layout

```
repo/
  wt/
    main/
    ingestion-xml/
    roslyn-symbolgraph/
    mcp-server/
    agent-orchestration/
    aspire-host/
    tests-infra/
```

## Commands

```bash
mkdir -p wt
git worktree add wt/main main

git worktree add -b feature/ingestion-xml wt/ingestion-xml main
git worktree add -b feature/roslyn-symbolgraph wt/roslyn-symbolgraph main
git worktree add -b feature/mcp-server wt/mcp-server main
git worktree add -b feature/agent-orchestration wt/agent-orchestration main
git worktree add -b feature/aspire-host wt/aspire-host main
git worktree add -b feature/tests-infra wt/tests-infra main
```

## Policy

- Each worktree must:
  - have passing tests
  - add/update docs
  - avoid cross-cutting changes unless coordinated
- Merge via PRs, keep commits focused.
