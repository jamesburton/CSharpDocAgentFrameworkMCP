# Guidance for Claude Code

## Operating constraints
- Always use a dedicated worktree per task.
- Avoid editing generated files unless specifically asked.
- Keep edits minimal and incremental; make tests pass continuously.

## Quality bars
- Deterministic outputs for snapshots and indexes.
- Strong typing and explicit versioning.
- No implicit network calls in tests.

## Done criteria per task
- Unit tests added/updated
- `dotnet test` passes
- Docs updated where relevant
