# Architecture

## Layers

- **Core (Domain):** pure types + interfaces; no IO
- **Ingestion:** source discovery, parsing, normalization → snapshots
- **Indexing:** build/query indexes from snapshots
- **Serving:** MCP tools + security + authn/authz boundaries
- **Orchestration:** Agent Framework workflows calling MCP tools
- **Host:** Aspire app host + wiring (config, storage, telemetry)

This keeps “memory building” and “memory serving” separate, which helps security and testing.

## Key abstractions

### Sources

```csharp
public interface IProjectSource
{
    Task<ProjectInventory> DiscoverAsync(ProjectLocator locator, CancellationToken ct);
}

public interface IDocSource
{
    Task<DocInputSet> LoadAsync(ProjectInventory inv, CancellationToken ct);
}
```

Provide implementations for:
- local filesystem
- remote git repo (read-only cache)
- (V3) NuGet metadata source

### Symbol graph builder

```csharp
public interface ISymbolGraphBuilder<TDoc>
    where TDoc : class
{
    Task<SymbolGraphSnapshot> BuildAsync(ProjectInventory inv, TDoc docs, CancellationToken ct);
}
```

The generic keeps the builder independent of *how* docs were obtained.

### Indexes

```csharp
public interface ISearchIndex
{
    Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, SearchOptions options, CancellationToken ct);
}

public interface IVectorIndex
{
    Task UpsertAsync(IEnumerable<VectorRecord> records, CancellationToken ct);
    Task<IReadOnlyList<VectorHit>> SimilarAsync(float[] embedding, int k, CancellationToken ct);
}
```

### Query facade

```csharp
public interface IKnowledgeQueryService
{
    Task<SymbolNode?> GetSymbolAsync(SymbolId id, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
    Task<GraphDiff> DiffAsync(SnapshotRef a, SnapshotRef b, CancellationToken ct);
}
```

### Tool surface (MCP)

Expose a narrow toolset that maps to `IKnowledgeQueryService`. Avoid “free-form filesystem” tools in V1.

## Polymorphism patterns

- `IProjectSource` and `IDocSource` are polymorphic; register multiple and select via a `SourceKind` discriminator.
- Extensions for composition:
  - `services.AddDocAgentCore()`
  - `services.AddDocAgentIngestion()`
  - `services.AddDocAgentMcpServer()`
- Prefer records for immutable domain types; explicit versioning for snapshots and tool contracts.

## Storage

- V1: write snapshots + indexes to `artifacts/`
- Optional: SQLite for metadata + snapshot catalog
- V2+: add content-addressed storage (hash keys)

## Telemetry

Use `Microsoft.Extensions.Logging` + OpenTelemetry hooks.
Log every tool call with:
- tool name
- requester identity (if available)
- duration
- status
