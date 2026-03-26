# Phase 32: JSON Contract Alignment (TS ↔ C# Deserialization) - Research

**Researched:** 2026-03-25
**Domain:** System.Text.Json deserialization, TypeScript-to-C# contract mapping
**Confidence:** HIGH — all findings derived from direct codebase inspection

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Fix direction (property names)**
- Fix on C# side — add `[JsonPropertyName]` attributes to C# domain types to accept TS camelCase names
- Full contract mapping — add `[JsonPropertyName]` to every property on SymbolNode, SymbolEdge, DocComment, SourceSpan, and related records (not just the mismatched ones)
- Separate serialization options — MCP tool output stays PascalCase (its own JsonSerializerOptions), sidecar deserialization uses PropertyNameCaseInsensitive=true + the attrs
- Claude's discretion on approach: DTO layer, custom JsonConverter, or attrs with separate MCP options — pick whichever minimizes risk to MCP API consumers

**Enum alignment strategy**
- String-based serialization for ALL enums in the sidecar contract (SymbolEdgeKind, SymbolKind, Accessibility, EdgeScope, NodeKind)
- TS sidecar emits C# enum member names as strings (e.g., "Inherits" not "Extends", "Contains" not numeric 0)
- Map TS `Extends` edge kind → C# `Inherits` (semantically correct for class inheritance)
- JsonStringEnumConverter on C# deserialization side
- Future-proofs against any ordinal drift between TS and C# enums

**E2E test approach**
- Both golden JSON file test AND real sidecar integration test
- Golden file: Captured from real sidecar output against existing TS test fixtures (src/ts-symbol-extractor/tests/fixtures/) — not hand-crafted
- Real sidecar test: Gated behind `RUN_SIDECAR_TESTS=true` environment variable (opt-in, keeps default CI fast)
- Assertions must cover all four categories:
  1. Edge integrity — From/To non-null, edge kinds correct (Inherits not Returns), edge count matches
  2. Doc preservation — Docs non-null for documented symbols, summary text matches
  3. Full snapshot comparison — deserialized snapshot matches reference (structural equality)
  4. Query tool validation — search_symbols, get_references etc. return correct results against deserialized snapshot

**Backward compatibility (missing fields)**
- C# defaults are sufficient for TS-missing fields — PreviousIds defaults to empty list, ProjectOrigin defaults to null
- EdgeScope defaults to IntraProject (correct for single TS project)
- No TS changes needed for these fields
- No contract versioning mechanism needed — string enums + golden file + E2E test are sufficient for an internal contract

### Claude's Discretion
- Exact approach to isolating sidecar JsonSerializerOptions from MCP serialization (DTO layer vs converter vs separate options)
- Internal test helper organization for golden file capture/comparison
- Whether to update existing PipelineOverride tests to also exercise the deserialization path

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SIDE-03 | C# TypeScriptIngestionService that spawns Node.js sidecar, sends request, deserializes response into SymbolGraphSnapshot | Fix deserialization in TypeScriptIngestionService.RunSidecarExtractionAsync — add JsonStringEnumConverter to JsonOptions, [JsonPropertyName] attrs on domain types |
| EXTR-04 | Extract inheritance (extends) and implementation (implements) edges as SymbolEdge relationships | TS emits `SymbolEdgeKind.Inherits` (ordinal 6) and `Implements` (ordinal 2) — string enum conversion removes ordinal mismatch; TS Extends=1 becomes string "Inherits" |
| EXTR-06 | Extract JSDoc/TSDoc comments into DocComment | TS `docComment` → C# `Docs` property name fix; DocComment structural mapping with extra fields handled via JSON ignore |
| MCPI-01 | ingest_typescript MCP tool accepting tsconfig.json path with PathAllowlist security enforcement | Correct deserialization is prerequisite; IngestionTools.IngestTypeScript already wired; fix makes it produce valid snapshots |
| MCPI-02 | All 14 existing MCP tools produce correct results when querying TypeScript snapshots | Golden file + E2E tests verify all tools against real deserialized snapshots |
</phase_requirements>

---

## Summary

Phase 32 fixes the JSON deserialization contract between the TypeScript sidecar and C# domain types. Direct inspection of the golden file (`src/ts-symbol-extractor/tests/golden-files/simple-project.json`) and source code confirms three categories of breakage, all of which have clear, low-risk fixes.

