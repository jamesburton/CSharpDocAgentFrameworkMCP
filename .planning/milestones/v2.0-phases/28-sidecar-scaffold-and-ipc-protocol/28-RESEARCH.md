# Phase 28: Sidecar Scaffold and IPC Protocol - Research

**Researched:** 2026-03-08
**Domain:** Node.js sidecar process management, NDJSON IPC, esbuild bundling, Aspire startup validation
**Confidence:** HIGH

## Summary

This phase establishes a Node.js sidecar project (`ts-symbol-extractor`) that communicates with the C# host via NDJSON over stdin/stdout. The C# side spawns the sidecar on demand per ingestion request using `System.Diagnostics.Process`, reads a single JSON response line from stdout, and deserializes it into a `SymbolGraphSnapshot`. The sidecar is bundled via esbuild into a single `dist/index.js` file and tested with vitest.

The codebase already has well-established patterns for every integration point: `IngestionService` with `PipelineOverride` test seams, `StartupValidator` as `IHostedLifecycleService`, `DocAgentServerOptions` for configuration, and `ServiceCollectionExtensions` for DI registration. The new code follows these patterns exactly -- no novel architectural decisions are needed.

**Primary recommendation:** Follow existing `IngestionService` + `StartupValidator` patterns verbatim. Use `System.Text.Json` with `PropertyNameCaseInsensitive = true` for NDJSON deserialization (matching existing `SnapshotStore` conventions). The sidecar returns camelCase JSON; C# deserializes case-insensitively.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Location: `src/ts-symbol-extractor/` alongside C# projects
- Module system: ESM with `"type": "module"` in package.json
- Sidecar source language: TypeScript (esbuild strips types during bundling)
- Bundle output: `dist/index.js` via esbuild, gitignored -- built on demand, not committed
- Test runner: vitest
- TypeScript version: ~5.9.x pinned
- Timeout: 300 seconds (matches existing IngestionService timeout)
- Error handling: Throw typed `TypeScriptIngestionException` with exit code + stderr content
- Stderr capture: Read asynchronously, forward each line to ILogger at Debug level
- Test seam: PipelineOverride pattern -- `Func<string, Task<SymbolGraphSnapshot>>?` that bypasses process spawning
- Sidecar mode: On-demand spawn per request only -- no Aspire resource registration for the sidecar itself
- Node.js detection: IHostedLifecycleService startup check via `node --version` -- log warning if missing, don't crash
- Version validation: Parse `node --version` output and require >= 22.x
- Auto build: Startup service runs `npm install && npm run build` in sidecar directory if `dist/index.js` is missing

### Claude's Discretion
- IPC contract details (NDJSON request/response schema)
- Internal sidecar source file organization
- Exact esbuild configuration
- npm scripts structure (build, test, lint)
- Logging format on stderr

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SIDE-01 | Node.js sidecar project with package.json, esbuild bundling, vitest test setup | Standard Stack (esbuild config, vitest config, package.json structure) |
| SIDE-02 | NDJSON stdin/stdout IPC protocol with defined request/response contract | Architecture Patterns (NDJSON protocol, request/response schema) |
| SIDE-03 | C# TypeScriptIngestionService that spawns sidecar, sends request, deserializes response | Architecture Patterns (Process spawning, async stderr, JSON deserialization) |
| SIDE-04 | Aspire AppHost startup validation for Node.js availability | Architecture Patterns (NodeAvailabilityValidator following StartupValidator pattern) |
</phase_requirements>

## Standard Stack

### Core (Node.js Sidecar)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| typescript | ~5.9.x | Type checking sidecar source | Locked decision; matches extraction target |
| esbuild | ^0.25.x | Bundle TS to single dist/index.js | Standard for Node CLI bundling; sub-second builds |
| vitest | ^3.x | Test runner | Locked decision; native ESM + TS support |

### Core (C# Side)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Diagnostics.Process | built-in | Spawn sidecar process | .NET built-in; no external dependency needed |
| System.Text.Json | built-in | NDJSON serialization/deserialization | Already used throughout codebase |

