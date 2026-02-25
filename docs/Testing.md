# Testing strategy

## Rules
- Every feature must be covered by tests to be “done”.
- Prefer deterministic fixtures. No network in unit tests.
- Component tests can spin in-proc servers.

## Test pyramid

### Unit tests (fast)
- XML doc parser: member binding, overloaded methods, generics
- Roslyn collector: symbol IDs, file spans, partial types
- Package mapping: csproj, Directory.Packages.props, nuspec, lock file parsing
- Diff engine: stable diffs

### Component tests (medium)
- Ingestion pipeline end-to-end on fixture repo
- Indexing + query service

### End-to-end tests (slower)
- Start MCP server (in-proc or stdio)
- Call tools, validate responses

## Coverage gates
- Core + ingestion: high coverage target
- Serving layer: at least cover tool routing and authz

## Golden files
Store expected snapshots under `tests/Golden/` and compare with stable serialization.
