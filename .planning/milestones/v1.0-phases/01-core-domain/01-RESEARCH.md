# Phase 1: Core Domain - Research

**Researched:** 2026-02-26
**Domain:** C# domain modeling — value types, immutable records, serialization contracts, interface design
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**SymbolId Identity**
- Use Roslyn XML documentation comment ID format (e.g., `T:Namespace.Type`, `M:Namespace.Type.Method(System.String)`)
- Case-sensitive — match XML doc ID spec exactly; case-insensitive matching is a search-layer concern
- Rename tracking via `PreviousIds` — approach (list on SymbolNode vs. dedicated edge) is Claude's discretion
- Assembly prefix decision (e.g., `MyAssembly|T:...` vs. bare XML doc ID) is Claude's discretion based on multi-assembly scenarios

**Snapshot Schema**
- Multi-format serialization: MessagePack (default), System.Text.Json (debugging), and TRON (middle ground)
- Serialization format should be pluggable — a format enum or strategy pattern
- Content hash algorithm: xxHash (non-cryptographic, fast change detection)
- Schema versioning: semver string (e.g., `"1.0.0"`) — already scaffolded as `string SchemaVersion`
- Add `ContentHash` field (xxHash of serialized content) to `SymbolGraphSnapshot`
- Add `ProjectName` field (source project identifier) to `SymbolGraphSnapshot`

**Interface Surface**
- No pagination on `ISearchIndex.SearchAsync` for now — paginate at MCP tool layer later
- Primary return type: `IAsyncEnumerable<T>` for search operations
- Provide convenience extension method that materializes `IAsyncEnumerable<T>` to `Task<IReadOnlyList<T>>`
- Add `GetReferencesAsync(SymbolId id, CancellationToken ct)` to `IKnowledgeQueryService` — aligns with MCP `get_references` tool
- `IVectorIndex` stub shape is Claude's discretion (don't over-commit to v2 API surface)

**Edge and Node Model**
- Expand `SymbolKind` enum: add Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter
- Expand `DocComment` record: add Exceptions (`IReadOnlyList<(string Type, string Description)>`), SeeAlso (`IReadOnlyList<string>`), TypeParams (`IReadOnlyDictionary<string, string>`)
- Add `Accessibility` enum to `SymbolNode` (Public, Internal, Protected, Private, ProtectedInternal, PrivateProtected)
- Expand `SymbolEdgeKind`: add Overrides, Returns

### Claude's Discretion

