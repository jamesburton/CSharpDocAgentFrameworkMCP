# MCP Setup & Installation System — Design Spec

**Date:** 2026-03-16
**Status:** Approved (blockers resolved; warnings addressed in rev 3)
**Scope:** Cross-platform install scripts, per-agent MCP config writing, project-level init, Claude Code skill, git hooks, and documentation

---

## 1. Problem Statement

DocAgent.McpServer is a fully functional MCP server, but there is no automated path to wire it into the agent tools a developer already has installed (Claude Code, Cursor, VS Code/Copilot, Windsurf, OpenCode, Zed, etc.). Setting it up requires manually editing agent-specific JSON config files and knowing the correct transport arguments. This spec defines an automated, cross-platform setup system that eliminates that friction.

---

## 2. Goals

- One command (`dotnet run scripts/install-user.cs`) configures DocAgent for every detected agent on the machine.
- One command (`docagent-mcp init`) wires DocAgent into a specific project.
- All logic is cross-platform: works on Windows (CMD, PowerShell), macOS, and Linux without bash dependency in the scripts themselves.
- Adding support for a new agent requires changes in one place.
- Git hooks for automatic re-ingest are available but opt-in; the default update path is explicit (`docagent-mcp update`).

## 3. Non-Goals

- NuGet XML doc ingestion (`type: nuget`) and remote git repo ingestion (`type: git-remote`) as secondary sources — config format accommodates them, implementation deferred.
- Background file watcher re-ingest — future option.
- Periodic/scheduled re-ingest — future option.
- `docagent-mcp uninstall` — not in scope for this spec; documented as a manual process in `docs/Setup.md`.

---

## 4. Architecture

### 4.1 Overview

Layered: thin `.cs` bootstrapper scripts delegate to binary subcommands. All real logic lives in `DocAgent.McpServer` CLI subcommands, which work identically whether called via script or directly. Scripts exist solely to bootstrap the tool installation on a fresh machine.

```
scripts/install-user.cs
  └─ checks PATH for docagent-mcp
  └─ if absent: dotnet tool install -g (default) or prompts for mode B/C
  └─ delegates to: docagent-mcp install

scripts/setup-project.cs
  └─ delegates to: docagent-mcp init

docagent-mcp install     ← writes user-level agent MCP configs
docagent-mcp init        ← writes project-level configs, offers ingest + hooks
docagent-mcp update      ← re-ingests current project (reads docagent.project.json)
docagent-mcp hooks enable|disable  ← manages .git/hooks/
```

### 4.2 CLI Routing — MCP Server Mode vs CLI Mode

`DocAgent.McpServer` is a single binary that serves two roles. The entry point (`Program.cs` / `CliRunner.cs`) routes on startup:

```
args[0] is one of: install | init | update | hooks
    → route to the corresponding CLI command handler, then exit
args[0] is absent or unrecognised
    → enter MCP server mode (existing behaviour: stdio JSON-RPC loop)
```

The routing check happens **before** any MCP wiring. Known subcommand strings are declared in `CliRunner.cs` as a static set. This is the only change to the existing server entry point — the MCP path is entirely unchanged.

**Why this is safe:** MCP clients invoke the binary with no arguments (or only transport flags), so there is zero collision risk with existing MCP startup.

### 4.3 File & Directory Layout

```
scripts/
  install-user.cs          # Cross-platform user-level bootstrapper
  setup-project.cs         # Cross-platform project-level bootstrapper
  README.md                # "Run with: dotnet run scripts/install-user.cs"

docs/
  Setup.md                 # Quick-start, prerequisites, hosting modes, troubleshooting
  Agents.md                # Per-agent manual config reference + extension guide
  GitHooks.md              # Opt-in hook guide

src/DocAgent.McpServer/
  Cli/
    CliRunner.cs            # Routes first arg to subcommand if recognised; else MCP mode
    InstallCommand.cs       # docagent-mcp install
    InitCommand.cs          # docagent-mcp init [--non-interactive flags]
    UpdateCommand.cs        # docagent-mcp update [--quiet]
    HooksCommand.cs         # docagent-mcp hooks enable|disable

~/.claude/plugins/docagent/          # Written by install command
  setup-project.skill.md             # /docagent:setup-project skill
  update.skill.md                    # /docagent:update skill
```