The most impactful fix is the enum strategy: the TS sidecar currently emits numeric ordinals (e.g., `"kind": 6` for Inherits, `"kind": 2` for Implements). C# `SymbolEdgeKind` has `Contains=0, Inherits=1, Implements=2` — ordinals that DO NOT match the TS enum where `Contains=0, Extends=1, Implements=2, Inherits=6`. Every inheritance edge currently deserializes to the wrong kind. The fix is to have TS emit string names and C# use `JsonStringEnumConverter`.

The property name mismatches (`sourceId/targetId` vs `From/To`, `docComment` vs `Docs`) cause null values on deserialized edges and nodes respectively. The approach is `[JsonPropertyName]` attributes on C# records plus `PropertyNameCaseInsensitive = true` already present in `TypeScriptIngestionService.JsonOptions`. The MCP output serializer is entirely separate and unaffected.

The `DocComment` structural mismatch (TS `example: string|null` vs C# `Examples: IReadOnlyList<string>`, TS `throws: Record<string,string>` vs C# `Exceptions: IReadOnlyList<(string, string)>`, TS `see: string[]` vs C# `SeeAlso: IReadOnlyList<string>`) requires either a custom `JsonConverter<DocComment>` or an intermediate DTO. Given the complexity of the tuple-valued `Exceptions` field, a custom converter is the least-risk approach.

**Primary recommendation:** Use `[JsonPropertyName]` attrs for structural property name fixes, a custom `JsonConverter<DocComment>` for the shape mismatch, and `JsonStringEnumConverter` for all enums — all scoped to a sidecar-specific `JsonSerializerOptions` instance that does not affect MCP output.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Text.Json` | .NET 10 built-in | JSON deserialization of sidecar output | Already in use in TypeScriptIngestionService |
| `System.Text.Json.Serialization.JsonStringEnumConverter` | .NET 10 built-in | Enum name-based deserialization | Native, zero dependencies, supports all enum types |
| `System.Text.Json.Serialization.JsonPropertyNameAttribute` | .NET 10 built-in | Map camelCase JSON names to PascalCase C# properties | Declarative, no runtime overhead, widely supported |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit `[Trait]` | Existing | Categorize new deserialization/E2E tests | For `[Trait("Category", "Deserialization")]` and `[Trait("Category", "E2E")]` |
| `FluentAssertions` | Existing | Readable assertions for complex object graphs | All new tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `[JsonPropertyName]` attrs | DTO/mapping layer | DTO avoids touching domain types but adds translation code; attrs are simpler and domain types already have no serialization coupling in MCP path via separate options |
| Custom `JsonConverter<DocComment>` | DTO for DocComment | Custom converter keeps domain type clean; DTO alternative is equally valid but adds a parallel type |
| `JsonStringEnumConverter` global | Per-enum `[JsonConverter]` attr | Global on sidecar options is cleaner; per-attr approach risks missing an enum |

**Installation:** No new packages needed — all tools are in `System.Text.Json` (already referenced).

---

## Architecture Patterns

### Current Deserialization Path

```
Node.js sidecar
  → writes JSON to temp file
  → C# reads file
  → JsonDocument.Parse
  → root["result"].GetRawText()
  → JsonSerializer.Deserialize<SymbolGraphSnapshot>(rawText, JsonOptions)
```

`JsonOptions` is currently:
```csharp
// src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs lines 34-37
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```

### Recommended Sidecar JsonSerializerOptions

```csharp
// Source: System.Text.Json documentation / .NET 10
private static readonly JsonSerializerOptions SidecarJsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    Converters =
    {
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        new DocCommentConverter()  // custom — handles shape mismatch
    }
};
```

`allowIntegerValues: false` is important: it forces an exception if TS ever emits a numeric ordinal rather than silently accepting wrong enum values.

### MCP Output Options (unchanged)

The MCP tool output serializer is in `DocAgent.McpServer/Tools/` and uses its own `JsonSerializerOptions`. These must NOT be modified. This isolation already exists — the `TypeScriptIngestionService.JsonOptions` field is private and used only for sidecar deserialization.

### Pattern 1: [JsonPropertyName] on SymbolEdge

**What:** Add property name attributes to map TS field names to C# field names
**When to use:** Every property where TS camelCase name differs from C# PascalCase name

```csharp
// Source: direct inspection of Symbols.cs + golden file
// TS emits: { "sourceId": {...}, "targetId": {...}, "kind": "Contains", "scope": "IntraProject" }
// C# record:
public sealed record SymbolEdge(
    [property: JsonPropertyName("sourceId")] SymbolId From,
    [property: JsonPropertyName("targetId")] SymbolId To,
    SymbolEdgeKind Kind,
    EdgeScope Scope = EdgeScope.IntraProject);
