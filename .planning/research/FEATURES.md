# Feature Landscape

**Domain:** TypeScript symbol extraction and graph generation for existing MCP doc server
**Researched:** 2026-03-08
**Confidence:** HIGH (existing model inspected, TS Compiler API well-documented)

## Scope Note

This is a subsequent milestone (v2.0) research document. The 14 existing MCP tools and full C#/Roslyn pipeline are already shipped. Research focuses exclusively on what is needed to make TypeScript codebases queryable through the same tool surface via the existing `SymbolNode`/`SymbolEdge` graph model.

---

## SymbolNode Model Mapping Analysis

### Existing Model (from `DocAgent.Core/Symbols.cs`)

The current `SymbolNode` record carries: `SymbolId`, `SymbolKind` (14 values), `DisplayName`, `FullyQualifiedName`, `Accessibility`, `DocComment`, `SourceSpan`, `ReturnType`, `Parameters`, `GenericConstraints`, `ProjectOrigin`, `NodeKind`.

The `SymbolEdgeKind` enum has: `Contains`, `Inherits`, `Implements`, `Calls`, `References`, `Overrides`, `Returns`.

### Natural Mappings (TypeScript to Existing SymbolKind)

| TypeScript Concept | SymbolKind | Confidence | Notes |
|-------------------|------------|------------|-------|
| `class` declaration | `Type` | HIGH | Direct 1:1. TS classes have constructors, methods, properties like C#. |
| `interface` declaration | `Type` | HIGH | Same as C# interface. Distinguish via metadata or naming convention in `DisplayName`. |
| `enum` declaration | `Type` | HIGH | TS enums map directly. `const enum` is a compile-time variant but structurally identical. |
| Enum member | `EnumMember` | HIGH | Direct 1:1. |
| `function` declaration | `Method` | HIGH | Top-level functions are methods without a containing type. Use `Contains` edge from module. |
| Method (class member) | `Method` | HIGH | Direct 1:1. |
| Constructor | `Constructor` | HIGH | Direct 1:1. |
| Property (class member) | `Property` | HIGH | Direct 1:1. Includes `readonly`, optional (`?`). |
| Field (class member) | `Field` | HIGH | TS doesn't distinguish field vs property syntactically but `declare` fields exist. Map class fields to `Field`. |
| Parameter | `Parameter` | HIGH | Direct 1:1. Includes destructured params (flatten to named params). |
| Generic type parameter | `TypeParameter` | HIGH | `<T extends Foo>` maps to `GenericConstraint`. |
| Getter/Setter | `Property` | HIGH | TS accessors map to `Property` with implicit get/set. |
| Namespace (TS `namespace`) | `Namespace` | HIGH | Direct 1:1. |
| Module (file-level) | `Namespace` | MEDIUM | ES modules are file-scoped. Treat each file as an implicit namespace node. |
| `type` alias | `Delegate` | MEDIUM | Reuse `Delegate` kind (closest existing). See Gaps section for better option. |
| Index signature | `Indexer` | HIGH | `[key: string]: T` maps to `Indexer`. |
| Event (`on`/`emit` patterns) | `Event` | LOW | TS has no native event keyword. Only applicable for EventEmitter patterns. Likely skip. |

### Natural Edge Mappings

| TypeScript Relationship | SymbolEdgeKind | Notes |
|------------------------|----------------|-------|
| Class extends class | `Inherits` | Direct 1:1. |
| Class implements interface | `Implements` | Direct 1:1. |
| Interface extends interface | `Inherits` | Direct 1:1. |
| Module contains declaration | `Contains` | Direct 1:1. Parent-child containment. |
| Function returns type | `Returns` | Direct 1:1. |
| Symbol references another | `References` | Import references, type references. |
| Method overrides parent | `Overrides` | Direct 1:1. |
| Function calls function | `Calls` | Requires deeper analysis; can be extracted from checker. |

### Accessibility Mapping