---

## 5. Hosting Modes

| Mode | Description | Command written into agent configs |
|------|-------------|------------------------------------|
| A (default) | `dotnet tool install -g DocAgent.McpServer` → `docagent-mcp` on PATH | `"command": "docagent-mcp"` |
| B | Self-contained binary published to `~/.docagent/bin/docagent-mcp[.exe]` | `"command": "<abs-path-to-binary>"` |
| C | MCP server only — `dotnet run --project <source-path>` | `"command": "dotnet", "args": ["run", "--project", "<path>"]` |

**Mode C constraint:** Mode C supports only MCP server usage. The CLI subcommands (`install`, `init`, `update`, `hooks`) require Mode A or B because `dotnet run` startup latency makes interactive CLI use impractical and argument forwarding through `dotnet run` is unreliable for interactive TTY sessions. `scripts/install-user.cs` explains this constraint when Mode C is selected and will not attempt to delegate to `docagent-mcp install` via `dotnet run`; instead it runs the bootstrap logic inline within the script itself for that case.

**Version pinning:** `dotnet tool install -g` installs the latest stable published version by default. A `--version <x.y.z>` flag is accepted by the install script for teams that require pinned installs. The installed version is recorded in `~/.docagent/config.json`.

**Version pin and `docagent-mcp update`:** The `update` command (re-ingest) does not upgrade the binary — it only re-ingests source. There is no automatic binary upgrade. To upgrade the tool: run `dotnet tool update -g DocAgent.McpServer` (or `dotnet tool update -g DocAgent.McpServer --version <x.y.z>` for a pinned version), then run `docagent-mcp update` to re-ingest with the new binary. `docs/Setup.md` documents this upgrade flow.

**`dotnet tool install` failure in non-interactive mode:** With `--yes`, if `dotnet tool install -g` fails, the install script automatically falls back to Mode B (self-contained binary) without prompting. If Mode B also fails, exit code 1 is returned with the error.

**Chosen mode** is stored in `~/.docagent/config.json` (see Section 5.1) and used when writing agent MCP configs and when generating git hook scripts.

### 5.1 `~/.docagent/config.json` Schema

```json
{
  "version": 1,
  "hostingMode": "A",
  "binaryPath": null,
  "sourceProjectPath": null,
  "artifactsDir": "<user-home>/.docagent/artifacts",
  "installedAt": "<ISO-8601 timestamp>",
  "toolVersion": "1.2.0"
}
```

`binaryPath` is populated for Mode B. `sourceProjectPath` is populated for Mode C. On missing or malformed file, the install command treats it as a fresh install and re-prompts.

---

## 6. Agent Detection & MCP Config Writing (`docagent-mcp install`)

### 6.1 Detection Matrix

Each agent entry specifies: detection probe (Windows and macOS/Linux), config file path, and JSON schema shape. This is the single place to add new agents.