- PreviousIds tracking approach (list on SymbolNode vs. RenamedFrom edge)
- Assembly prefix on SymbolId (bare XML doc ID vs. assembly-qualified)
- IVectorIndex stub shape (minimal marker vs. mirrored ISearchIndex)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CORE-01 | Stable `SymbolId` spec with assembly-qualified identity and rename tracking (`PreviousIds`) | XML doc ID format confirmed via Roslyn `ISymbol.GetDocumentationCommentId()`; `readonly record struct` gives value equality automatically; `PreviousIds` as `IReadOnlyList<SymbolId>` on `SymbolNode` is simplest V1 approach |
| CORE-02 | `SymbolGraphSnapshot` schema with version field, content hash, and deterministic serialization | `System.IO.Hashing.XxHash64` ships in-box in .NET 10 (no extra NuGet needed); MessagePack 3.1.4 supports source generators for AOT; `System.Text.Json` property order is deterministic since .NET 7 (metadata order); pluggable format enum is standard strategy pattern |
| CORE-03 | Domain interfaces: `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `ISearchIndex`, `IKnowledgeQueryService` | All five exist in current scaffold; need `IVectorIndex` stub added and `ISearchIndex.SearchAsync` switched to `IAsyncEnumerable<SearchHit>`; `IKnowledgeQueryService` needs `GetReferencesAsync`; .NET 10 ships `AsyncEnumerable.ToListAsync` in-box |
</phase_requirements>

---

## Summary

Phase 1 establishes the pure domain layer: value types, graph schema, and interface contracts that all downstream phases depend on. The scaffolding in `DocAgent.Core` (`Symbols.cs`, `Abstractions.cs`) is a working starting point but requires targeted expansion to match the locked decisions — `SymbolGraphSnapshot` needs `ContentHash` and `ProjectName` fields, `SymbolKind` and `SymbolEdgeKind` enums need additional members, `DocComment` needs richer metadata fields, and `ISearchIndex.SearchAsync` must return `IAsyncEnumerable<SearchHit>` rather than `Task<IReadOnlyList<SearchHit>>`. The `IVectorIndex` stub must be added.

The key technical insight for this phase is that `.NET 10` makes several things easy that would have required extra NuGet packages before: `System.IO.Hashing.XxHash64` is in-box (no third-party xxHash library needed), and `System.Linq.AsyncEnumerable` provides `ToListAsync` in-box (no `System.Linq.Async` package needed). MessagePack 3.1.4 requires `[MessagePackObject]` / `[Key]` attributes on types, which means the Core domain types must be attributed or use the `ContractlessStandardResolver` — this is a key design choice that affects how "pure" the domain layer stays.

The golden-file test for CORE-01's rename/equality scenario is best served by the `Verify` library (NuGet: `Verify.Xunit`), which produces `.verified.txt` snapshot files committed to the repo. The FluentAssertions v7 pin (currently `6.12.1` in `Directory.Packages.props`) is safe and should be explicitly capped at `< 8.0.0` to avoid the Xceed commercial license introduced in v8.

**Primary recommendation:** Expand existing scaffold types in-place, attribute them for MessagePack using `ContractlessStandardResolver` to avoid polluting domain records with binary-serialization concerns, add `Verify.Xunit` for golden-file tests, and use `System.IO.Hashing` (in-box) for xxHash content hashing.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.IO.Hashing` | 10.0.x (in-box .NET 10) | XxHash64 content hash for `SymbolGraphSnapshot.ContentHash` | Ships with .NET 10 runtime; no extra NuGet reference needed; `XxHash64.Hash(ReadOnlySpan<byte>)` is a one-liner |
| `MessagePack` | 3.1.4 | Binary serialization (default format) | Fastest binary serializer in .NET ecosystem; v3 added full AOT source-generator support; Microsoft-recommended BinaryFormatter migration target |
| `System.Text.Json` | In-box (.NET 10) | JSON serialization (debugging / human-readable format) | In-box; deterministic property ordering since .NET 7 (metadata order); native support for records and positional constructors |
| `xunit` | 2.7.1 (already pinned) | Test framework | Already in solution |
| `FluentAssertions` | 6.12.1 (already pinned, cap at `< 8.0.0`) | Test assertions | v7.x remains Apache 2.0; v8+ is commercial (Xceed). Current pin `6.12.1` is safe but should be capped |
| `Verify.Xunit` | 31.x | Golden-file / snapshot tests for CORE-01 rename scenario | De-facto standard for snapshot testing in .NET; produces committed `.verified.txt` files; integrates with xUnit |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `MessagePack.SourceGenerator` | 2.6.x-alpha (or bundled with MessagePack 3.x) | AOT-safe formatter generation | Use if targeting NativeAOT or trimming; not strictly required for .NET 10 server scenario but avoids reflection |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `System.IO.Hashing.XxHash64` (in-box) | `K4os.Hash.xxHash` (NuGet) | No advantage; in-box is preferred for .NET 10 targets |
| `MessagePack` with `ContractlessStandardResolver` | Attribute all domain types with `[MessagePackObject]` | Attributing domain types with binary serialization concerns violates Core's "no infrastructure dependencies" goal; `ContractlessStandardResolver` avoids this at a slight performance cost |
| `Verify.Xunit` | `ApprovalTests` | Both work; `Verify` is more actively maintained and has better .NET 10 support as of 2025 |
| `FluentAssertions` v6 | `Shouldly`, `TUnit` assertions | `FluentAssertions` already pinned and familiar; no reason to switch |