| TypeScript | Accessibility Enum | Notes |
|-----------|-------------------|-------|
| `public` (default) | `Public` | TS class members default to public. |
| `private` | `Private` | Direct 1:1. |
| `protected` | `Protected` | Direct 1:1. |
| `#private` (ES private) | `Private` | ES private fields map to Private. Distinguish via naming if needed. |
| `export` (module-level) | `Public` | Exported = public API surface. |
| Not exported | `Internal` | Non-exported module members = internal. |
| `export default` | `Public` | Default exports are public. |

### DocComment Mapping (JSDoc to DocComment)

| JSDoc Tag | DocComment Field | Notes |
|-----------|-----------------|-------|
| `/** description */` | `Summary` | First paragraph before any tags. |
| `@remarks` | `Remarks` | Direct 1:1. |
| `@param name description` | `Params` dictionary | Direct 1:1. |
| `@typeParam T description` | `TypeParams` dictionary | Direct 1:1. TSDoc standard. |
| `@returns description` | `Returns` | Direct 1:1. |
| `@example` | `Examples` list | Direct 1:1. |
| `@throws` / `@exception` | `Exceptions` list | Direct 1:1. Type from `{ErrorType}` syntax. |
| `@see` | `SeeAlso` list | Direct 1:1. |
| `@deprecated` | No direct field | Store in `Remarks` or add to `Summary` with prefix. |
| `@since`, `@version` | No direct field | Store in `Remarks`. |
| `@internal` | Sets `Accessibility` to `Internal` | Semantic tag, not doc content. |

---

## Table Stakes

Features users expect. Missing = TypeScript support feels incomplete.

| Feature | Why Expected | Complexity | Dependencies on Existing Model |
|---------|--------------|------------|-------------------------------|
| **Class extraction** (name, members, heritage) | Classes are the primary OOP building block in TS. Every doc tool handles them. | LOW | Direct mapping to `SymbolKind.Type` + `Contains` edges. No model changes. |
| **Interface extraction** (name, members, extends) | Interfaces define API contracts. Widely used in TS codebases. | LOW | Direct mapping to `SymbolKind.Type` + `Inherits`/`Contains` edges. No model changes. |
| **Function extraction** (standalone + methods) | Functions are fundamental. Top-level functions and class methods must be captured. | LOW | `SymbolKind.Method`. Parameters map to `ParameterInfo`. `ReturnType` field exists. No model changes. |
| **Enum extraction** (regular + const enums) | Enums exist in TS and map directly. | LOW | `SymbolKind.Type` + `SymbolKind.EnumMember`. No model changes. |
| **Module/export structure** | ES module exports define the public API surface. Without export tracking, you cannot determine what is public. | MEDIUM | Use `Namespace` kind for modules. `Accessibility.Public` for exported, `Internal` for non-exported. `Contains` edges for parent-child. No model changes needed. |
| **Type alias extraction** | `type Foo = ...` is ubiquitous in TS. Must appear in symbol graph. | MEDIUM | Reuse `SymbolKind.Delegate` or add new `SymbolKind.TypeAlias`. Store RHS type string in `ReturnType` field. See Gaps section. |
| **Generic type parameters and constraints** | `<T extends Foo>` is common. Must be captured for API comprehension. | LOW | `GenericConstraint` record exists and maps directly. No model changes. |
| **Property and field extraction** | Class properties (including `readonly`, optional) are core members. | LOW | `SymbolKind.Property` / `SymbolKind.Field`. No model changes. |
| **Constructor extraction** | `constructor()` must appear as a member. | LOW | `SymbolKind.Constructor`. `ParameterInfo` for params. No model changes. |
| **Source spans** | File path + line numbers for each symbol. Required for "go to definition" workflows. | LOW | `SourceSpan` record exists and maps directly. No model changes. |
| **JSDoc extraction** (summary, params, returns) | TSDoc/JSDoc is the standard documentation format. Without it, `DocComment` is always null. | MEDIUM | `DocComment` record maps well. TS Compiler API provides `symbol.getDocumentationComment(checker)`. Some edge cases with multiple JSDoc blocks. |
| **Inheritance/implementation edges** | `extends` and `implements` relationships must be captured. | LOW | `SymbolEdgeKind.Inherits` and `Implements` exist. No model changes. |
| **tsconfig.json as project entry point** | Standard TS project definition. Equivalent to `.csproj` for C#. | LOW | `ProjectName` from tsconfig path. `SourceFingerprint` from file hashes. No model changes. |
| **Incremental ingestion** | SHA-256 file hashing already exists for C#. TS must use same pattern. | MEDIUM | Reuse existing `SourceFingerprint` / SHA-256 infrastructure. File change detection applies identically. |
| **Deterministic snapshot output** | Same TS source must produce identical `SymbolGraphSnapshot`. Core contract. | MEDIUM | Sort nodes/edges deterministically via existing `SymbolSorter`. Same challenge as C# pipeline, same solution. |

