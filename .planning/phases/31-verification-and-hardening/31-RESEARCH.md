# Research: Phase 31 Verification and Hardening

**Focus**: Performance profiling, error handling robustness, and security validation of the TypeScript ingestion pipeline.

## Performance Analysis
- **TypeChecker Overhead**: The TypeScript `TypeChecker` is computationally expensive. For large projects, `createProgram` can take seconds or even minutes.
- **Process Spawning**: Spawning a new Node.js process per ingestion is safe but adds a ~500ms-1s overhead. Incremental hits currently avoid this, which is good.
- **Memory Consumption**: Large ASTs and type graphs can consume significant memory. The 512MB limit needs to be verified against a project like `zod` or `lucide-react`.

## Error Handling Scenarios
- **Invalid `tsconfig.json`**: Ensure clear errors (handled in Phase 30, but need negative tests).
- **Missing `node_modules`**: The sidecar needs to handle cases where `npm install` hasn't been run in the *target* project (though `tsc` usually handles this via types).
- **Broken Symlinks**: Windows path issues with symlinks in `node_modules`.
- **Syntax Errors**: TypeScript compiler typically continues despite syntax errors, but we should verify the graph remains "best effort" rather than failing entirely.

## Security Hardening
- **Path Traversal**: Verify that `PathAllowlist` prevents `tsconfig.json` from referencing files outside the allowed scope (via `extends` or `include`).
- **Process Isolation**: The sidecar runs with the same permissions as the host. For true hardening, we might need a low-privilege sandbox, but for this milestone, ensuring `PathAllowlist` is enforced at the C# boundary is the priority.

## Robustness
- **Node.js Availability**: The `NodeAvailabilityValidator` is implemented. Verify it correctly handles systems with only `node.exe` vs `node` in PATH.
- **Concurrent Ingestion**: The `SemaphoreSlim` in `TypeScriptIngestionService` handles this. Verify it doesn't cause deadlocks under high load.

## Action Items for Planning
1. Create a "Stress Test" fixture with 100+ files.
2. Implement negative test suite for error conditions.
3. Profile memory and CPU during sidecar execution.
4. Verify `PathAllowlist` enforcement in `ingest_typescript`.