### Installation

```bash
# Add to tests/DocAgent.Tests/DocAgent.Tests.csproj
dotnet add tests/DocAgent.Tests package Verify.Xunit

# Add to src/DocAgent.Core/DocAgent.Core.csproj (MessagePack for default serializer)
dotnet add src/DocAgent.Core package MessagePack

# System.IO.Hashing is in-box for net10.0 — no add needed

# Central version pins to add to src/Directory.Packages.props:
# <PackageVersion Include="MessagePack" Version="3.1.4" />
# <PackageVersion Include="Verify.Xunit" Version="31.*" />
```

**FluentAssertions version cap** — update `Directory.Packages.props`:
```xml
<!-- Cap at < 8.0.0 to stay on Apache 2.0 license -->
<PackageVersion Include="FluentAssertions" Version="[6.12.1,8.0.0)" />
```

---

## Architecture Patterns

### Recommended File Structure (Phase 1 scope)

```
src/DocAgent.Core/
├── Symbols.cs           # SymbolId, SymbolNode, SymbolEdge, SymbolGraphSnapshot, enums
├── Abstractions.cs      # All six interfaces + extension methods
└── DocAgent.Core.csproj

tests/DocAgent.Tests/
├── SymbolIdTests.cs          # CORE-01: value equality + PreviousIds rename tracking
├── SnapshotSerializationTests.cs  # CORE-02: roundtrip + ContentHash determinism
├── InterfaceCompilationTests.cs   # CORE-03: compile-time contracts (can be minimal)
├── Snapshots/                     # Verify .verified.txt golden files
└── DocAgent.Tests.csproj
```

### Pattern 1: `readonly record struct` for SymbolId

**What:** Use `readonly record struct` (not `record class`) for `SymbolId` — it is a value semantic identity, stack-allocated, zero heap overhead, and auto-generates structural equality, `==`, `!=`, and `GetHashCode`.

**When to use:** Any domain concept that is an identity or value with no mutable state and where allocation pressure matters.

```csharp
// Already in scaffold — correct shape, no changes needed for equality behavior
public readonly record struct SymbolId(string Value);

// Value equality works automatically:
var a = new SymbolId("T:MyNs.MyType");
var b = new SymbolId("T:MyNs.MyType");
Assert.Equal(a, b); // true — structural equality from record struct
```

