# Agent Configuration Reference

How to register the DocAgent MCP server with each supported AI agent host.

The canonical MCP server entry is:

```json
{
  "command": "docagent",
  "args": [],
  "env": {
    "DOCAGENT_ARTIFACTS_DIR": "<path>"
  }
}
```

Replace `<path>` with a writable directory for snapshot storage (e.g. `~/.docagent/artifacts` or an absolute path). For Mode B, replace `"docagent"` with the full path to the binary. For Mode C, replace `"docagent"` with `"dotnet"` and add `["run", "--project", "<path/to/DocAgent.McpServer>"]` to `"args"`.

`docagent install` writes these entries automatically. This file documents the locations for manual edits or team onboarding.

---

## Claude Desktop

**Config file:**

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

**Structure:**

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/Users/<user>/.docagent/artifacts"
      }
    }
  }
}
```

**Restart requirement:** Quit and relaunch Claude Desktop after editing the config.

---

## Claude Code CLI

**Config file:**

| OS | Path |
|----|------|
| All | `~/.claude/claude_code_config.json` |

**Structure:**

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts"
      }
    }
  }
}
```

**Restart requirement:** The MCP server is started per-session; no restart needed for subsequent `claude` invocations. A running session must be restarted (`/exit` then relaunch) to pick up config changes.

---

## VS Code / GitHub Copilot

**Config file:**

| Scope | Path |
|-------|------|
| User (all workspaces) | `~/.vscode/settings.json` (or via VS Code Settings UI) |
| Workspace | `.vscode/settings.json` in the project root |

**Structure:**

```json
{
  "github.copilot.chat.mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "${userHome}/.docagent/artifacts"
      }
    }
  }
}
```

`${userHome}` is a VS Code variable expanded at runtime.

**Restart requirement:** Reload the VS Code window (`Developer: Reload Window`) after editing `settings.json`.

---

## Cursor

**Config file:**

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Cursor/User/globalStorage/cursor.mcp/config.json` |
| Windows | `%APPDATA%\Cursor\User\globalStorage\cursor.mcp\config.json` |
| Linux | `~/.config/Cursor/User/globalStorage/cursor.mcp/config.json` |

Cursor also supports a per-project `.cursor/mcp.json` file in the workspace root.

**Structure:**

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts"
      }
    }
  }
}
```

**Restart requirement:** Restart Cursor (close and reopen) or use `Cursor: Reload MCP Servers` from the command palette if available.

---

## Windsurf

**Config file:**

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Windsurf/User/globalStorage/windsurf.mcp/config.json` |
| Windows | `%APPDATA%\Windsurf\User\globalStorage\windsurf.mcp\config.json` |
| Linux | `~/.config/Windsurf/User/globalStorage/windsurf.mcp/config.json` |

**Structure:**

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts"
      }
    }
  }
}
```

**Restart requirement:** Reload the Windsurf window.

---

## OpenCode

**Config file:**

| OS | Path |
|----|------|
| All | `~/.config/opencode/config.json` |

**Structure:**

```json
{
  "mcp": {
    "servers": {
      "docagent": {
        "command": "docagent",
        "args": [],
        "env": {
          "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts"
        }
      }
    }
  }
}
```

**Restart requirement:** OpenCode reads config at startup; restart the CLI to pick up changes.

---

## Zed

**Config file:**

| OS | Path |
|----|------|
| macOS | `~/.config/zed/settings.json` |
| Linux | `~/.config/zed/settings.json` |
| Windows | `%APPDATA%\Zed\settings.json` |

**Structure:**

```json
{
  "context_servers": {
    "docagent": {
      "command": {
        "path": "docagent",
        "args": [],
        "env": {
          "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts"
        }
      }
    }
  }
}
```

**Restart requirement:** Zed reloads context servers automatically when `settings.json` is saved. If the server doesn't appear, restart Zed.

---

## Adding a New Agent

To add support for a new AI agent host:

### 1. Add a probe to `AgentDetector.cs`

`AgentDetector.cs` (in `DocAgent.McpServer`) contains the list of known agent hosts. Add a new entry:

```csharp
new AgentProbe(
    name: "MyAgent",
    configPaths: new[]
    {
        // Ordered: Windows, macOS, Linux
        Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "MyAgent", "config.json"),
        Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".config", "myagent", "config.json"),
    },
    configKeyPath: new[] { "mcpServers" },          // JSON path to the servers object
    entryKey: "docagent"                            // Key name for this tool's entry
)
```

The probe is responsible for:
- Checking whether the config file exists (agent is installed)
- Reading and writing the `mcpServers` (or equivalent) object using the canonical entry structure above

### 2. Add an entry to this file

Copy the structure of an existing agent section above, substituting the correct config file paths and JSON shape. Include:
- Config file paths for each OS
- Exact JSON structure with a filled-in example
- Restart requirements
