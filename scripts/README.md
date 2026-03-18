# DocAgent Setup Scripts

Two thin bootstrapper scripts for installing and initialising DocAgent. Both require only the .NET 10 SDK — no global tool pre-installed.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- For Mode B fallback: the repository must be checked out locally (so `dotnet publish` can build from source)

---

## Step 1 — Install the CLI (`install-user.cs`)

```bash
dotnet run scripts/install-user.cs
```

Installs the `docagent` global tool via `dotnet tool install -g DocAgent.McpServer`, then calls `docagent install` to register the MCP server with detected agent hosts on your machine.

### Options

| Flag | Description |
|------|-------------|
| `--version <ver>` | Pin to a specific NuGet package version (e.g. `--version 2.1.0`) |
| `--mode A\|B\|C` | Override hosting mode (default: auto-detected by `docagent install`) |
| `--yes` / `-y` | Non-interactive; falls back to Mode B self-contained publish if `dotnet tool install` fails |
| `--primary <model>` | Passed through to `docagent install` (primary AI model hint) |
| `--secondary <model>` | Passed through to `docagent install` (secondary AI model hint) |

### Hosting modes

| Mode | Description |
|------|-------------|
| **A** | Global tool (`dotnet tool install -g`). Recommended. Updates via `dotnet tool update -g DocAgent.McpServer`. |
| **B** | Self-contained binary published to `~/.docagent/bin/`. Used when NuGet is unavailable or as a `--yes` fallback. |
| **C** | Direct `dotnet run`. No global tool needed. The MCP server is launched directly from the repository. Use in CI or locked-down environments. |

### Mode C note

If you pass `--mode C`, the script exits immediately with instructions. Mode C does not install any binary; agent hosts invoke the server directly via `dotnet run --project`.

---

## Step 2 — Initialise the project (`setup-project.cs`)

```bash
dotnet run scripts/setup-project.cs
```

Equivalent to running `docagent init` directly. Adds a `.docagent/project.json` config file to the current directory and optionally installs git hooks.

If `docagent` is not on PATH (Step 1 not complete), the script prints an error and exits with code 1.

### Options

All flags are passed through to `docagent init`:

| Flag | Description |
|------|-------------|
| `--primary <model>` | Primary AI agent that will query this project (used to name the config entry) |
| `--secondary <model>` | Secondary agents to configure |
| `--hooks` | Install git pre-commit hook during init |
| `--no-hooks` | Skip hook installation |
| `--yes` / `-y` | Accept all defaults non-interactively |

---

## Shortcut: if `docagent` is already installed

Skip the scripts entirely and run the commands directly:

```bash
docagent install   # user-level: register MCP agents
docagent init      # project-level: create .docagent/project.json
```

See `docs/Setup.md` for the full setup guide and `docs/Agents.md` for per-agent configuration details.
