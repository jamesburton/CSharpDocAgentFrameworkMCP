# Guidance for GSD (Get Shit Done) style multi-agent workflow

## Orchestration pattern
- Thin orchestrator spawns specialized agents for:
  - ingestion
  - symbol graph
  - mcp server
  - aspire wiring
  - tests + fixtures
  - docs

## Worktree policy
- One agent = one worktree = one PR.
- Avoid cross-worktree conflicts by owning a directory or interface boundary.

## Acceptance criteria
- Every stage produces a verifiable artifact (tests or golden files).
- No stage is “done” without green tests.