| Agent | Windows detection | macOS/Linux detection | Config file | JSON key path |
|-------|-------------------|-----------------------|-------------|----------------|
| Claude Desktop | `%APPDATA%\Claude\` dir exists | `~/Library/Application Support/Claude/` exists | `<config-dir>/claude_desktop_config.json` | `mcpServers` |
| Claude Code CLI | `claude` on PATH | `claude` on PATH | `~/.claude.json` | `mcpServers` |
| VS Code / Copilot | `code` on PATH or `%APPDATA%\Code\` | `code` on PATH or `~/.config/Code/` | `.vscode/mcp.json` in cwd | `servers` |
| Cursor | `cursor` on PATH or `%LOCALAPPDATA%\Programs\cursor\` | `cursor` on PATH or `/Applications/Cursor.app` | `~/.cursor/mcp.json` | `mcpServers` |
| Windsurf | `%USERPROFILE%\.codeium\windsurf\` exists | `~/.codeium/windsurf/` exists | `~/.codeium/windsurf/mcp_config.json` | `mcpServers` |
| OpenCode | `opencode` on PATH or `~/.config/opencode/` | same | `~/.config/opencode/config.json` | `mcp.servers` |
| Zed | `zed` on PATH or `~/.config/zed/` | `~/.config/zed/` exists | `~/.config/zed/settings.json` | `context_servers` |

Per-agent JSON structure details (exact shapes) are documented in `docs/Agents.md` — that file is the implementation reference and the extension guide for new agents.

### 6.2 MCP Server Entry (Hosting Mode A)

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent-mcp",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/abs/path/to/.docagent/artifacts"
      }
    }
  }
}
```

`DOCAGENT_ARTIFACTS_DIR` is written as an absolute path resolved at install time. For modes B and C, `command`/`args` are adjusted per Section 5.

### 6.3 Config Merge Strategy

1. Read target config file (create minimal valid stub if absent).
2. Deserialise to `JsonNode`.
3. **Conflict check:** if `mcpServers.docagent` (or equivalent key path) already exists with values that differ from what would be written:
   - Interactive mode: display a diff and prompt `[O]verwrite / [S]kip / [V]iew full config`.
   - Non-interactive with `--yes`: overwrite silently.
   - Non-interactive without `--yes`: skip and emit a warning to stderr; exit code 0 (not a fatal error).
4. Merge only the `docagent` key; leave all other entries untouched.
5. Write back with original formatting preserved where possible (`JsonWriterOptions.Indented = true`).

This process is safe to run multiple times. A re-run with no config changes is a no-op (no prompt shown).

### 6.4 Confirmation UI

```
Detected agents:
  [✓] Claude Code CLI    → will write ~/.claude.json
  [✓] Cursor             → will write ~/.cursor/mcp.json
  [✗] Windsurf           → not found (skip)
  [✗] VS Code            → not found (skip)

Proceed? [Y/n/select]
```

`select` drops into per-agent toggle before writing. Nothing is written without this confirmation. In non-interactive mode (`--yes`), confirmation is skipped and all detected agents are written.

**No agents detected:** The command prints a message directing the user to `docs/Agents.md` for manual configuration instructions and exits with code 0.

### 6.5 Error Handling

| Failure | Behaviour |
|---------|-----------|
| `dotnet tool install -g` fails (network, permissions, version conflict) | Print error with dotnet error output. Offer to retry with `--version <x>`, switch to Mode B, or switch to Mode C. Exit code 1 if user declines all options. |
| Config file is read-only or locked | Skip that agent with a clear warning. Print the config entry that would have been written so the user can apply it manually. Continue with remaining agents. |
| Config file contains malformed JSON | Skip with warning. Print file path. Do not attempt to write. |
| Config directory does not exist | Create the directory, then write. |

---

## 7. Project-Level Setup (`docagent-mcp init`)

### 7.1 Interactive Flow

```
DocAgent Project Setup
──────────────────────
Primary source: MyApp.sln  [auto-detected — confirm? Y/n]

Secondary sources (optional):
  [1] Add another local .sln or .csproj
  [2] Add a TypeScript/Node project directory
  [coming soon] NuGet package XML docs
  [coming soon] Remote git repository
  [3] Skip

Agent configs to write for this project:
  [✓] CLAUDE.md entry
  [✓] .vscode/mcp.json  (VS Code detected)

Ingest now? [Y/n]
Enable git hooks for auto-update? [y/N]
```

Primary source auto-detection: searches cwd for a single `.sln`, then multiple `.sln`, then `.csproj`. If ambiguous, prompts for selection.

**Invalid or missing primary source:** If the provided or detected path does not point to a `.sln` or `.csproj`, the command prints an error and exits with code 1 without writing any files.