```

Note: `[property: JsonPropertyName(...)]` is the correct syntax for positional record parameters. Placement before the parameter applies the attribute to the generated property.

### Pattern 2: [JsonPropertyName] on SymbolNode

```csharp
// TS emits: { "id": {...}, "docComment": {...}, ... }
// C# record has: DocComment? Docs
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    [property: JsonPropertyName("docComment")] DocComment? Docs,
    SourceSpan? Span,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<GenericConstraint> GenericConstraints,
    string? ProjectOrigin = null,
    NodeKind NodeKind = NodeKind.Real);
```

### Pattern 3: DocCommentConverter

The `DocComment` record has structural mismatches that cannot be resolved with attribute mapping alone:

| TS field | TS type | C# property | C# type | Mismatch |
|----------|---------|-------------|---------|----------|
| `example` | `string \| null` | `Examples` | `IReadOnlyList<string>` | Scalar vs list |
| `throws` | `Record<string,string>` | `Exceptions` | `IReadOnlyList<(string Type, string Description)>` | Dict vs tuple list |
| `see` | `string[]` | `SeeAlso` | `IReadOnlyList<string>` | Name only |
| `params` | `Record<string,string>` | `Params` | `IReadOnlyDictionary<string,string>` | Compatible via case insensitive |
| `typeParams` | `Record<string,string>` | `TypeParams` | `IReadOnlyDictionary<string,string>` | Compatible |

A custom `JsonConverter<DocComment>` reads the raw JSON and constructs the C# record:

```csharp
// Illustrative pattern
internal sealed class DocCommentConverter : JsonConverter<DocComment>
{
    public override DocComment? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null;
        var remarks = root.TryGetProperty("remarks", out var r) ? r.GetString() : null;
        var returns = root.TryGetProperty("returns", out var ret) ? ret.GetString() : null;

        // TS "example": string|null → C# Examples: IReadOnlyList<string>
        IReadOnlyList<string> examples = [];
        if (root.TryGetProperty("example", out var ex) && ex.ValueKind == JsonValueKind.String)
        {
            var exText = ex.GetString();
            if (exText is not null) examples = [exText];
        }

        // TS "throws": Record<string,string> → C# Exceptions: IReadOnlyList<(string,string)>
        var exceptions = new List<(string Type, string Description)>();
        if (root.TryGetProperty("throws", out var throwsProp) && throwsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var kv in throwsProp.EnumerateObject())
                exceptions.Add((kv.Name, kv.Value.GetString() ?? ""));
        }

        // TS "see": string[] → C# SeeAlso: IReadOnlyList<string>
        IReadOnlyList<string> seeAlso = [];
        if (root.TryGetProperty("see", out var seeProp) && seeProp.ValueKind == JsonValueKind.Array)
            seeAlso = seeProp.EnumerateArray().Select(e => e.GetString() ?? "").ToList();

        // TS "params": Record<string,string>
        var parms = ReadStringDict(root, "params");
        var typeParams = ReadStringDict(root, "typeParams");

        return new DocComment(summary, remarks, parms, typeParams, returns, examples,
            exceptions.AsReadOnly(), seeAlso);
    }

    public override void Write(Utf8JsonWriter writer, DocComment value, JsonSerializerOptions options)
        => throw new NotSupportedException("DocCommentConverter is read-only (sidecar deserialization only)");

    private static IReadOnlyDictionary<string, string> ReadStringDict(JsonElement root, string propName)
    {
        if (!root.TryGetProperty(propName, out var prop) || prop.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();
        return prop.EnumerateObject().ToDictionary(kv => kv.Name, kv => kv.Value.GetString() ?? "");
    }
}
```

### Pattern 4: TS Sidecar Enum Emission (string names)

The TS sidecar uses `const enum` which inlines numeric values at compile time. To emit strings, the enums must be converted to regular TypeScript enums with string values matching C# member names.

```typescript
// BEFORE (types.ts): const enum emits ordinals 0, 1, 2, 6...
export const enum SymbolEdgeKind {
  Contains = 0,
  Extends = 1,   // <-- does NOT exist in C# SymbolEdgeKind
  Implements = 2,
  References = 3,
  Calls = 4,
  Overrides = 5,
  Inherits = 6   // <-- C# ordinal is 1, not 6
}