Source: [C# record structs — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/record-structs)

### Pattern 2: PreviousIds Rename Tracking on SymbolNode

**What:** Store rename history as `IReadOnlyList<SymbolId> PreviousIds` on `SymbolNode`. This is simpler than a dedicated `RenamedFrom` edge (which would require edge traversal to check identity continuity). Reasoning: rename tracking is a property of the symbol's identity, not a graph relationship.

**Recommendation for Claude's discretion:** Use list on SymbolNode for V1. A dedicated edge would only add value if the ingestion pipeline needed to query "what was this symbol formerly known as" across snapshots — that is a Phase 4 (DiffAsync) concern.

```csharp
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,   // rename history; empty list = no renames
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span);
```

### Pattern 3: Assembly Prefix on SymbolId

**What:** For Claude's discretion — use bare Roslyn XML doc ID format (no assembly prefix) in V1.

**Reasoning:** `ISymbol.GetDocumentationCommentId()` returns bare IDs like `T:MyNs.MyType`. Adding an assembly prefix (e.g., `MyAssembly|T:MyNs.MyType`) would require custom ID construction everywhere and cannot round-trip through standard Roslyn APIs. The risk of cross-assembly collision in V1 (single-project ingestion) is near-zero. Solve with a prefix in the `SnapshotStore` path or `ProjectName` field when multi-assembly matters.

**Recommendation for Claude's discretion:** Use bare XML doc ID (no prefix) for V1. Document the assumption that `ProjectName` on the snapshot provides assembly context.

### Pattern 4: ContentHash via XxHash64

**What:** Compute `ContentHash` as a hex string of XxHash64 over the deterministically serialized bytes of the snapshot. Compute it _after_ serialization (hash the wire format, not the in-memory model).

```csharp
using System.IO.Hashing;

// In the serialization layer (not Core itself — Core only holds the field):
// 1. Serialize snapshot to byte[] with the chosen format
// 2. Hash the bytes
byte[] serialized = MessagePackSerializer.Serialize(snapshot);
ulong hash = XxHash64.HashToUInt64(serialized);
string contentHash = hash.ToString("x16"); // 16 hex chars, deterministic
```

Source: [XxHash64 Class — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash64?view=net-10.0-pp)

**Important:** `ContentHash` must be computed externally and stored on the snapshot — it cannot be computed from within the snapshot constructor because that creates a circular dependency (hash of a record that includes the hash field). The field is nullable or set via `with` expression after construction.

### Pattern 5: IAsyncEnumerable for Search Operations

**What:** `ISearchIndex.SearchAsync` returns `IAsyncEnumerable<SearchHit>` (not `Task<IReadOnlyList<SearchHit>>`). Callers that need a list use a convenience extension method.

**Why .NET 10 matters:** `System.Linq.AsyncEnumerable` ships in-box in .NET 10, providing `ToListAsync`, `ToReadOnlyListAsync`, and full LINQ operators on `IAsyncEnumerable<T>`. No `System.Linq.Async` NuGet package needed.

```csharp
// Interface contract (in Abstractions.cs):
public interface ISearchIndex
{
    Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct);
    IAsyncEnumerable<SearchHit> SearchAsync(string query, CancellationToken ct);
    Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct);
}

// Convenience extension method (in Abstractions.cs or a separate Extensions.cs):
public static class SearchIndexExtensions
{
    public static async Task<IReadOnlyList<SearchHit>> SearchToListAsync(
        this ISearchIndex index, string query, CancellationToken ct)
        => await index.SearchAsync(query, ct).ToListAsync(ct);
        // ToListAsync comes from System.Linq.AsyncEnumerable in .NET 10
}
```

Source: [System.Linq.Async in .NET 10](https://steven-giesel.com/blogPost/e40aaedc-9e56-491f-9fe5-3bb0b162ae94), [Breaking change notice](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable)

### Pattern 6: IVectorIndex Stub Shape

**Recommendation for Claude's discretion:** Use a minimal marker interface for V1. Do not mirror `ISearchIndex` — the vector operation semantics (embedding float arrays, k-nearest-neighbor) are different enough that a shared surface would be misleading. Keep it simple.

```csharp
// Stub — enough for CORE-03 to compile; no implementation in V1
public interface IVectorIndex
{
    // V2: UpsertAsync, SimilarAsync
    // Intentionally empty in V1 — implementations are a v2 concern (VCTR-01)
}
```

### Pattern 7: Pluggable Serialization Format

**What:** A `SerializationFormat` enum + a simple factory/strategy. Core defines the enum; the Ingestion layer (Phase 2) or a `DocAgent.Serialization` helper provides the implementations.

```csharp
// In Core (pure enum — no IO dependency):
public enum SerializationFormat
{
    MessagePack,   // default — compact binary
    Json,          // human-readable / debugging
    Tron           // compact text (middle ground) — see Open Questions
}
```

### Anti-Patterns to Avoid

- **Mutable records:** Do not add `set` properties or `init` on fields that could change post-construction. All domain types must be immutable.
- **Reflection-dependent serialization on hot path:** Do not use `dynamic`, `object` boxing, or unresolved `JsonSerializer.Serialize<object>` — be explicit about types.
- **Generic `ISymbolGraphBuilder<TDoc>`:** Architecture.md shows a generic version; the CONTEXT.md decision locked this as non-generic for V1. The scaffold's non-generic version (`ISymbolGraphBuilder`) is correct.
- **Dictionary equality on records:** `IReadOnlyDictionary<K,V>` does not implement structural equality. If `SymbolNode` equality must cover `DocComment.Params`, tests must compare dictionaries explicitly. Consider using `ImmutableDictionary` if structural equality on `DocComment` is needed in tests.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Content hashing | Custom CRC or MD5 | `System.IO.Hashing.XxHash64` | In-box, zero-allocation `Span<byte>` API, 64-bit output fits in a `ulong` |
| Snapshot golden-file tests | String comparison in test | `Verify.Xunit` | Handles diffing, auto-accept on first run, committed `.verified.txt` files, works with complex object graphs |
| IAsyncEnumerable materialization | `foreach` + `List<T>` | `AsyncEnumerable.ToListAsync` (in-box .NET 10) | One-liner, respects cancellation token, no extra package |
| Binary serialization | Custom binary writer | `MessagePack` with `ContractlessStandardResolver` | Handles versioning, schema evolution, pooled buffers |

**Key insight:** .NET 10 absorbs several formerly-NuGet concerns in-box (`System.IO.Hashing`, `System.Linq.AsyncEnumerable`). Avoid adding packages for things the runtime already provides.

---

## Common Pitfalls

### Pitfall 1: ContentHash Field Creates Circular Dependency

**What goes wrong:** If `ContentHash` is a required positional parameter in the `SymbolGraphSnapshot` record constructor, you cannot compute it from the serialized bytes of the snapshot itself (chicken-and-egg).

**Why it happens:** The hash is computed over the serialized bytes, but the serialized bytes include the hash field value.

**How to avoid:** Make `ContentHash` either (a) `string? ContentHash = null` with `init` and set via `with` after serialization in the snapshot store, or (b) compute the hash over all fields _except_ ContentHash (serialize with `ContentHash = ""` sentinel, then patch). Option (a) is cleaner — the field is set by the persistence layer, not the domain constructor.

**Warning signs:** Any code that tries to compute `ContentHash` inside `SymbolGraphSnapshot`'s own constructor or factory method.

### Pitfall 2: IReadOnlyDictionary Breaks Record Structural Equality

**What goes wrong:** `DocComment` contains `IReadOnlyDictionary<string, string> Params`. C# `record` equality uses `EqualityComparer<T>.Default`, which for `IReadOnlyDictionary` checks reference equality, not content equality. Two `DocComment` records with identical `Params` contents will not be `==`.

**Why it happens:** `Dictionary<K,V>` does not implement `IEquatable`. Record synthesized equality delegates to `EqualityComparer<T>.Default.Equals`, which calls `object.Equals` for types without `IEquatable` — i.e., reference equality.

**How to avoid:** For golden-file and equality tests, compare dictionary contents explicitly (`docComment.Params.Should().BeEquivalentTo(expected.Params)`). If structural equality on `DocComment` is critical, switch internal storage to `ImmutableDictionary<K,V>` which implements content equality, or override `Equals`/`GetHashCode` on `DocComment`.

**Warning signs:** Tests asserting `docA.Docs == docB.Docs` returning false despite identical content.

### Pitfall 3: FluentAssertions v8 Auto-Upgrade

**What goes wrong:** `dotnet add package` or `dotnet update` pulls FluentAssertions 8.x, which requires a commercial license from Xceed for non-open-source use.

**Why it happens:** NuGet resolves the latest compatible version unless explicitly bounded.

**How to avoid:** Pin `Directory.Packages.props` to `[6.12.1,8.0.0)` (exclusive upper bound) immediately. The `[` bracket syntax enforces a minimum; the `)` enforces exclusive maximum.

**Warning signs:** Build output mentioning Xceed license file requirement; FA 8.x changelog entries in `packages.lock.json`.

### Pitfall 4: System.Text.Json Property Order Not Guaranteed for Custom Types

**What goes wrong:** Property serialization order for JSON is based on metadata (source) order since .NET 7, but only for types that don't override serialization behavior. If any type uses `[JsonExtensionData]`, custom converters, or source-generator opts-in, order may differ.

**Why it happens:** Source generators can reorder based on optimization passes. Custom converters control their own output order.

**How to avoid:** Use record positional constructors throughout (order is declaration order, which is deterministic). Avoid `[JsonExtensionData]`. If using JSON source generator, verify order with a round-trip test.

**Warning signs:** ContentHash differs across machines for the JSON format; golden file tests flap.

### Pitfall 5: TRON Format Has No C# Library

**What goes wrong:** TRON (Token Reduced Object Notation) is referenced in decisions as a "middle ground" format. Investigation found no official C# SDK — only a JavaScript npm package exists as of early 2026.

**Why it matters:** If `SerializationFormat.Tron` is in the enum, there is no implementation to back it.

**How to avoid:** Define the `SerializationFormat` enum with `Tron` as a member, but stub its implementation to `throw new NotSupportedException("TRON format not yet implemented")`. Document that TRON support depends on a C# library becoming available or a hand-rolled implementation. Do not block Phase 1 on this — the enum member is sufficient for the contract.

---

## Code Examples

### SymbolId with PreviousIds (recommended final shape)

```csharp
// Symbols.cs
public readonly record struct SymbolId(string Value);

// SymbolNode gains PreviousIds and Accessibility:
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,   // empty = no renames; never null
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span);
```

### SymbolGraphSnapshot with ContentHash and ProjectName

```csharp
public sealed record SymbolGraphSnapshot(
    string SchemaVersion,           // semver string, e.g. "1.0.0"
    string ProjectName,             // source project identifier
    string SourceFingerprint,       // existing field — keep
    string? ContentHash,            // hex string of XxHash64; set by persistence layer after serialization
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges);
```

### ISearchIndex with IAsyncEnumerable

```csharp
public interface ISearchIndex
{
    Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct);
    IAsyncEnumerable<SearchHit> SearchAsync(string query, CancellationToken ct);
    Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct);
}
```

### IKnowledgeQueryService with GetReferencesAsync

```csharp
public interface IKnowledgeQueryService
{
    IAsyncEnumerable<SearchHit> SearchAsync(string query, CancellationToken ct);
    Task<SymbolNode?> GetSymbolAsync(SymbolId id, CancellationToken ct);
    Task<GraphDiff> DiffAsync(SnapshotRef a, SnapshotRef b, CancellationToken ct);
    IAsyncEnumerable<SymbolEdge> GetReferencesAsync(SymbolId id, CancellationToken ct);
}
```

### Expanded SymbolKind enum

```csharp
public enum SymbolKind
{
    Namespace,
    Type,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Delegate,
    Indexer,
    Operator,
    Destructor,
    EnumMember,
    TypeParameter,
    Parameter
}
```

### Expanded DocComment record

```csharp
public sealed record DocComment(
    string? Summary,
    string? Remarks,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyDictionary<string, string> TypeParams,       // new
    string? Returns,
    IReadOnlyList<string> Examples,
    IReadOnlyList<(string Type, string Description)> Exceptions,  // new
    IReadOnlyList<string> SeeAlso);                              // new
```

### ContentHash computation (in serialization layer, not Core)

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash64
using System.IO.Hashing;

byte[] serialized = MessagePackSerializer.Serialize(snapshotWithoutHash);
ulong hashValue = XxHash64.HashToUInt64(serialized);
string contentHash = hashValue.ToString("x16");
SymbolGraphSnapshot final = snapshotWithoutHash with { ContentHash = contentHash };
```

### Golden-file test with Verify.Xunit (CORE-01 rename scenario)

```csharp
using Verify;

public class SymbolIdTests
{
    [Fact]
    public Task PreviousIds_tracks_rename()
    {
        var renamed = new SymbolId("T:MyNs.NewName");
        var original = new SymbolId("T:MyNs.OldName");
        var node = new SymbolNode(
            renamed, SymbolKind.Type, "NewName", "MyNs.NewName",
            PreviousIds: [original],
            Accessibility: Accessibility.Public,
            Docs: null, Span: null);

        return Verify(node); // produces SymbolIdTests.PreviousIds_tracks_rename.verified.txt
    }

    [Fact]
    public void SymbolId_value_equality_holds()
    {
        var a = new SymbolId("T:MyNs.MyType");
        var b = new SymbolId("T:MyNs.MyType");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `System.Linq.Async` NuGet for `IAsyncEnumerable` LINQ | `System.Linq.AsyncEnumerable` in-box | .NET 10 (Nov 2025) | Remove `System.Linq.Async` from deps for net10.0 targets; potential ambiguity if both present |
| `MessagePack` v2 (reflection-only) | `MessagePack` v3 (source generator + AOT) | 2024 | Source generator removes startup reflection cost; AOT/trimming compatible |
| `FluentAssertions` Apache 2.0 (any version) | v7.x = Apache 2.0; v8+ = Xceed commercial | Jan 2025 | Pin to `< 8.0.0` to stay free |
| `ApprovalTests` for golden-file tests | `Verify` / `Verify.Xunit` | ~2022, accelerated in 2024-2025 | Verify is actively maintained with better .NET 10 support |

**Deprecated/outdated:**
- `System.Linq.Async` NuGet: superseded by in-box `System.Linq.AsyncEnumerable` in .NET 10 — do not add this package for a net10.0-only target.
- `FluentAssertions` v8+: commercial license — avoid upgrading past v7.

---

## Open Questions

1. **TRON format C# implementation**
   - What we know: TRON is a JSON superset that reduces token count by defining schemas for repeated object shapes; a JS npm package (`@tron-format/tron`) exists; the official site (tron-format.github.io) has no C# SDK listed
   - What's unclear: Whether a C# implementation exists or whether the intent is to hand-roll a simple subset
   - Recommendation: Stub `SerializationFormat.Tron` in the enum with `throw new NotSupportedException`; revisit in Phase 2 or later. Do not block Phase 1 on TRON.

2. **InMemorySearchIndex signature break from IAsyncEnumerable change**
   - What we know: `DocAgent.Indexing/InMemorySearchIndex.cs` currently implements `ISearchIndex.SearchAsync` returning `Task<IReadOnlyList<SearchHit>>`. Changing the interface to `IAsyncEnumerable<SearchHit>` is a breaking change to the existing stub implementation.
   - What's unclear: Whether the plan should update `InMemorySearchIndex` as part of Phase 1, or leave it as a compile error for Phase 3 to resolve
   - Recommendation: Update `InMemorySearchIndex` during Phase 1 when the interface changes — it is a trivial `yield return` loop change and leaving a compile error in the solution violates the `dotnet test` passing success criterion.

3. **DocComment dictionary equality in tests**
   - What we know: `IReadOnlyDictionary` members on `DocComment` break record structural equality
   - What's unclear: Whether Phase 1 tests will need DocComment equality (they will for golden-file tests)
   - Recommendation: Use `Verify.Xunit` for golden-file tests (it handles complex object graphs including dictionaries); use `FluentAssertions` `.BeEquivalentTo()` for inline assertions. Do not switch to `ImmutableDictionary` unless tests demand it — that adds a dependency.

---

## Validation Architecture

*(nyquist_validation not set in config.json — section included because tests are a success criterion)*

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.7.1 |
| Config file | None — implicit from SDK |
| Quick run command | `dotnet test --filter "FullyQualifiedName~DocAgent.Tests"` |
| Full suite command | `dotnet test src/DocAgentFramework.sln` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CORE-01 | `SymbolId` value equality holds for equal values | unit | `dotnet test --filter "FullyQualifiedName~SymbolIdTests"` | No — create in Wave 0 |
| CORE-01 | `PreviousIds` persists rename history on `SymbolNode` | unit + golden-file | `dotnet test --filter "FullyQualifiedName~SymbolIdTests"` | No — create in Wave 0 |
| CORE-02 | `SymbolGraphSnapshot` roundtrips through MessagePack with identical bytes | unit | `dotnet test --filter "FullyQualifiedName~SnapshotSerializationTests"` | No — create in Wave 0 |
| CORE-02 | `ContentHash` is identical across two serializations of the same snapshot | unit | `dotnet test --filter "FullyQualifiedName~SnapshotSerializationTests"` | No — create in Wave 0 |
| CORE-03 | All six interfaces compile with `TreatWarningsAsErrors=true` | compile-time (build) | `dotnet build src/DocAgent.Core` | Partial — needs IVectorIndex added |

### Wave 0 Gaps

- [ ] `tests/DocAgent.Tests/SymbolIdTests.cs` — covers CORE-01 (value equality + PreviousIds golden-file)
- [ ] `tests/DocAgent.Tests/SnapshotSerializationTests.cs` — covers CORE-02 (roundtrip + ContentHash)
- [ ] `tests/DocAgent.Tests/Snapshots/` directory — Verify golden files storage
- [ ] Add `Verify.Xunit` to `Directory.Packages.props` and `DocAgent.Tests.csproj`
- [ ] Add `MessagePack` to `Directory.Packages.props` and `DocAgent.Core.csproj`

---

## Sources

### Primary (HIGH confidence)

- [XxHash64 Class — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash64?view=net-10.0-pp) — confirmed in-box for .NET 10; `HashToUInt64` API verified
- [System.IO.Hashing 10.0.3 — NuGet](https://www.nuget.org/packages/System.IO.Hashing/) — version confirmed
- [C# record structs — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/record-structs) — synthesized equality behavior confirmed via Context7
- [System.Linq.AsyncEnumerable breaking change — .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable) — ToListAsync confirmed in-box for .NET 10
- [AsyncEnumerable.ToListAsync — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.linq.asyncenumerable.tolistasync) — API confirmed
- [XML documentation comment specification — C# language spec](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments) — ID format confirmed (T:, M:, P: prefixes)
- [Verify.Xunit 31.12.5 — NuGet](https://www.nuget.org/packages/Verify.Xunit/) — current version confirmed

### Secondary (MEDIUM confidence)

- [MessagePack 3.1.4 — NuGet](https://www.nuget.org/packages/messagepack) — version confirmed; AOT source generator support verified via release notes
- [FluentAssertions v8 commercial license — InfoQ](https://www.infoq.com/news/2025/01/fluent-assertions-v8-license/) — license change confirmed via multiple sources (InfoQ, devclass, GitHub discussion)
- [System.Linq.Async in .NET 10 — steven-giesel.com](https://steven-giesel.com/blogPost/e40aaedc-9e56-491f-9fe5-3bb0b162ae94) — in-box LINQ for IAsyncEnumerable confirmed
- [ISymbol.GetDocumentationCommentId — Roslyn GitHub issue #16786](https://github.com/dotnet/roslyn/issues/16786) — method existence confirmed; assembly-qualified behavior described as "bare XML doc ID"

### Tertiary (LOW confidence)

- TRON format .NET support: no C# SDK found as of 2026-02-26. JS npm package confirmed. C# support: unconfirmed — flag as open question.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — in-box .NET 10 APIs confirmed via Microsoft Learn; MessagePack 3.1.4 confirmed on NuGet; FluentAssertions license change confirmed via multiple authoritative sources
- Architecture: HIGH — patterns follow directly from locked CONTEXT.md decisions and existing scaffold; record struct behavior confirmed via Context7
- Pitfalls: HIGH (except TRON) — dictionary equality pitfall is a known C# behavior; ContentHash circular dependency is a logical consequence; FluentAssertions version issue confirmed; TRON library gap confirmed via web search

**Research date:** 2026-02-26
**Valid until:** 2026-03-28 (stable stack; only MessagePack pre-release versions might shift)