### 7.2 `--non-interactive` Flags

For use by the Claude Code skill and CI:

```
docagent-mcp init \
  --primary MyApp.sln \
  --secondary dotnet:../SharedLib/SharedLib.sln \
  --secondary typescript:./frontend \
  --ingest \
  --no-hooks \
  --yes
```

`--yes` skips all confirmation prompts and proceeds with defaults for any unspecified options. Without `--yes` in non-interactive mode (i.e., flags provided but no TTY), the command proceeds without interactive prompts using flag values only.

### 7.3 Files Written

**`docagent.project.json`** (committed to repo):
```json
{
  "version": 1,
  "primarySource": "MyApp.sln",
  "secondarySources": [
    { "type": "dotnet", "path": "../SharedLib/SharedLib.sln" },
    { "type": "typescript", "path": "./frontend" }
  ],
  "artifactsDir": ".docagent/artifacts",
  "excludeTestFiles": true
}
```

The `type` field is an open enum: current values are `dotnet` and `typescript`; future values `nuget` and `git-remote` are accepted and stored but silently skipped during ingestion until implemented. This ensures forward-compatibility: a newer binary can ingest a config written by an older one without error.

**`.gitignore` append** (idempotent — checks for sentinel before appending):
```
# DocAgent snapshot artifacts
.docagent/artifacts/
```

Sentinel checked: if the exact line `.docagent/artifacts/` already exists in `.gitignore` (exact match after trimming leading/trailing whitespace, normalising line endings to LF for comparison), the append is skipped.

**`CLAUDE.md` append** (idempotent — checks for sentinel `<!-- docagent -->` before appending):
```markdown
<!-- docagent -->
## DocAgent (code documentation MCP)
This project uses DocAgent to serve symbol graph and documentation queries.
- Re-ingest after significant code changes: run `/docagent:update` or `docagent-mcp update`
- Git hooks for automatic re-ingest: see `docs/GitHooks.md`
- MCP tools available: search_symbols, get_symbol, find_implementations, explain_project, and more
<!-- /docagent -->
```

If the sentinel block already exists, `init` replaces it with the current content (idempotent update). If CLAUDE.md does not exist, it is created.

**Agent project configs:** `.vscode/mcp.json` and equivalent files for detected agents, using the same merge strategy as Section 6.3.

---

## 8. Claude Code Skills

Two skills installed to `~/.claude/plugins/docagent/` by the install command.

### 8.1 `/docagent:setup-project`

The skill's job is to gather answers to the same questions `docagent-mcp init` would ask interactively, but using Claude's `AskUserQuestion` tool for a clickable multi-choice UI, then invoke `init --non-interactive` with those answers.

**Flag mapping — AskUserQuestion answer → `--non-interactive` flag:**

| Step | AskUserQuestion prompt | Collected value | Flag passed to init |
|------|------------------------|-----------------|---------------------|
| 1 | Existing config found: `[Reconfigure] [Re-ingest only] [Cancel]` | reconfigure / reingest / cancel | If reingest: skip to step 6 with `--ingest` only |
| 2 | Primary source (detected list or free-text) | Path string | `--primary <path>` |
| 3a | Add .NET secondary source? (detected list + None) | Path string or skip | `--secondary dotnet:<path>` (repeatable) |
| 3b | Add TypeScript secondary source? (detected list + None) | Path string or skip | `--secondary typescript:<path>` (repeatable) |
| 4 | Ingest now? `[Yes / No]` | bool | `--ingest` if Yes |
| 5 | Enable git hooks? `[Yes / No / Tell me more]` | bool / explain | If "Tell me more": show GitHooks.md excerpt, re-ask. `--no-hooks` if No |

**Step-by-step flow:**

1. Check for `docagent.project.json` in cwd.
   - If present: ask step 1 question above; handle `[Re-ingest only]` by jumping to step 6.
   - If absent: proceed to step 2.
