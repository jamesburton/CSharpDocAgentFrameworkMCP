# DocAgent Setup Guide

Get DocAgent running in three steps.

---

## Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download)
- One or more supported AI agent hosts (Claude Desktop, Claude Code CLI, VS Code/Copilot, Cursor, Windsurf, OpenCode, or Zed)

---

## Quick Start (3 steps)

### 1. Install the CLI

```bash
dotnet run scripts/install-user.cs
```

Installs the `docagent` global tool and auto-detects your installed agent hosts.

### 2. Initialise your project

```bash
cd /path/to/your-project
dotnet run scripts/setup-project.cs
```

Creates `.docagent/project.json` in the project root. Commit this file.

### 3. Ingest your codebase

Inside any configured agent host, call the MCP tool:

```
ingest_solution path=/absolute/path/to/YourSolution.sln
```

Then query with `search_symbols`, `get_symbol`, `explain_solution`, etc.

---

## Hosting Modes

DocAgent supports three modes for how the MCP server binary is delivered to agent hosts.

### Mode A — Global tool (recommended)

Installed via `dotnet tool install -g DocAgent.McpServer`. The `docagent` binary is on PATH and agent hosts invoke it directly.

**Agent host entry (stdio):**
```json
{
  "command": "docagent",
  "args": ["--stdio"],
  "env": { "DOCAGENT_ARTIFACTS_DIR": "~/.docagent/artifacts" }
}
```

**Update:**
```bash
dotnet tool update -g DocAgent.McpServer
```

### Mode B — Self-contained binary

Published to `~/.docagent/bin/` via `dotnet publish --self-contained`. Use when NuGet is unavailable (air-gapped machines, locked-down CI).

**Install:**
```bash
dotnet run scripts/install-user.cs --mode B --yes
```

Add `~/.docagent/bin` to your PATH, then run `docagent install`.

**Agent host entry (stdio):**
```json
{
  "command": "/home/<user>/.docagent/bin/docagent",
  "args": ["--stdio"],
  "env": { "DOCAGENT_ARTIFACTS_DIR": "~/.docagent/artifacts" }
}
```

### Mode C — Direct dotnet run

The agent host launches the server directly from the repository. No global tool needed. Suitable for CI or contributors working from source.

**Agent host entry (stdio):**
```json
{
  "command": "dotnet",
  "args": ["run", "--project", "/path/to/DocAgent.McpServer", "--", "--stdio"],
  "env": { "DOCAGENT_ARTIFACTS_DIR": "/tmp/docagent-artifacts" }
}
```

No installer step is needed; skip to `docagent init` (or `dotnet run scripts/setup-project.cs`).

---

## Version Pinning

Pin to a specific release to keep a team on a known-good version:

```bash
dotnet run scripts/install-user.cs --version 2.1.0
```

Or update the project config:

```json
// .docagent/project.json
{
  "toolVersion": "2.1.0"
}
```

When `docagent` detects a version mismatch at startup it prints a warning and exits, preventing silent incompatibilities.

---

## Upgrade Flow

```bash
# Mode A
dotnet tool update -g DocAgent.McpServer

# Mode B (rebuild self-contained binary)
dotnet run scripts/install-user.cs --mode B --yes

# Mode C (pull latest source)
git pull
```

After upgrading, re-run `docagent install` to refresh agent host configs if the entry point changed.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `docagent: command not found` | Global tool not on PATH | Add `~/.dotnet/tools` to your PATH, or use Mode B/C |
| `dotnet tool install` fails with auth error | Missing NuGet source | Configure a NuGet source or use `--yes` to fall back to Mode B |
| Agent host can't connect to MCP server | `DOCAGENT_ARTIFACTS_DIR` unset or unwritable | Set the env var to a directory the server can write to |
| `ingest_solution` times out | Very large solution | Set `DOCAGENT_INGESTION_TIMEOUT_SECONDS=3600` in env |
| Snapshot hash mismatch after re-ingest | Non-deterministic build output | Run with `forceReindex=true` to clear cached state |
| Tool version mismatch warning | Agent host config pinned to old version | Run `docagent install` again after upgrading |
| Git hook blocks commit | Hook exit code non-zero | See `docs/GitHooks.md` for the `--no-verify` escape hatch |

---

## Manual Uninstall

### Mode A

```bash
dotnet tool uninstall -g DocAgent.McpServer
```

Remove agent host entries manually (see `docs/Agents.md` for file locations).

### Mode B

```bash
rm -rf ~/.docagent/bin
```

Remove the PATH entry you added and remove agent host entries manually.

### Mode C

Remove the agent host entry that points to `dotnet run`.

### Clean project state

```bash
rm -rf .docagent
```

Removes project config and any locally cached snapshots. The user-level `~/.docagent/artifacts` store is unaffected.