---

## Differentiators

Features that set the tool apart from basic TS doc generators.

| Feature | Value Proposition | Complexity | Dependencies on Existing Model |
|---------|-------------------|------------|-------------------------------|
| **Full JSDoc/TSDoc tag extraction** (all tags, not just summary) | Most tools extract only `@param`/`@returns`. Extracting `@example`, `@throws`, `@see`, `@deprecated`, `@remarks` gives agents richer context. | MEDIUM | `DocComment` already has fields for all of these. TS Compiler API `ts.getJSDocTags()` provides access. |
| **Declaration merging resolution** | TS allows interfaces and namespaces to merge across files. Reporting the merged symbol is valuable and non-trivial. | HIGH | Single `SymbolNode` with merged members. Multiple `SourceSpan` values needed (model only supports one). |
| **Ambient declaration support** (`.d.ts` files) | `.d.ts` files define types for JS libraries. Ingesting `.d.ts` gives agents visibility into the full type surface. | MEDIUM | Map to `NodeKind.Stub` (external reference nodes). Same pattern as NuGet stub nodes in C#. |
| **Re-export tracking** (`export { X } from './y'`) | TS modules re-export extensively. Tracking the re-export chain shows the true public API surface. | MEDIUM | Model as `References` edges. |
| **Overload signatures** | TS functions can have multiple overload signatures. Capturing all overloads gives agents the full API surface. | MEDIUM | Each overload becomes a separate `SymbolNode` with unique `SymbolId` per overload. |
| **Monorepo / multi-tsconfig support** | Real-world TS projects use workspace monorepos with multiple `tsconfig.json`. | MEDIUM | Each tsconfig = one `SymbolGraphSnapshot`. Discovery logic in sidecar. |
| **Cross-file reference edges** | Track which symbols reference which across files within a project. Enables `get_references` for TS. | MEDIUM | `SymbolEdgeKind.References` with `EdgeScope.IntraProject`. |

---

## Anti-Features