2. List detected `.sln`/`.csproj` files in cwd and parent (up to 2 levels). Present as numbered choices + free-text option. Map to `--primary`.
3. List detected nearby `.sln`/`.csproj` (excluding primary) and TypeScript dirs (`package.json` presence check). Present as multi-select checkboxes. Map to repeated `--secondary` flags.
4. Ask `Ingest now?`.
5. Ask `Enable git hooks?` with "Tell me more" fallback.
6. Run `docagent-mcp init --non-interactive --primary <x> [--secondary <type>:<path> ...] [--ingest] [--no-hooks] --yes` as a shell command.
7. If `--ingest` was selected, stream stdout of the ingest run to the user.
8. Report completion: list files written, symbol count and snapshot hash from ingest JSON output, and reminder: "Run `/docagent:update` or `docagent-mcp update` any time to re-ingest."

### 8.2 `/docagent:update`

Invokes `docagent-mcp update` in cwd and surfaces the result.

**Pre-condition check:** Before invoking, the skill checks for `docagent.project.json` in cwd. If absent and `~/.docagent/config.json` is also missing, the skill asks: `"DocAgent is not set up for this project yet. Run setup now? [Yes / No]"`. If Yes, it invokes `/docagent:setup-project`. If No, it exits without running update.

**`docagent-mcp update` stdout format** (consumed by the skill):
```json
{
  "status": "ok",
  "projectsIngested": 3,
  "symbolCount": 4821,
  "durationMs": 12400,
  "snapshotHash": "abc123"
}
```

The skill formats this as a brief prose summary for the user:
> "DocAgent updated: 3 projects, 4,821 symbols indexed in 12s (snapshot abc123)."

On non-zero exit from `docagent-mcp update`, the skill surfaces stderr to the user and suggests running `docagent-mcp update` directly in a terminal for full output.

---

## 9. Git Hooks (Opt-in)

### 9.1 Enable / Disable

```
docagent-mcp hooks enable    # installs post-commit + post-merge into .git/hooks/
docagent-mcp hooks disable   # removes them
```

Both commands check for a `.git/` directory in cwd; exit with a clear error if not in a git repo.

`hooks enable` on a repo that already has `post-commit` or `post-merge` hooks: appends the docagent block to the existing file rather than overwriting it. A pair of sentinel comment lines marks the block:

```
# BEGIN docagent-mcp
...hook body...
# END docagent-mcp
```

Duplicate handling: if a `# BEGIN docagent-mcp` / `# END docagent-mcp` block already exists in the hook file, the existing block is replaced in-place (idempotent update). The sentinel pair is the canonical marker — a single `# docagent-mcp update` comment from an older install is treated as legacy and replaced with the pair.

`hooks disable` removes all lines from `# BEGIN docagent-mcp` through `# END docagent-mcp` (inclusive). If the hook file becomes empty after removal (docagent was the only content), the file is deleted.

### 9.2 Hook Script (Written by `hooks enable`)

The hook body varies by hosting mode (read from `~/.docagent/config.json`):

**Mode A:**
```bash
#!/bin/sh
# docagent-mcp update
docagent-mcp update --quiet || true
```

**Mode B:**
```bash
#!/bin/sh
# docagent-mcp update
"$HOME/.docagent/bin/docagent-mcp" update --quiet || true
```

Git executes `.git/hooks/` files via its bundled sh on all platforms including Windows (Git for Windows). The `|| true` ensures a failed re-ingest never blocks a commit. `--quiet` suppresses all stdout/stderr unless the exit code is non-zero.

### 9.3 Team Behaviour

Hooks live in `.git/hooks/` and are not committed to the repo. Each team member opts in individually by running `docagent-mcp hooks enable`. `docs/GitHooks.md` explains setup, disabling, and the `--no-verify` escape hatch.

---

## 10. Documentation