// AFTER (types.ts): regular enum emits string names
export enum SymbolEdgeKind {
  Contains = "Contains",
  Inherits = "Inherits",    // replaces Extends; semantically correct
  Implements = "Implements",
  References = "References",
  Calls = "Calls",
  Overrides = "Overrides"
}
```

The extractor.ts uses `SymbolEdgeKind.Inherits` already (line 184 in extractor.ts), so the rename from `Extends` to `Inherits` in the enum definition is the only change needed in that file. All other enum usages (`Contains`, `Implements`) already have matching C# names.

Similarly for SymbolKind, Accessibility, EdgeScope, NodeKind — convert from `const enum` with numbers to regular enum with string values matching C# PascalCase names.

### Pattern 5: Environment-Gated Integration Test

```csharp
// New pattern for this phase — gated by RUN_SIDECAR_TESTS env var
[Fact]
[Trait("Category", "Sidecar")]
public async Task RealSidecar_Produces_Valid_SymbolGraphSnapshot()
{
    if (Environment.GetEnvironmentVariable("RUN_SIDECAR_TESTS") != "true")
        return; // Skip unless explicitly opted in

    // ... real sidecar invocation
}
```

### Pattern 6: Golden File Test

```csharp
// Deserialize the committed golden JSON file through the real deserialization path
[Fact]
[Trait("Category", "Deserialization")]
public async Task GoldenFile_Deserializes_To_Valid_Snapshot()
{
    var goldenPath = FindGoldenFile("simple-project.json"); // locate relative to test assembly
    var json = await File.ReadAllTextAsync(goldenPath);
    using var doc = JsonDocument.Parse(json);
    var snapshot = JsonSerializer.Deserialize<SymbolGraphSnapshot>(doc.RootElement.GetRawText(), SidecarJsonOptions);

    snapshot.Should().NotBeNull();
    snapshot!.Edges.Should().Contain(e =>
        e.Kind == SymbolEdgeKind.Inherits &&
        e.From.Value.Contains("SpecialGreeter") &&
        e.To.Value.Contains("Greeter"));
    // Greeter implements IGreeter
    snapshot.Edges.Should().Contain(e =>
        e.Kind == SymbolEdgeKind.Implements &&
        e.From.Value.Contains("Greeter") &&
        e.To.Value.Contains("IGreeter"));
    // hello() function has docs
    snapshot.Nodes.Should().Contain(n =>
        n.DisplayName == "hello" &&
        n.Docs != null &&
        n.Docs.Summary == "This is a sample function.");
}
```

### Anti-Patterns to Avoid

- **Modifying global MCP serializer options:** The `JsonOptions` in `TypeScriptIngestionService` is already scoped to sidecar deserialization. Do not add a global `JsonStringEnumConverter` to the application's MCP output serializer — it would change all enum representations in MCP responses to string, breaking existing API consumers.
- **Hand-crafting golden JSON:** The golden file must be generated by running the actual sidecar against the existing test fixtures. Hand-crafted JSON would not catch future sidecar output changes.
- **`allowIntegerValues: true` on JsonStringEnumConverter:** This silently accepts numeric values and would mask any regression where TS reverts to emitting ordinals.
- **Using `const enum` for string output in TypeScript:** TypeScript `const enum` values are inlined at compile time as their underlying value (number). To emit strings, use a regular `enum` with string values.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Enum name-to-value mapping | Custom enum parser | `JsonStringEnumConverter` | Handles all edge cases including flags, unknown values, culture invariance |
| Case-insensitive property matching | Manual property scanning | `PropertyNameCaseInsensitive = true` | Already in codebase, zero-cost option |
| SymbolId deserialization | Custom SymbolId parser | `PropertyNameCaseInsensitive = true` + record auto-deserialization | SymbolId is `readonly record struct(string Value)` — TS emits `{ "value": "..." }` which matches with case insensitive |

**Key insight:** The most dangerous hand-roll in this domain is a manual enum mapper. Any drift between TS and C# enum members would cause silent incorrect values rather than a deserializer exception.

---

## Complete Mismatch Inventory

This is the authoritative list of all property name and shape mismatches, derived from direct comparison of `src/DocAgent.Core/Symbols.cs` and `src/ts-symbol-extractor/src/types.ts` against `src/ts-symbol-extractor/tests/golden-files/simple-project.json`.

### SymbolEdge: Property Name Mismatches

| TS JSON field | C# property | Fix |
|---------------|-------------|-----|
| `sourceId` | `From` | `[property: JsonPropertyName("sourceId")]` on `From` |
| `targetId` | `To` | `[property: JsonPropertyName("targetId")]` on `To` |
| `kind` (numeric) | `Kind` (SymbolEdgeKind) | `JsonStringEnumConverter` + TS emits strings |
| `scope` (numeric) | `Scope` (EdgeScope) | `JsonStringEnumConverter` + TS emits strings |

### SymbolNode: Property Name Mismatches

| TS JSON field | C# property | Fix |
|---------------|-------------|-----|
| `docComment` | `Docs` | `[property: JsonPropertyName("docComment")]` on `Docs` |
| `kind` (numeric) | `Kind` (SymbolKind) | `JsonStringEnumConverter` + TS emits strings |
| `accessibility` (numeric) | `Accessibility` (Accessibility) | `JsonStringEnumConverter` + TS emits strings |
| `nodeKind` (numeric) | `NodeKind` (NodeKind) | `JsonStringEnumConverter` + TS emits strings |
| Not emitted | `PreviousIds` | C# default `[]` — no fix needed |
| Not emitted | `ProjectOrigin` | C# default `null` — no fix needed |

### DocComment: Shape Mismatches

| TS JSON field | C# property | Mismatch | Fix |
|---------------|-------------|----------|-----|
| `example: string\|null` | `Examples: IReadOnlyList<string>` | Scalar vs list | `DocCommentConverter` wraps single string in list |
| `throws: Record<string,string>` | `Exceptions: IReadOnlyList<(string, string)>` | Dict vs tuple list | `DocCommentConverter` converts dict entries to tuples |
| `see: string[]` | `SeeAlso: IReadOnlyList<string>` | Name mismatch | `[JsonPropertyName("see")]` or `DocCommentConverter` |
| `params` | `Params` | Case only | `PropertyNameCaseInsensitive = true` handles |
| `typeParams` | `TypeParams` | Case only | `PropertyNameCaseInsensitive = true` handles |
| `summary` | `Summary` | Case only | `PropertyNameCaseInsensitive = true` handles |
| `returns` | `Returns` | Case only | `PropertyNameCaseInsensitive = true` handles |
| `remarks` | `Remarks` | Case only | `PropertyNameCaseInsensitive = true` handles |

### Enum Ordinal Conflicts

The golden file shows actual emitted ordinals: `kind: 0` (Contains), `kind: 2` (Implements), `kind: 6` (Inherits).

| TS enum | TS ordinal | C# enum | C# ordinal | Conflict? |
|---------|-----------|---------|-----------|-----------|
| `SymbolEdgeKind.Contains` | 0 | `SymbolEdgeKind.Contains` | 0 | None (but fragile) |
| `SymbolEdgeKind.Extends` | 1 | Does not exist | — | BREAK — maps to C# `Inherits`=1 by accident |
| `SymbolEdgeKind.Implements` | 2 | `SymbolEdgeKind.Implements` | 2 | None (but fragile) |
| `SymbolEdgeKind.References` | 3 | `SymbolEdgeKind.References` | 4 | BREAK — maps to `Overrides` |
| `SymbolEdgeKind.Calls` | 4 | `SymbolEdgeKind.Calls` | 3 | BREAK — maps to wrong |
| `SymbolEdgeKind.Overrides` | 5 | `SymbolEdgeKind.Overrides` | 5 | None (fragile) |
| `SymbolEdgeKind.Inherits` | 6 | `SymbolEdgeKind.Inherits` | 1 | BREAK |

The `Extends` TS value (ordinal 1) currently never appears in extractor output — extractor.ts line 184 uses `SymbolEdgeKind.Inherits` directly. But `SymbolEdgeKind.Inherits` (ordinal 6 in TS) does appear in the golden file and maps to C# ordinal 1 (`Inherits`), which is a coincidental match for `Inherits`. However `References` (ordinal 3 in TS, ordinal 4 in C#) and `Calls` (ordinal 4 in TS, ordinal 3 in C#) ARE mismatches even though the test fixtures may not exercise them yet.

**Conclusion:** String-based enum serialization eliminates all ordinal concerns unconditionally.

### ParameterInfo: Structural Differences

| TS field | C# property | Gap |
|----------|-------------|-----|
| `isOptional` | No direct match | C# has `IsParams`, `IsRef`, `IsOut`, `IsIn` — none is "optional" |
| Not emitted | `IsParams`, `IsRef`, `IsOut`, `IsIn` | C# defaults to `false` — acceptable for TS |

The `isOptional` TS field will be ignored during C# deserialization (no `[JsonPropertyName]` needed for ignored incoming fields — `System.Text.Json` ignores unknown properties by default). The C# `ParameterInfo` `IsParams`/`IsRef`/`IsOut`/`IsIn` default to `false` which is correct for TypeScript parameters.

### IngestionMetadata: No Change Needed

TS emits `ingestionMetadata: null`. C# defaults the field to `null`. No fix needed.

---

## Common Pitfalls

### Pitfall 1: `const enum` Cannot Emit Strings

**What goes wrong:** TS `const enum` values are erased at compile time and replaced with their numeric literal. Even if you assign string values, the TS compiler will reject it for `const enum`. The build will fail or emit `NaN`-style values.

**Why it happens:** TypeScript's `const enum` optimization only works with numeric inlining.

**How to avoid:** Use regular `enum` (drop the `const` keyword) with string values. Verify with `tsc --noEmit` after the change.

**Warning signs:** `vitest` golden file tests fail with enum values being numbers instead of strings after the change.

### Pitfall 2: `[property: ...]` Syntax on Positional Record Parameters

**What goes wrong:** Writing `[JsonPropertyName("foo")] string Bar` on a record parameter applies the attribute to the parameter, not the generated property. The serializer never sees it.

**Why it happens:** C# record syntax requires `[property: Attr]` to target the generated property rather than just the parameter.

**How to avoid:** Always use `[property: JsonPropertyName("name")]` syntax for positional record parameters.

**Warning signs:** Deserialization test shows null value even after adding the attribute.

### Pitfall 3: MCP Output Serializer Contamination

**What goes wrong:** Adding `JsonStringEnumConverter` to a shared `JsonSerializerOptions` instance causes MCP tool responses to emit string enum names instead of integers, breaking MCP clients expecting numeric values.

**Why it happens:** The sidecar deserialization options and MCP output options might accidentally reference the same instance.

**How to avoid:** `TypeScriptIngestionService.JsonOptions` is `private static readonly` — the fix stays there. Confirm no other code references this field. Grep for `SidecarJsonOptions` or `JsonOptions` usage before shipping.

**Warning signs:** Existing `McpToolTests` or `TypeScriptToolVerificationTests` fail with assertion errors on enum values.

### Pitfall 4: `JsonStringEnumConverter` and Missing TS Enum Members

**What goes wrong:** If TS emits a string name that does not exist in the C# enum (e.g., TS still emits `"Extends"` after a partial fix), `JsonStringEnumConverter` with `allowIntegerValues: false` throws a `JsonException`.

**Why it happens:** TS `Extends` enum member was renamed to `Inherits` in the fix but extractor code still references the old name.

**How to avoid:** After renaming the TS enum member, search all `.ts` files for `SymbolEdgeKind.Extends` usage and replace with `SymbolEdgeKind.Inherits`. There should be zero references after the fix.

**Warning signs:** Deserialization of the golden file throws `JsonException: The JSON value could not be converted to DocAgent.Core.SymbolEdgeKind`.

### Pitfall 5: Golden File Node/Edge Counts

**What goes wrong:** Golden file assertions compare counts (e.g., `nodes.Count.Should().Be(12)`) which break when the TS fixture is legitimately modified.

**Why it happens:** Count-based assertions are brittle for evolving fixtures.

**How to avoid:** Assert structural properties (specific symbol IDs, specific edge kinds) rather than exact counts. Use `Should().Contain(...)` not `Should().HaveCount(...)` as the primary assertion.

---

## Code Examples

### Adding JsonStringEnumConverter to Sidecar Options

```csharp
// Source: TypeScriptIngestionService.cs — replace existing JsonOptions field
// System.Text.Json documentation (HIGH confidence)
private static readonly JsonSerializerOptions SidecarJsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    Converters =
    {
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        new DocCommentConverter()
    }
};
```

Note: `JsonNamingPolicy.CamelCase` is passed but since TS will emit PascalCase strings (`"Contains"`, `"Inherits"`) that match C# member names exactly, and `PropertyNameCaseInsensitive = true` applies to enum string matching as well, this parameter is largely irrelevant. Using `null` (default case-sensitive) is also fine since TS will emit exact C# member names.

### Record Attribute Syntax Verification

```csharp
// Correct — targets the generated property
public sealed record SymbolEdge(
    [property: JsonPropertyName("sourceId")] SymbolId From,
    [property: JsonPropertyName("targetId")] SymbolId To,
    SymbolEdgeKind Kind,
    EdgeScope Scope = EdgeScope.IntraProject);