Features to explicitly NOT build in v2.0.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| **Union type decomposition** | Union types are type expressions, not declarations. No stable identity, no members. Would create thousands of synthetic nodes. | Store as string in `ReturnType` or `TypeName` (e.g., `"string \| number"`). |
| **Mapped type expansion** | Compile-time transformations. Expanded form depends on input type. | Store expression as string (e.g., `"Partial<User>"`). |
| **Conditional type evaluation** | Polymorphic -- resolved form changes per call site. | Store expression as string. |
| **Template literal type expansion** | Combinatorial explosion. | Store expression as string. |
| **Type inference / hover resolution** | Requires full type checker per-expression. Expensive, non-deterministic. | Out of scope. |
| **Cross-language edges (C# <-> TS)** | Requires semantic understanding of HTTP routes/RPCs. | Separate snapshots per language. |
| **Runtime JavaScript analysis** | Without type info, extraction is unreliable. | Support `.ts`/`.tsx` + `.d.ts` only. |
| **Decorator metadata extraction** | Evolving API (TC39 stage 3 vs legacy). Framework-specific. | Extract decorator names in `Remarks` or ignore. |
| **Language-specific query tools** | Doubles tool surface. Existing 14 tools work on any `SymbolGraphSnapshot`. | Use `project` filter on existing tools. |

---

## Feature Dependencies

```
[tsconfig.json discovery]
    |
    v
[TypeScript Compiler API program creation]
    |
    +---> [Symbol extraction: classes, interfaces, functions, enums]
    |         |
    |         +---> [Member extraction: methods, properties, fields, constructors]
    |         |
    |         +---> [Inheritance/implementation edge extraction]
    |         |
    |         +---> [Generic constraint extraction]
    |
    +---> [Module/export structure extraction]
    |         |
    |         +---> [Accessibility mapping (exported = public)]
    |
    +---> [JSDoc/TSDoc extraction]
    |
    +---> [Source span extraction]
    |
    +---> [SymbolId generation] (must be stable, deterministic)
    |
    v
[SymbolGraphSnapshot assembly]
    |
    +---> [Incremental ingestion] (reuse SHA-256 file hashing)
    |
    +---> [Deterministic serialization] (reuse MessagePack pipeline)
    |
    v
[All 14 existing MCP tools work against TS snapshots]
```

---

## Gaps Requiring Model Attention

### Gap 1: Type Aliases Have No SymbolKind

**Problem:** TypeScript `type Foo = string | number` is extremely common but has no natural `SymbolKind`. The closest existing kind is `Delegate`, which is semantically misleading.

**Recommendation:** Add `SymbolKind.TypeAlias = 14`. One-line enum addition. All existing C# symbols remain unaffected. The `kindFilter` parameter on `search_symbols` already accepts string values, so "TypeAlias" works immediately.

### Gap 2: Multiple Source Spans (Declaration Merging)

**Problem:** TS interfaces can be declared across multiple files. Current `SymbolNode.Span` is a single `SourceSpan?`.

**Recommendation:** Use primary declaration site for `Span` in v2.0. Declaration merging is a differentiator for later.

### Gap 3: `export` as Accessibility

**Recommendation:** Map `export` to `Accessibility.Public` and non-export to `Accessibility.Internal`.

### Gap 4: Function Overloads

**Recommendation:** One `SymbolNode` per overload with suffix in SymbolId (e.g., `proj:myFunc#0`, `proj:myFunc#1`). Defer to v2.1.

### Gap 5: No Modifier Flags

**Recommendation:** Defer `abstract`, `static`, `readonly` flags. Not essential for v2.0 symbol navigation.

---

## MVP Recommendation

### Build in v2.0

1. Symbol extraction for all declaration types (classes, interfaces, functions, enums, type aliases, namespaces, constructors, methods, properties, fields)
2. JSDoc extraction (summary, `@param`, `@returns`, `@example`, `@throws`, `@see`, `@remarks`)
3. Inheritance and implementation edges (`extends`, `implements`)
4. Module export structure (export = public, non-export = internal)
5. Source spans (file path + line range for every symbol)
6. Stable SymbolId generation
7. tsconfig.json as entry point
8. Incremental ingestion (reuse SHA-256 file hashing)
9. `ingest_typescript` MCP tool
10. Add `SymbolKind.TypeAlias` (one enum value)

### Defer to v2.1+

- Declaration merging (multi-span symbols)
- `.d.ts` / ambient declaration ingestion (stub nodes)
- Re-export chain tracking
- Monorepo multi-tsconfig discovery
- Function overload signatures
- Modifier flags (`abstract`, `static`, `readonly`)
- Decorator extraction

---

## Sources

- Existing codebase: `src/DocAgent.Core/Symbols.cs`, `src/DocAgent.Core/DiffTypes.cs` -- HIGH confidence (direct inspection)
- `.planning/PROJECT.md` -- HIGH confidence (project requirements and scope)
- [TypeScript Compiler API Wiki](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API) -- HIGH confidence
- [TSDoc Standard](https://tsdoc.org/) -- HIGH confidence
- [TypeScript Declaration Merging](https://www.typescriptlang.org/docs/handbook/declaration-merging.html) -- HIGH confidence
- [TypeDoc GitHub](https://github.com/TypeStrong/typedoc) -- MEDIUM confidence (reference for TS doc extraction)

---
*Feature research for: DocAgentFramework v2.0 TypeScript Language Support*
*Researched: 2026-03-08*