### Not Needed
| Library | Why Not |
|---------|---------|
| Aspire.Hosting.JavaScript | Sidecar is on-demand spawn, NOT an Aspire resource -- no registration needed |
| @microsoft/tsdoc | Phase 29 concern, not this phase |
| ts-morph | Out of scope per REQUIREMENTS.md |

**Installation (sidecar):**
```bash
cd src/ts-symbol-extractor
npm init -y
npm install --save-dev typescript@~5.9 esbuild@^0.25 vitest@^3 @types/node@^22
```

## Architecture Patterns

### Recommended Project Structure
```
src/ts-symbol-extractor/
  package.json           # ESM, scripts: build, test
  tsconfig.json          # strict, ESM, NodeNext
  vitest.config.ts       # minimal config
  src/
    index.ts             # stdin reader, dispatch, stdout writer
    types.ts             # Request/Response interfaces
    stub-extractor.ts    # Stub implementation (returns empty snapshot shape)
  dist/
    index.js             # esbuild output (gitignored)
  tests/
    stub-extractor.test.ts  # At least one passing test
```

### Pattern 1: NDJSON Request/Response Protocol
**What:** Single-line JSON objects separated by newlines over stdin/stdout.
**When to use:** All sidecar communication.

**Request schema (C# sends on stdin):**
```json
{"jsonrpc":"2.0","id":1,"method":"extract","params":{"tsconfigPath":"/abs/path/tsconfig.json"}}
```

**Response schema (sidecar writes to stdout):**
```json
{"jsonrpc":"2.0","id":1,"result":{"schemaVersion":"1.0","projectName":"my-project","sourceFingerprint":"stub","contentHash":null,"createdAt":"2026-03-08T00:00:00Z","nodes":[],"edges":[]}}
```

**Why JSON-RPC 2.0 framing:** Matches MCP's own protocol. Provides method dispatch, request correlation via `id`, and standardized error responses (`{"jsonrpc":"2.0","id":1,"error":{"code":-32600,"message":"..."}}`).

**Key rule:** Each message is exactly one line (no pretty-printing). `\n` terminates each message.

### Pattern 2: C# Process Spawning with Async Stderr
**What:** Spawn `node dist/index.js`, write request to stdin, read response from stdout, capture stderr asynchronously.
**Example:**
```csharp
// Source: existing IngestionService pattern + System.Diagnostics.Process docs
var psi = new ProcessStartInfo
{
    FileName = "node",
    Arguments = Path.Combine(sidecarDir, "dist", "index.js"),
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    WorkingDirectory = sidecarDir
};

using var process = new Process { StartInfo = psi };
var stderrLines = new List<string>();

process.ErrorDataReceived += (_, e) =>
{
    if (e.Data is not null)
    {
        stderrLines.Add(e.Data);
        logger.LogDebug("[ts-sidecar] {Line}", e.Data);
    }
};

process.Start();
process.BeginErrorReadLine();

// Write request, close stdin to signal EOF
await process.StandardInput.WriteLineAsync(requestJson);
process.StandardInput.Close();

// Read single response line
var responseLine = await process.StandardOutput.ReadLineAsync();

// Wait for exit with timeout
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
await process.WaitForExitAsync(timeoutCts.Token);

if (process.ExitCode != 0)
    throw new TypeScriptIngestionException(process.ExitCode, string.Join("\n", stderrLines));

var snapshot = JsonSerializer.Deserialize<SymbolGraphSnapshot>(responseLine!, jsonOptions);
```

### Pattern 3: PipelineOverride Test Seam
**What:** `Func<string, Task<SymbolGraphSnapshot>>?` property that bypasses real process spawning.
**Matches:** Existing `IngestionService.PipelineOverride` and `IncrementalSolutionIngestionService.PipelineOverride`.
```csharp
// Source: existing IngestionService pattern
internal Func<string, Task<SymbolGraphSnapshot>>? PipelineOverride { get; set; }

// In the ingestion method:
if (PipelineOverride is not null)
    return await PipelineOverride(tsconfigPath);
```

### Pattern 4: NodeAvailabilityValidator (IHostedLifecycleService)
**What:** Startup check that runs `node --version`, parses output, warns if missing or < 22.x.
**Matches:** Existing `StartupValidator` in `Validation/StartupValidator.cs`.
```csharp
// Source: existing StartupValidator pattern
public sealed class NodeAvailabilityValidator : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken ct)
    {
        // Run "node --version" -> parse "v22.x.y" -> warn if missing or < 22
        // If dist/index.js missing -> run "npm install && npm run build"
        // WARNING only, don't crash -- C# ingestion still works without Node.js
    }
    // ... other lifecycle methods return Task.CompletedTask
}
```

### Pattern 5: Sidecar stdin Reader (Node.js)
**What:** Read lines from stdin, parse as JSON-RPC, dispatch to handler, write response to stdout.
```typescript
// src/index.ts
import { createInterface } from 'node:readline';

const rl = createInterface({ input: process.stdin });

for await (const line of rl) {
  try {
    const request = JSON.parse(line);
    const result = await handleRequest(request);
    process.stdout.write(JSON.stringify(result) + '\n');
  } catch (err) {
    const errorResponse = {
      jsonrpc: '2.0',
      id: null,
      error: { code: -32603, message: String(err) }
    };
    process.stdout.write(JSON.stringify(errorResponse) + '\n');
  }
}
```

### Anti-Patterns to Avoid
- **Pretty-printing stdout JSON:** Will break NDJSON line-by-line reading. Always use compact JSON.
- **console.log() in sidecar:** Pollutes stdout. All logging MUST use `console.error()` (stderr).
- **Forgetting to close stdin:** C# side must close stdin after writing request to signal EOF to Node process.
- **Synchronous stderr reading:** Will deadlock. Must use `BeginErrorReadLine()` for async capture.
- **Bundling node_modules dependencies as external:** esbuild with `--platform=node` auto-marks builtins as external, but sidecar has no npm runtime deps (only TypeScript compiler API in Phase 29).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON serialization | Custom serializer | `System.Text.Json` / `JSON.stringify` | Already used everywhere; handles records, enums |
| Process lifecycle | Raw Process.Start/Kill | Structured pattern with timeout CTS + WaitForExitAsync | Timeout + cancellation + stderr capture is complex |
| Line-based stdin reading | Manual buffer parsing | `readline.createInterface` (Node) / `StreamReader.ReadLineAsync` (C#) | Handles line buffering, partial reads correctly |
| Version parsing | Regex on "v22.3.1" | `Version.TryParse` after stripping "v" prefix | Handles comparison operators correctly |

## Common Pitfalls

### Pitfall 1: stdout Pollution Breaks NDJSON
**What goes wrong:** Any `console.log()` or debug output on stdout corrupts the NDJSON stream.
**Why it happens:** Node.js defaults to console.log -> stdout.
**How to avoid:** Lint rule or runtime check. All sidecar logging via `console.error()`. Consider wrapping: `const log = (...args: unknown[]) => console.error(...args);`
**Warning signs:** C# JsonSerializer throws on deserialization with "unexpected character".

### Pitfall 2: Process Deadlock on stderr/stdout Buffering
**What goes wrong:** Reading stdout synchronously before stderr is drained (or vice versa) causes the process to hang when buffers fill.
**Why it happens:** OS pipe buffers are finite (~64KB). If sidecar writes enough stderr to fill the buffer, it blocks waiting for the reader.
**How to avoid:** Always use `BeginErrorReadLine()` (async event-based) for stderr BEFORE reading stdout. Never read both synchronously.
**Warning signs:** Process hangs on large TypeScript projects with many warnings.

### Pitfall 3: Missing dist/index.js on First Run
**What goes wrong:** `TypeScriptIngestionService` fails because esbuild hasn't been run yet.
**Why it happens:** `dist/` is gitignored; fresh clone has no built sidecar.
**How to avoid:** `NodeAvailabilityValidator` startup service checks and runs `npm install && npm run build` if `dist/index.js` is missing.
**Warning signs:** FileNotFoundException or Node "Cannot find module" error.

### Pitfall 4: camelCase vs PascalCase JSON Property Names
**What goes wrong:** Sidecar produces camelCase JSON (`schemaVersion`), C# records use PascalCase (`SchemaVersion`).
**Why it happens:** JavaScript convention is camelCase; C# convention is PascalCase.
**How to avoid:** Use `PropertyNameCaseInsensitive = true` in `JsonSerializerOptions` (matching existing `SnapshotStore` pattern). The sidecar outputs camelCase; C# reads case-insensitively.
**Warning signs:** Deserialized snapshot has all null/default properties.

### Pitfall 5: Enum Serialization Mismatch
**What goes wrong:** C# `SymbolKind.Namespace` serializes as `0` (integer) by default, but sidecar may send `"Namespace"` (string).
**Why it happens:** `System.Text.Json` default is integer enum serialization.
**How to avoid:** Add `JsonStringEnumConverter` to serializer options for the NDJSON contract, OR have the sidecar send integer values matching C# enum ordinals. Integer is simpler and matches existing MessagePack serialization.
**Warning signs:** JsonException on enum deserialization.

### Pitfall 6: Node.js ESM + readline Async Iterator
**What goes wrong:** The `for await...of` loop on readline doesn't exit when stdin closes on some Node versions.
**Why it happens:** Edge case in Node.js readline async iterator implementation.
**How to avoid:** Use Node >= 22.x (locked decision). Also handle the `close` event explicitly. Alternatively, read stdin manually with `process.stdin.on('data')` and split on newlines.
**Warning signs:** Sidecar process hangs after C# closes stdin.

## Code Examples

### esbuild Configuration (build script)
```typescript
// Source: esbuild official docs (https://esbuild.github.io/api/)
// package.json script: "build": "node build.mjs"

// build.mjs
import { build } from 'esbuild';

await build({
  entryPoints: ['src/index.ts'],
  bundle: true,
  platform: 'node',
  target: 'node22',
  format: 'esm',
  outfile: 'dist/index.js',
  // No external deps in Phase 28 (stub only)
  // Phase 29 will add: external: ['typescript']
});
```

### vitest.config.ts
```typescript
// Source: vitest docs (https://vitest.dev/config/)
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
  },
});
```

### tsconfig.json (sidecar)
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "strict": true,
    "esModuleInterop": true,
    "declaration": false,
    "outDir": "dist",
    "rootDir": "src",
    "types": ["node"]
  },
  "include": ["src/**/*.ts"],
  "exclude": ["node_modules", "dist", "tests"]
}
```

### package.json
```json
{
  "name": "ts-symbol-extractor",
  "version": "0.1.0",
  "type": "module",
  "private": true,
  "scripts": {
    "build": "node build.mjs",
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "devDependencies": {
    "typescript": "~5.9.0",
    "esbuild": "^0.25.0",
    "vitest": "^3.0.0",
    "@types/node": "^22.0.0"
  }
}
```

### TypeScriptIngestionException
```csharp
// Source: project convention (typed exceptions with context)
public sealed class TypeScriptIngestionException : Exception
{
    public int ExitCode { get; }
    public string StderrOutput { get; }

    public TypeScriptIngestionException(int exitCode, string stderrOutput)
        : base($"TypeScript sidecar exited with code {exitCode}: {stderrOutput}")
    {
        ExitCode = exitCode;
        StderrOutput = stderrOutput;
    }
}
```

### JSON Serializer Options for NDJSON
```csharp
// Source: matches existing SnapshotStore.JsonOptions pattern
private static readonly JsonSerializerOptions s_ndjsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // for serializing request
    Converters = { new JsonStringEnumConverter() } // if using string enum values
};
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Aspire.Hosting.NodeJs` | `Aspire.Hosting.JavaScript` | Aspire 13.0 | Package renamed; old one deprecated |
| CommonJS (`require`) | ESM (`import`) | Node 22+ | ESM is default for new Node.js projects |
| Jest | vitest | 2023+ | vitest has native ESM+TS support, faster |
| webpack/rollup for CLI | esbuild | 2022+ | Sub-second builds, zero config for simple cases |
| `process.stdin.resume()` + manual buffering | `readline.createInterface` async iterator | Node 18+ | Clean async/await stdin reading |

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework (C#) | xUnit + FluentAssertions (existing) |
| Framework (Node.js) | vitest ^3.x (new) |
| Config file (C#) | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| Config file (Node.js) | `src/ts-symbol-extractor/vitest.config.ts` (Wave 0) |
| Quick run command (C#) | `dotnet test --filter "FullyQualifiedName~TypeScriptIngestion"` |
| Quick run command (Node.js) | `cd src/ts-symbol-extractor && npm test` |
| Full suite command | `dotnet test && cd src/ts-symbol-extractor && npm test` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SIDE-01 | Sidecar project builds and has passing test | smoke | `cd src/ts-symbol-extractor && npm run build && npm test` | Wave 0 |
| SIDE-02 | NDJSON request produces valid response | unit | `cd src/ts-symbol-extractor && npx vitest run tests/stub-extractor.test.ts` | Wave 0 |
| SIDE-03 | C# service spawns sidecar and deserializes response | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptIngestionServiceTests"` | Wave 0 |
| SIDE-04 | Startup validator detects Node.js availability | unit | `dotnet test --filter "FullyQualifiedName~NodeAvailabilityValidator"` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~TypeScriptIngestion"` + `cd src/ts-symbol-extractor && npm test`
- **Per wave merge:** `dotnet test && cd src/ts-symbol-extractor && npm test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `src/ts-symbol-extractor/` -- entire sidecar project (package.json, tsconfig.json, vitest.config.ts, build.mjs)
- [ ] `src/ts-symbol-extractor/tests/stub-extractor.test.ts` -- covers SIDE-01, SIDE-02
- [ ] `tests/DocAgent.Tests/TypeScriptIngestionServiceTests.cs` -- covers SIDE-03
- [ ] `tests/DocAgent.Tests/NodeAvailabilityValidatorTests.cs` -- covers SIDE-04

## Open Questions

1. **Enum serialization: string vs integer?**
   - What we know: Existing MessagePack serialization uses integers. Existing JSON serialization in tools uses `JsonNamingPolicy.CamelCase` but no `JsonStringEnumConverter`.
   - What's unclear: Whether to have sidecar send integer enum values (0, 1, 2...) or string names ("Namespace", "Type", "Method"...).
   - Recommendation: Use integers to match existing serialization. Simpler, no converter needed. Document the enum ordinal mapping in the sidecar types.ts.

2. **Auto-build timing: startup service vs lazy?**
   - What we know: CONTEXT.md says startup service runs npm install/build if dist/index.js missing.
   - What's unclear: Whether npm install could be slow enough to delay server startup noticeably.
   - Recommendation: Run in `StartingAsync` as documented. npm install for a project with only devDependencies is fast (~2-5 seconds). Log progress so user knows what's happening.

## Sources

### Primary (HIGH confidence)
- Codebase inspection: `IngestionService.cs`, `StartupValidator.cs`, `ServiceCollectionExtensions.cs`, `DocAgentServerOptions.cs`, `Symbols.cs` -- all patterns verified directly
- esbuild official docs (https://esbuild.github.io/api/) -- platform, format, bundle options
- vitest official docs (https://vitest.dev/config/) -- configuration reference
- System.Diagnostics.Process .NET docs -- RedirectStandardInput/Output/Error, BeginErrorReadLine, WaitForExitAsync

### Secondary (MEDIUM confidence)
- Aspire.Hosting.JavaScript docs (https://aspire.dev/integrations/frameworks/javascript/) -- confirmed package rename from NodeJs; NOT needed for this phase (on-demand spawn pattern)
- NDJSON spec (https://github.com/ndjson/ndjson-spec) -- one JSON object per line, newline terminated

### Tertiary (LOW confidence)
- None -- all findings verified against codebase or official docs

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- locked decisions, verified against codebase patterns
- Architecture: HIGH -- every pattern copies an existing codebase pattern (IngestionService, StartupValidator, SnapshotStore JSON options)
- Pitfalls: HIGH -- based on well-known Process deadlock issues and observed codebase conventions

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable domain; no fast-moving dependencies)