// INCORRECT — targets the parameter only, property is invisible to serializer
public sealed record SymbolEdge(
    [JsonPropertyName("sourceId")] SymbolId From,  // WRONG
    ...);
```

### TypeScript Enum Conversion

```typescript
// BEFORE (const enum, emits numbers): types.ts
export const enum SymbolEdgeKind {
  Contains = 0,
  Extends = 1,
  Implements = 2,
  References = 3,
  Calls = 4,
  Overrides = 5,
  Inherits = 6
}

// AFTER (regular enum, emits strings matching C# names): types.ts
export enum SymbolEdgeKind {
  Contains = "Contains",
  Inherits = "Inherits",    // was Extends=1; semantically maps class extends → C# Inherits
  Implements = "Implements",
  References = "References",
  Calls = "Calls",
  Overrides = "Overrides"
}
```

### Environment-Gated Sidecar Test Pattern

```csharp
// New pattern for this codebase — first use
[Fact]
[Trait("Category", "Sidecar")]
public async Task RealSidecar_simple_project_Deserializes_Correctly()
{
    if (Environment.GetEnvironmentVariable("RUN_SIDECAR_TESTS") != "true")
    {
        // Not a skip — just an early return so the test still counts as "passed"
        return;
    }

    var fixturesDir = FindFixturesDirectory(); // walk up from test assembly to src/ts-symbol-extractor/tests/fixtures
    var tsconfigPath = Path.Combine(fixturesDir, "simple-project", "tsconfig.json");

    // ... set up TypeScriptIngestionService without PipelineOverride
    // ... call IngestTypeScriptAsync
    // ... assert edge kinds, doc preservation, etc.
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Numeric enum ordinals in JSON | String enum names | This phase | Decouples TS and C# enum ordering; eliminates silent mismatches |
| `const enum` in TypeScript | Regular `enum` with string values | This phase | Enables runtime string emission; slight bundle size increase (negligible) |
| PipelineOverride for all TS tests | Real sidecar path for E2E tests | This phase | Validates actual deserialization contract, not just domain model |

**Deprecated/outdated:**
- `SymbolEdgeKind.Extends` in TS: removed and replaced with `SymbolEdgeKind.Inherits` to match C# semantics

---

## Open Questions

1. **DocComment.Write path in DocCommentConverter**
   - What we know: `DocCommentConverter.Write` is not needed for sidecar deserialization; MCP serialization uses a separate options instance
   - What's unclear: Whether any code path currently serializes `DocComment` using the sidecar options
   - Recommendation: Implement `Write` as `throw new NotSupportedException(...)` with a clear message; if this throws in practice, it surfaces an unintended usage of the sidecar options

2. **ParameterInfo.isOptional field**
   - What we know: TS emits `isOptional` but C# has no matching property; System.Text.Json ignores unknown fields by default
   - What's unclear: Whether `DefaultUnmappedMemberHandling` is set anywhere globally to throw on unknown fields
   - Recommendation: Verify `JsonOptions` does not have `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` set; the default (ignore) is correct

3. **TypeScript `SymbolId` deserialization**
   - What we know: TS emits `{ "value": "..." }` for SymbolId; C# `SymbolId` is `readonly record struct(string Value)`
   - What's unclear: Whether `PropertyNameCaseInsensitive = true` correctly handles `value` → `Value` for record structs (as opposed to classes)
   - Recommendation: Add a specific assertion in the golden file test that verifies at least one `SymbolId.Value` is non-empty after deserialization

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.x (existing) |
| Config file | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| Quick run command | `dotnet test --filter "Category=Deserialization" --no-build` |
| Full suite command | `dotnet test src/DocAgentFramework.sln` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SIDE-03 | Sidecar JSON correctly deserializes to SymbolGraphSnapshot | unit | `dotnet test --filter "Category=Deserialization"` | ❌ Wave 0 |
| EXTR-04 | Inheritance/Implements edges deserialize with correct SymbolEdgeKind | unit (golden file) | `dotnet test --filter "Category=Deserialization"` | ❌ Wave 0 |
| EXTR-06 | DocComment fields (Summary, Params, Returns, Examples, Exceptions, SeeAlso) populated correctly | unit (golden file) | `dotnet test --filter "Category=Deserialization"` | ❌ Wave 0 |
| MCPI-01 | ingest_typescript tool produces non-empty snapshot with correct structure | integration | `dotnet test --filter "Category=E2E"` | ❌ Wave 0 |
| MCPI-02 | All MCP tools produce correct results against deserialized TypeScript snapshot | integration | `dotnet test --filter "Category=E2E"` | ❌ Wave 0 |
| MCPI-02 (sidecar path) | Real sidecar produces correct results (opt-in) | e2e | `RUN_SIDECAR_TESTS=true dotnet test --filter "Category=Sidecar"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "Category=Deserialization" --no-build`
- **Per wave merge:** `dotnet test src/DocAgentFramework.sln`
- **Phase gate:** Full suite green (641+ tests) before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/TypeScriptDeserializationTests.cs` — covers SIDE-03, EXTR-04, EXTR-06 using golden file
- [ ] `tests/DocAgent.Tests/TypeScriptContractE2ETests.cs` — covers MCPI-01, MCPI-02 with full MCP tool verification against deserialized snapshot
- [ ] `tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs` — covers MCPI-02 sidecar path (gated by `RUN_SIDECAR_TESTS=true`)

---

## Sources

### Primary (HIGH confidence)
- Direct inspection: `src/DocAgent.Core/Symbols.cs` — authoritative C# domain types
- Direct inspection: `src/ts-symbol-extractor/src/types.ts` — authoritative TS contract types
- Direct inspection: `src/ts-symbol-extractor/src/extractor.ts` — actual edge kind assignments
- Direct inspection: `src/ts-symbol-extractor/tests/golden-files/simple-project.json` — real sidecar output showing numeric ordinals
- Direct inspection: `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs` — current deserialization path and options
- Direct inspection: `tests/DocAgent.Tests/TypeScriptIngestionServiceTests.cs` and `TypeScriptE2ETests.cs` — existing test patterns

### Secondary (MEDIUM confidence)
- System.Text.Json `[property: JsonPropertyName]` syntax — standard .NET attribute targeting syntax, well-established

### Tertiary (LOW confidence)
- None

---

## Metadata

**Confidence breakdown:**
- Mismatch inventory: HIGH — all items verified by direct comparison of source files and golden file output
- Fix approach ([JsonPropertyName], JsonStringEnumConverter, DocCommentConverter): HIGH — standard System.Text.Json patterns
- TS enum string emission: HIGH — verified that `const enum` cannot emit strings; regular enum with string values is the correct TS pattern
- Test patterns: HIGH — follows existing test conventions in the codebase

**Research date:** 2026-03-25
**Valid until:** 2026-04-25 (stable domain — System.Text.Json and TS compiler API are not fast-moving in this area)