| File | Purpose |
|------|---------|
| `docs/Setup.md` | Quick-start (3 steps), prerequisites (.NET 10 SDK), all three hosting modes with trade-offs, version pinning, troubleshooting FAQ, manual uninstall steps |
| `docs/Agents.md` | Per-agent config reference: config file path (per OS), exact JSON structure expected by each agent, whether agent restart is required after config change, guide for adding a new agent |
| `docs/GitHooks.md` | Hook setup via `hooks enable`, what triggers re-ingest, how to disable, `--no-verify` note, Mode B hook path note |

`docs/Agents.md` is the canonical extension guide: each agent entry is a template that a contributor follows to add a new agent, keeping the "one place to change" promise verifiable.

---

## 11. Near-Term Roadmap (Out of Scope for This Spec)

Ordered by expected priority:

| Item | Config stub | Notes |
|------|-------------|-------|
| NuGet XML doc secondary sources (`type: nuget`) | Accepted and stored now, silently skipped during ingestion | XML doc extraction from NuGet package cache |
| Remote git repo secondary sources (`type: git-remote`) | Accepted and stored now, silently skipped during ingestion | Clone to temp dir + ingest |
| `docagent-mcp uninstall` | — | Remove agent configs, hooks, `~/.docagent/` |
| Background file watcher re-ingest | — | Post-save trigger without git commits |
| Periodic/scheduled re-ingest | — | OS-level scheduler integration |

---

## 12. Decisions Log

| Question | Decision | Rationale |
|----------|----------|-----------|
| Cross-platform scripting mechanism | .NET 10 single-file C# scripts (`dotnet run script.cs`) | No bash dependency; same language as codebase; .NET 10 SDK is a prerequisite |
| CLI vs MCP server routing | `args[0]` subcommand prefix check in `CliRunner.cs`; fall through to MCP mode if unrecognised | Zero risk of collision with MCP client invocations (no args); minimal change to existing Program.cs |
| Mode C and CLI subcommands | Mode C is MCP-server-only; CLI subcommands require Mode A or B | `dotnet run` startup latency and TTY arg-forwarding make CLI use via Mode C impractical |
| Git hooks mechanism | Minimal sh scripts (git's own sh, universal across platforms) | Hooks are a bash/sh interface regardless of OS when using Git for Windows |
| Default hosting mode | A (`dotnet tool install -g`) | Standard .NET tooling pattern; cleanest cross-platform PATH story |
| Default update trigger | Explicit command; git hooks opt-in | Avoids surprise background processes; hooks documented as opt-in |
| Secondary sources in scope | `dotnet` and `typescript` only; `nuget` and `git-remote` as future enum values | Config format already forward-compatible; implementations deferred |
| Config merge conflict | Interactive: diff + prompt; non-interactive `--yes`: overwrite; non-interactive without `--yes`: skip + warn | Predictable behaviour in all three contexts without data loss |
| CLAUDE.md and .gitignore idempotency | Sentinel marker for CLAUDE.md block; exact-line match (trimmed, LF-normalised) for .gitignore | Safe to re-run init without duplicating entries; handles CRLF on Windows |
| Exit code asymmetry (§6.3 vs §6.5) | Skipped config write (§6.3) → exit 0; declined recovery after tool install failure (§6.5) → exit 1 | A skipped write is a handled no-op (not an error); a declined install failure leaves setup incomplete (is an error) |
| Version pin vs `update` command | `update` re-ingests only; binary upgrade is a manual `dotnet tool update -g` step | Keeps update fast and predictable; avoids silent binary upgrades during ingest |
| `dotnet tool install` failure in non-interactive `--yes` mode | Auto-fallback to Mode B; if Mode B also fails → exit 1 | Unattended installs should not hang waiting for input |
| Git hook sentinel format | `# BEGIN docagent-mcp` / `# END docagent-mcp` pair; idempotent replace if pair exists | Unambiguous range marker; handles legacy single-line sentinel by replacing it with pair |
| Hook content per hosting mode | Mode A: `docagent-mcp update`; Mode B: absolute binary path; Mode C: unsupported (no hook written) | Hooks must work without PATH resolution; Mode C has no stable binary to reference |
