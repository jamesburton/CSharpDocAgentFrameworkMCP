# MCP Setup & Installation System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add cross-platform install scripts, agent MCP config writing, project-level init, `docagent update`, git hooks, Claude Code skills, and documentation to DocAgentFramework.

**Architecture:** A `CliRunner` intercepts recognised subcommands before ASP.NET Core starts; all CLI logic lives in `src/DocAgent.McpServer/Cli/`; thin `.NET 10 single-file C# scripts` in `scripts/` bootstrap the tool on fresh machines and delegate to the binary.

**Tech Stack:** .NET 10, C# 13, xUnit + FluentAssertions + Moq, `System.Text.Json`, `Microsoft.Extensions.Hosting`, existing `IIngestionService` + `AddDocAgent()` DI extension.

> **⚠️ Tool name note:** The spec uses `docagent-mcp` but the `.csproj` has `ToolCommandName>docagent`. This plan uses `docagent` throughout to match the actual binary. Update the spec if the name needs changing.

---

## File Map

### New source files
| File | Responsibility |
|------|----------------|
| `src/DocAgent.McpServer/Cli/CliRunner.cs` | Routes `args[0]` to CLI handlers before MCP wiring |
| `src/DocAgent.McpServer/Cli/CliServiceProvider.cs` | Builds minimal `IHost` (no MCP/OTEL/health) for CLI commands |
| `src/DocAgent.McpServer/Cli/ProjectConfig.cs` | `docagent.project.json` schema + read/write |
| `src/DocAgent.McpServer/Cli/UserConfig.cs` | `~/.docagent/config.json` schema + read/write |
| `src/DocAgent.McpServer/Cli/UpdateCommand.cs` | Re-ingests all sources from `docagent.project.json` |
| `src/DocAgent.McpServer/Cli/AgentDetector.cs` | Probes for installed agents; returns list with config paths |
| `src/DocAgent.McpServer/Cli/ConfigMerger.cs` | Reads/merges/writes per-agent JSON config files |
| `src/DocAgent.McpServer/Cli/InstallCommand.cs` | User-level: detect agents, confirm, write MCP configs, install skill files |
| `src/DocAgent.McpServer/Cli/InitCommand.cs` | Project-level: interactive/non-interactive project setup |
| `src/DocAgent.McpServer/Cli/HooksCommand.cs` | Installs/removes git hook sentinel blocks |

### Modified source files
| File | Change |
|------|--------|
| `src/DocAgent.McpServer/Program.cs` | Add 2-line routing call at very top, before `WebApplication.CreateBuilder` |

### New test files
| File | Tests |
|------|-------|
| `tests/DocAgent.Tests/Cli/CliRunnerTests.cs` | Routing logic |
| `tests/DocAgent.Tests/Cli/ProjectConfigTests.cs` | JSON round-trip, defaults |
| `tests/DocAgent.Tests/Cli/UserConfigTests.cs` | JSON round-trip, missing file handling |
| `tests/DocAgent.Tests/Cli/UpdateCommandTests.cs` | Reads project config, calls ingestion, outputs JSON |
| `tests/DocAgent.Tests/Cli/AgentDetectorTests.cs` | Detection probes (temp dirs as fake agent installs) |
| `tests/DocAgent.Tests/Cli/ConfigMergerTests.cs` | Merge, conflict, idempotency |
| `tests/DocAgent.Tests/Cli/InstallCommandTests.cs` | End-to-end: detect → confirm → write |
| `tests/DocAgent.Tests/Cli/InitCommandTests.cs` | Files written, idempotency, --non-interactive |
| `tests/DocAgent.Tests/Cli/HooksCommandTests.cs` | Enable/disable, append, sentinel, idempotency |

### New scripts/docs/skills
| File | Purpose |
|------|---------|
| `scripts/install-user.cs` | Bootstrapper: install tool if absent, delegate to `docagent install` |
| `scripts/setup-project.cs` | Bootstrapper: delegate to `docagent init` |
| `scripts/README.md` | Usage instructions |
| `docs/Setup.md` | Quick-start, hosting modes, troubleshooting |
| `docs/Agents.md` | Per-agent config reference + extension guide |
| `docs/GitHooks.md` | Hook setup guide |
| `~/.claude/plugins/docagent/setup-project.skill.md` | Written at install time by `InstallCommand` |
| `~/.claude/plugins/docagent/update.skill.md` | Written at install time by `InstallCommand` |

---

## Chunk 1: Core Types + CLI Routing

### Task 1: `ProjectConfig` — `docagent.project.json` schema

**Files:**
- Create: `src/DocAgent.McpServer/Cli/ProjectConfig.cs`
- Test: `tests/DocAgent.Tests/Cli/ProjectConfigTests.cs`

- [ ] **Step 1.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/ProjectConfigTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class ProjectConfigTests
{
    [Fact]
    public void RoundTrip_DefaultValues_Preserved()
    {
        var config = new ProjectConfig { PrimarySource = "MyApp.sln" };
        var json = JsonSerializer.Serialize(config, ProjectConfig.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectConfig>(json, ProjectConfig.JsonOptions)!;

        deserialized.PrimarySource.Should().Be("MyApp.sln");
        deserialized.ArtifactsDir.Should().Be(".docagent/artifacts");
        deserialized.ExcludeTestFiles.Should().BeTrue();
        deserialized.SecondarySources.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var config = new ProjectConfig
            {
                PrimarySource = "App.sln",
                SecondarySources = [new SecondarySource("dotnet", "../Shared/Shared.sln")]
            };
            var path = Path.Combine(dir, "docagent.project.json");
            await ProjectConfig.SaveAsync(config, path);
            var loaded = await ProjectConfig.LoadAsync(path);

            loaded.Should().NotBeNull();
            loaded!.PrimarySource.Should().Be("App.sln");
            loaded.SecondarySources.Should().HaveCount(1);
            loaded.SecondarySources[0].Type.Should().Be("dotnet");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ReturnsNull()
    {
        var result = await ProjectConfig.LoadAsync("/nonexistent/docagent.project.json");
        result.Should().BeNull();
    }

    [Fact]
    public void SecondarySource_FutureTypes_AcceptedWithoutError()
    {
        var json = """{"version":1,"primarySource":"App.sln","secondarySources":[{"type":"nuget","path":"pkg"}]}""";
        var config = JsonSerializer.Deserialize<ProjectConfig>(json, ProjectConfig.JsonOptions)!;
        config.SecondarySources[0].Type.Should().Be("nuget");
    }
}
```

- [ ] **Step 1.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~ProjectConfigTests" --no-build
```
Expected: compile error (type not found)

- [ ] **Step 1.3: Implement `ProjectConfig`**

```csharp
// src/DocAgent.McpServer/Cli/ProjectConfig.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocAgent.McpServer.Cli;

public sealed class ProjectConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("primarySource")]
    public string PrimarySource { get; set; } = string.Empty;

    [JsonPropertyName("secondarySources")]
    public List<SecondarySource> SecondarySources { get; set; } = [];

    [JsonPropertyName("artifactsDir")]
    public string ArtifactsDir { get; set; } = ".docagent/artifacts";

    [JsonPropertyName("excludeTestFiles")]
    public bool ExcludeTestFiles { get; set; } = true;

    public static async Task<ProjectConfig?> LoadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectConfig>(stream, JsonOptions);
    }

    public static async Task SaveAsync(ProjectConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    /// <summary>Finds docagent.project.json in <paramref name="dir"/> or returns null.</summary>
    public static Task<ProjectConfig?> LoadFromDirAsync(string dir) =>
        LoadAsync(Path.Combine(dir, "docagent.project.json"));
}

public sealed record SecondarySource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("path")] string Path);
```

- [ ] **Step 1.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~ProjectConfigTests"
```
Expected: all pass

- [ ] **Step 1.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/ProjectConfig.cs tests/DocAgent.Tests/Cli/ProjectConfigTests.cs
git commit -m "feat(cli): add ProjectConfig schema and round-trip serialisation"
```

---

### Task 2: `UserConfig` — `~/.docagent/config.json` schema

**Files:**
- Create: `src/DocAgent.McpServer/Cli/UserConfig.cs`
- Test: `tests/DocAgent.Tests/Cli/UserConfigTests.cs`

- [ ] **Step 2.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/UserConfigTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class UserConfigTests
{
    [Fact]
    public async Task SaveAndLoad_ModeA_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var config = new UserConfig
            {
                HostingMode = HostingMode.A,
                ToolVersion = "2.1.0",
                InstalledAt = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero)
            };
            var path = Path.Combine(dir, "config.json");
            await UserConfig.SaveAsync(config, path);
            var loaded = await UserConfig.LoadAsync(path);

            loaded.Should().NotBeNull();
            loaded!.HostingMode.Should().Be(HostingMode.A);
            loaded.ToolVersion.Should().Be("2.1.0");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_Missing_ReturnsNull()
    {
        var result = await UserConfig.LoadAsync("/nonexistent/config.json");
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_ReturnsNull()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{ not valid json }}}");
            var result = await UserConfig.LoadAsync(path);
            result.Should().BeNull();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DefaultDocAgentDir_IsUnderUserHome()
    {
        UserConfig.DefaultDocAgentDir.Should().StartWith(Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile));
    }
}
```

- [ ] **Step 2.2: Run tests — confirm they fail (compile error)**
```
dotnet test --filter "FullyQualifiedName~UserConfigTests" --no-build
```

- [ ] **Step 2.3: Implement `UserConfig`**

```csharp
// src/DocAgent.McpServer/Cli/UserConfig.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocAgent.McpServer.Cli;

public enum HostingMode { A, B, C }

public sealed class UserConfig
{
    public static readonly string DefaultDocAgentDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docagent");

    public static readonly string DefaultConfigPath =
        Path.Combine(DefaultDocAgentDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("hostingMode")]
    public HostingMode HostingMode { get; set; } = HostingMode.A;

    [JsonPropertyName("binaryPath")]
    public string? BinaryPath { get; set; }

    [JsonPropertyName("sourceProjectPath")]
    public string? SourceProjectPath { get; set; }

    [JsonPropertyName("artifactsDir")]
    public string ArtifactsDir { get; set; } =
        Path.Combine(DefaultDocAgentDir, "artifacts");

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("toolVersion")]
    public string? ToolVersion { get; set; }

    public static async Task<UserConfig?> LoadAsync(string? path = null)
    {
        path ??= DefaultConfigPath;
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<UserConfig>(stream, JsonOptions);
        }
        catch (JsonException) { return null; }
    }

    public static async Task SaveAsync(UserConfig config, string? path = null)
    {
        path ??= DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }
}
```

- [ ] **Step 2.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~UserConfigTests"
```

- [ ] **Step 2.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/UserConfig.cs tests/DocAgent.Tests/Cli/UserConfigTests.cs
git commit -m "feat(cli): add UserConfig schema (~/.docagent/config.json)"
```

---

### Task 3: `CliRunner` — routing logic

**Files:**
- Create: `src/DocAgent.McpServer/Cli/CliRunner.cs`
- Test: `tests/DocAgent.Tests/Cli/CliRunnerTests.cs`

- [ ] **Step 3.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/CliRunnerTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class CliRunnerTests
{
    [Theory]
    [InlineData("install")]
    [InlineData("init")]
    [InlineData("update")]
    [InlineData("hooks")]
    public void IsCliCommand_KnownSubcommands_ReturnsTrue(string cmd)
    {
        CliRunner.IsCliCommand([cmd]).Should().BeTrue();
    }

    [Theory]
    [InlineData(new string[0])]
    [InlineData(new[] { "--stdio" })]
    [InlineData(new[] { "--version" })]
    [InlineData(new[] { "unknown-cmd" })]
    public void IsCliCommand_NonSubcommands_ReturnsFalse(string[] args)
    {
        CliRunner.IsCliCommand(args).Should().BeFalse();
    }

    [Fact]
    public void IsCliCommand_SubcommandWithFlags_ReturnsTrue()
    {
        CliRunner.IsCliCommand(["update", "--quiet"]).Should().BeTrue();
    }

    [Fact]
    public void IsCliCommand_NullArgs_ReturnsFalse()
    {
        CliRunner.IsCliCommand(null).Should().BeFalse();
    }
}
```

- [ ] **Step 3.2: Run tests — confirm they fail (compile error)**
```
dotnet test --filter "FullyQualifiedName~CliRunnerTests" --no-build
```

- [ ] **Step 3.3: Implement `CliRunner` (stub — full handler wiring added in later tasks)**

```csharp
// src/DocAgent.McpServer/Cli/CliRunner.cs
namespace DocAgent.McpServer.Cli;

/// <summary>
/// Entry point for CLI subcommands. Called from Program.cs before ASP.NET Core wiring.
/// Returns true if a CLI command was handled (caller should exit); false to continue as MCP server.
/// </summary>
public static class CliRunner
{
    private static readonly HashSet<string> KnownSubcommands =
        new(StringComparer.OrdinalIgnoreCase) { "install", "init", "update", "hooks" };

    public static bool IsCliCommand(string[]? args) =>
        args is { Length: > 0 } && KnownSubcommands.Contains(args[0]);

    /// <summary>Handles the CLI subcommand. Returns the exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        return args[0].ToLowerInvariant() switch
        {
            "install" => await InstallCommand.RunAsync(args[1..]),
            "init"    => await InitCommand.RunAsync(args[1..]),
            "update"  => await UpdateCommand.RunAsync(args[1..]),
            "hooks"   => await HooksCommand.RunAsync(args[1..]),
            _         => 1  // unreachable — guarded by IsCliCommand
        };
    }
}
```

- [ ] **Step 3.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~CliRunnerTests"
```

- [ ] **Step 3.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/CliRunner.cs tests/DocAgent.Tests/Cli/CliRunnerTests.cs
git commit -m "feat(cli): add CliRunner routing — known subcommands intercept before MCP wiring"
```

---

### Task 4: Wire `CliRunner` into `Program.cs`

**Files:**
- Modify: `src/DocAgent.McpServer/Program.cs`

- [ ] **Step 4.1: Add routing check at very top of `Program.cs` (before `WebApplication.CreateBuilder`)**

In `src/DocAgent.McpServer/Program.cs`, add these lines immediately after the `using` declarations, as the first executable code:

```csharp
// CLI subcommand routing — must happen before WebApplication.CreateBuilder
if (DocAgent.McpServer.Cli.CliRunner.IsCliCommand(args))
{
    var exitCode = await DocAgent.McpServer.Cli.CliRunner.RunAsync(args);
    return exitCode;
}
```

The full top of the file becomes:
```csharp
using DocAgent.Core;
// ... existing usings unchanged ...

// CLI subcommand routing — must happen before WebApplication.CreateBuilder
if (DocAgent.McpServer.Cli.CliRunner.IsCliCommand(args))
{
    var exitCode = await DocAgent.McpServer.Cli.CliRunner.RunAsync(args);
    return exitCode;
}

var builder = WebApplication.CreateBuilder(args);
// ... rest of file unchanged ...
```

- [ ] **Step 4.2: Build to confirm no errors**
```
dotnet build src/DocAgentFramework.sln
```
Expected: Build succeeded (note: CliRunner.RunAsync references commands not yet implemented — add stub classes for InitCommand, HooksCommand now)

- [ ] **Step 4.3: Add temporary stubs for unimplemented commands** (so the build passes; full implementations follow in later tasks)

```csharp
// src/DocAgent.McpServer/Cli/InstallCommand.cs (stub)
namespace DocAgent.McpServer.Cli;
public static class InstallCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("install command not yet implemented");
        return Task.FromResult(1);
    }
}
```

```csharp
// src/DocAgent.McpServer/Cli/InitCommand.cs (stub)
namespace DocAgent.McpServer.Cli;
public static class InitCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("init command not yet implemented");
        return Task.FromResult(1);
    }
}
```

```csharp
// src/DocAgent.McpServer/Cli/UpdateCommand.cs (stub)
namespace DocAgent.McpServer.Cli;
public static class UpdateCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("update command not yet implemented");
        return Task.FromResult(1);
    }
}
```

```csharp
// src/DocAgent.McpServer/Cli/HooksCommand.cs (stub)
namespace DocAgent.McpServer.Cli;
public static class HooksCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("hooks command not yet implemented");
        return Task.FromResult(1);
    }
}
```

- [ ] **Step 4.4: Build and run all tests**
```
dotnet build src/DocAgentFramework.sln && dotnet test
```
Expected: Build succeeded, all existing tests pass

- [ ] **Step 4.5: Commit**
```bash
git add src/DocAgent.McpServer/Program.cs \
        src/DocAgent.McpServer/Cli/InstallCommand.cs \
        src/DocAgent.McpServer/Cli/InitCommand.cs \
        src/DocAgent.McpServer/Cli/UpdateCommand.cs \
        src/DocAgent.McpServer/Cli/HooksCommand.cs
git commit -m "feat(cli): wire CliRunner into Program.cs; add command stubs"
```

---

## Chunk 2: UpdateCommand

### Task 5: `CliServiceProvider` — minimal host for CLI commands

**Files:**
- Create: `src/DocAgent.McpServer/Cli/CliServiceProvider.cs`
- Test: `tests/DocAgent.Tests/Cli/CliServiceProviderTests.cs`

> **Ordering rule:** `services.Configure<DocAgentServerOptions>(...)` must be registered **before** `services.AddDocAgent()` is called, because `AddDocAgent()` captures a closure that resolves `DocAgentServerOptions` lazily on first service resolution. `ArtifactsDir` must be non-empty by that point or `Directory.CreateDirectory("")` will throw.

> **Do NOT call `host.StartAsync()`** — this would trigger `NodeAvailabilityValidator` (registered as a hosted service by `AddDocAgent()`), which spawns `node --version`. CLI commands must not have Node.js as a side-effect.

- [ ] **Step 5.1: Write smoke test**

```csharp
// tests/DocAgent.Tests/Cli/CliServiceProviderTests.cs
using DocAgent.McpServer.Cli;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class CliServiceProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    public CliServiceProviderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Build_ResolvesIIngestionService_WithoutThrowing()
    {
        var sp = CliServiceProvider.Build(_dir);
        var svc = sp.GetRequiredService<IIngestionService>();
        svc.Should().NotBeNull();
    }
}
```

- [ ] **Step 5.2: Run test — confirm it fails (type not found)**
```
dotnet test --filter "FullyQualifiedName~CliServiceProviderTests" --no-build
```

- [ ] **Step 5.3: Implement `CliServiceProvider`**

```csharp
// src/DocAgent.McpServer/Cli/CliServiceProvider.cs
using DocAgent.McpServer.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocAgent.McpServer.Cli;

/// <summary>
/// Builds a minimal <see cref="IServiceProvider"/> for CLI commands.
/// IMPORTANT: Do NOT call host.StartAsync() — this would trigger NodeAvailabilityValidator.
/// IMPORTANT: Configure&lt;DocAgentServerOptions&gt; must be registered before AddDocAgent().
/// </summary>
public static class CliServiceProvider
{
    public static IServiceProvider Build(string artifactsDir)
    {
        var absArtifactsDir = Path.GetFullPath(artifactsDir);
        Directory.CreateDirectory(absArtifactsDir);

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Warning);
            })
            .ConfigureServices((_, services) =>
            {
                // Configure BEFORE AddDocAgent() — AddDocAgent captures ArtifactsDir lazily
                services.Configure<DocAgentServerOptions>(opts =>
                {
                    opts.ArtifactsDir = absArtifactsDir;
                    opts.ExcludeTestFiles = true;
                });
                services.AddDocAgent();
            })
            .Build();

        // Do NOT call host.StartAsync() — avoids NodeAvailabilityValidator side-effects.
        return host.Services;
    }
}
```

- [ ] **Step 5.4: Run test — confirm it passes**
```
dotnet test --filter "FullyQualifiedName~CliServiceProviderTests"
```

- [ ] **Step 5.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/CliServiceProvider.cs \
        tests/DocAgent.Tests/Cli/CliServiceProviderTests.cs
git commit -m "feat(cli): add CliServiceProvider — minimal DI host; Configure before AddDocAgent"
```

---

### Task 6: `UpdateCommand` — full implementation

**Files:**
- Modify: `src/DocAgent.McpServer/Cli/UpdateCommand.cs` (replace stub)
- Test: `tests/DocAgent.Tests/Cli/UpdateCommandTests.cs`

- [ ] **Step 6.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/UpdateCommandTests.cs
using DocAgent.McpServer.Cli;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class UpdateCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public UpdateCommandTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ProjectJsonPath => Path.Combine(_dir, "docagent.project.json");

    private async Task WriteProjectConfig(string primarySource)
    {
        var config = new ProjectConfig
        {
            PrimarySource = primarySource,
            ArtifactsDir = Path.Combine(_dir, "artifacts")
        };
        await ProjectConfig.SaveAsync(config, ProjectJsonPath);
    }

    [Fact]
    public async Task RunAsync_NoProjectJson_ReturnsNonZero()
    {
        var exitCode = await UpdateCommand.RunAsync([], workingDir: _dir);
        exitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task RunAsync_WithProjectJson_CallsIngestionAndOutputsJson()
    {
        var slnPath = Path.Combine(_dir, "MyApp.sln");
        await File.WriteAllTextAsync(slnPath, ""); // fake sln
        await WriteProjectConfig(slnPath);

        var mockIngestion = new Mock<IIngestionService>();
        // Must match all 7 params — Moq does not honour C# default parameter values.
        // Use explicit cast (Func<int,int,string,Task>?)null to avoid CS8625 under
        // TreatWarningsAsErrors=true with nullable reference types enabled.
        mockIngestion
            .Setup(s => s.IngestAsync(
                It.IsAny<string>(),
                (string?)null,                           // includeGlob
                (string?)null,                           // excludeGlob
                false,                                   // forceReindex
                (Func<int, int, string, Task>?)null,     // reportProgress — UpdateCommand passes null
                It.IsAny<CancellationToken>(),
                false))                                  // forceFullReingestion — must be explicit
            .ReturnsAsync(new IngestionResult("hash1", 100, 1,
                TimeSpan.FromSeconds(2), [], null));

        var stdoutLines = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            [], workingDir: _dir,
            ingestionService: mockIngestion.Object,
            writeOutput: line => stdoutLines.Add(line));

        exitCode.Should().Be(0);
        stdoutLines.Should().ContainSingle();
        var result = JsonSerializer.Deserialize<JsonElement>(stdoutLines[0]);
        result.GetProperty("status").GetString().Should().Be("ok");
        result.GetProperty("symbolCount").GetInt32().Should().Be(100);
        result.GetProperty("snapshotHash").GetString().Should().Be("hash1");
    }

    [Fact]
    public async Task RunAsync_QuietFlag_StillOutputsJsonToStdout()
    {
        // --quiet suppresses progress on stderr but must NOT suppress the JSON result on stdout
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");
        await WriteProjectConfig(slnPath);

        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion
            .Setup(s => s.IngestAsync(
                It.IsAny<string>(),
                (string?)null, (string?)null, false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new IngestionResult("h2", 0, 1, TimeSpan.Zero, []));

        var stdoutLines = new List<string>();
        var exitCode = await UpdateCommand.RunAsync(
            ["--quiet"], workingDir: _dir,
            ingestionService: mockIngestion.Object,
            writeOutput: line => stdoutLines.Add(line));

        exitCode.Should().Be(0);
        // JSON summary must still be emitted even with --quiet
        stdoutLines.Should().ContainSingle("--quiet must not suppress the JSON summary");
        var result = JsonSerializer.Deserialize<JsonElement>(stdoutLines[0]);
        result.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task RunAsync_MissingSourceFile_SkipsWithWarningAndContinues()
    {
        // Secondary source that doesn't exist should be skipped with a warning, not a crash
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");
        var config = new ProjectConfig
        {
            PrimarySource = slnPath,
            SecondarySources = [new SecondarySource("dotnet", "/nonexistent/Lib.sln")],
            ArtifactsDir = Path.Combine(_dir, "artifacts")
        };
        await ProjectConfig.SaveAsync(config, Path.Combine(_dir, "docagent.project.json"));

        var mockIngestion = new Mock<IIngestionService>();
        mockIngestion
            .Setup(s => s.IngestAsync(
                slnPath,
                (string?)null, (string?)null, false,
                (Func<int, int, string, Task>?)null,
                It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new IngestionResult("h3", 50, 1, TimeSpan.Zero, []));

        var exitCode = await UpdateCommand.RunAsync(
            [], workingDir: _dir,
            ingestionService: mockIngestion.Object,
            writeOutput: _ => { });

        exitCode.Should().Be(0);
        // Only primary was ingested — nonexistent secondary was skipped
        mockIngestion.Verify(s => s.IngestAsync(
            slnPath,
            (string?)null, (string?)null, false,
            (Func<int, int, string, Task>?)null,
            It.IsAny<CancellationToken>(), false), Times.Once);
        mockIngestion.Verify(s => s.IngestAsync(
            "/nonexistent/Lib.sln",
            (string?)null, (string?)null, false,
            (Func<int, int, string, Task>?)null,
            It.IsAny<CancellationToken>(), false), Times.Never);
    }
}
```

- [ ] **Step 6.2: Run tests — confirm they fail (method signature mismatch with stub)**
```
dotnet test --filter "FullyQualifiedName~UpdateCommandTests" --no-build
```

- [ ] **Step 6.3: Implement `UpdateCommand`**

```csharp
// src/DocAgent.McpServer/Cli/UpdateCommand.cs
using DocAgent.McpServer.Ingestion;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace DocAgent.McpServer.Cli;

public static class UpdateCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        string? workingDir = null,
        IIngestionService? ingestionService = null,
        Action<string>? writeOutput = null)
    {
        workingDir ??= Directory.GetCurrentDirectory();
        writeOutput ??= Console.WriteLine;
        bool quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);

        var projectConfig = await ProjectConfig.LoadFromDirAsync(workingDir);
        if (projectConfig is null)
        {
            Console.Error.WriteLine(
                "docagent.project.json not found in current directory. " +
                "Run 'docagent init' to set up this project.");
            return 1;
        }

        var artifactsDir = Path.IsPathRooted(projectConfig.ArtifactsDir)
            ? projectConfig.ArtifactsDir
            : Path.GetFullPath(Path.Combine(workingDir, projectConfig.ArtifactsDir));

        // Pre-flight: validate artifacts dir is usable before building DI
        try { Directory.CreateDirectory(artifactsDir); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Cannot create artifacts directory '{artifactsDir}': {ex.Message}");
            return 1;
        }

        if (ingestionService is null)
        {
            // Real path: register MSBuild and build DI
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
            var sp = CliServiceProvider.Build(artifactsDir);
            ingestionService = sp.GetRequiredService<IIngestionService>();
        }

        var sources = new List<string> { projectConfig.PrimarySource };
        sources.AddRange(projectConfig.SecondarySources
            .Where(s => s.Type is "dotnet" or "typescript")
            .Select(s => s.Path));

        int totalSymbols = 0;
        int totalProjects = 0;
        string lastHash = string.Empty;
        var startTime = DateTimeOffset.UtcNow;

        foreach (var sourcePath in sources)
        {
            var absPath = Path.IsPathRooted(sourcePath)
                ? sourcePath
                : Path.GetFullPath(Path.Combine(workingDir, sourcePath));

            if (!File.Exists(absPath) && !Directory.Exists(absPath))
            {
                Console.Error.WriteLine($"Warning: source not found, skipping: {absPath}");
                continue;
            }

            if (!quiet)
                Console.Error.WriteLine($"Ingesting: {absPath}");

            var result = await ingestionService.IngestAsync(
                absPath,
                includeGlob: null,
                excludeGlob: null,
                forceReindex: false,
                reportProgress: null,
                cancellationToken: CancellationToken.None,
                forceFullReingestion: false);

            totalSymbols += result.SymbolCount;
            totalProjects += result.ProjectCount;
            lastHash = result.SnapshotId;

            if (result.IndexError is not null)
                Console.Error.WriteLine($"Warning: {result.IndexError}");
        }

        var duration = DateTimeOffset.UtcNow - startTime;
        var summary = new
        {
            status = "ok",
            projectsIngested = totalProjects,
            symbolCount = totalSymbols,
            durationMs = (long)duration.TotalMilliseconds,
            snapshotHash = lastHash
        };

        writeOutput(JsonSerializer.Serialize(summary));
        return 0;
    }
}
```

- [ ] **Step 6.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~UpdateCommandTests"
```

- [ ] **Step 6.5: Run full test suite**
```
dotnet test
```
Expected: all pass

- [ ] **Step 6.6: Commit**
```bash
git add src/DocAgent.McpServer/Cli/UpdateCommand.cs \
        src/DocAgent.McpServer/Cli/CliServiceProvider.cs \
        tests/DocAgent.Tests/Cli/UpdateCommandTests.cs
git commit -m "feat(cli): implement UpdateCommand — re-ingests from docagent.project.json"
```

---

## Chunk 3: Agent Detection + Config Merge

### Task 7: `AgentDetector` — probe for installed agents

**Files:**
- Create: `src/DocAgent.McpServer/Cli/AgentDetector.cs`
- Test: `tests/DocAgent.Tests/Cli/AgentDetectorTests.cs`

- [ ] **Step 7.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/AgentDetectorTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class AgentDetectorTests : IDisposable
{
    private readonly string _fakeHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public AgentDetectorTests() => Directory.CreateDirectory(_fakeHome);
    public void Dispose() => Directory.Delete(_fakeHome, recursive: true);

    [Fact]
    public void Detect_CursorDirExists_FindsCursor()
    {
        var cursorDir = Path.Combine(_fakeHome, ".cursor");
        Directory.CreateDirectory(cursorDir);

        var detector = new AgentDetector(homeDir: _fakeHome);
        var agents = detector.Detect();

        agents.Should().Contain(a => a.AgentId == "cursor");
        var cursor = agents.First(a => a.AgentId == "cursor");
        cursor.ConfigPath.Should().Be(Path.Combine(cursorDir, "mcp.json"));
        cursor.JsonKeyPath.Should().Be("mcpServers");
    }

    [Fact]
    public void Detect_NoDirsExist_ReturnsEmptyList()
    {
        var detector = new AgentDetector(homeDir: _fakeHome);
        var agents = detector.Detect();
        agents.Should().BeEmpty();
    }

    [Fact]
    public void Detect_WindsurfDirExists_FindsWindsurf()
    {
        var windsurfDir = Path.Combine(_fakeHome, ".codeium", "windsurf");
        Directory.CreateDirectory(windsurfDir);

        var detector = new AgentDetector(homeDir: _fakeHome);
        var agents = detector.Detect();

        agents.Should().Contain(a => a.AgentId == "windsurf");
    }

    [Fact]
    public void Detect_ClaudeSettingsJsonExists_FindsClaudeCodeCli()
    {
        // Claude Code CLI config lives in ~/.claude/settings.json (not ~/.claude.json)
        var claudeDir = Path.Combine(_fakeHome, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "settings.json"), "{}");

        var detector = new AgentDetector(homeDir: _fakeHome);
        var agents = detector.Detect();

        agents.Should().Contain(a => a.AgentId == "claude-code-cli");
        var cli = agents.First(a => a.AgentId == "claude-code-cli");
        cli.ConfigPath.Should().Be(Path.Combine(claudeDir, "settings.json"));
        cli.JsonKeyPath.Should().Be("mcpServers");
    }

    [Fact]
    public void AgentInfo_HasRequiredFields()
    {
        var windsurfDir = Path.Combine(_fakeHome, ".codeium", "windsurf");
        Directory.CreateDirectory(windsurfDir);

        var detector = new AgentDetector(homeDir: _fakeHome);
        var agent = detector.Detect().First(a => a.AgentId == "windsurf");

        agent.DisplayName.Should().NotBeNullOrEmpty();
        agent.ConfigPath.Should().NotBeNullOrEmpty();
        agent.JsonKeyPath.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 7.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~AgentDetectorTests" --no-build
```

- [ ] **Step 7.3: Implement `AgentDetector`**

```csharp
// src/DocAgent.McpServer/Cli/AgentDetector.cs
namespace DocAgent.McpServer.Cli;

public sealed record AgentInfo(
    string AgentId,
    string DisplayName,
    string ConfigPath,
    string JsonKeyPath);

/// <summary>
/// Probes the local machine for installed AI agent tools and returns config locations.
/// All detection is file-system based (no registry, no process enumeration).
/// </summary>
public sealed class AgentDetector
{
    private readonly string _home;

    public AgentDetector(string? homeDir = null)
    {
        _home = homeDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public IReadOnlyList<AgentInfo> Detect()
    {
        var results = new List<AgentInfo>();
        foreach (var probe in AllProbes())
        {
            var info = probe();
            if (info is not null)
                results.Add(info);
        }
        return results;
    }

    private IEnumerable<Func<AgentInfo?>> AllProbes() =>
    [
        ProbeClaudeDesktop,
        ProbeClaudeCodeCli,
        ProbeCursor,
        ProbeWindsurf,
        ProbeOpenCode,
        ProbeZed
        // VS Code is handled separately — it writes to the project directory, not user home
    ];

    private AgentInfo? ProbeClaudeDesktop()
    {
        string configDir;
        if (OperatingSystem.IsWindows())
            configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
        else if (OperatingSystem.IsMacOS())
            configDir = Path.Combine(_home, "Library", "Application Support", "Claude");
        else
            configDir = Path.Combine(_home, ".config", "claude-desktop");

        if (!Directory.Exists(configDir)) return null;

        return new AgentInfo(
            "claude-desktop",
            "Claude Desktop",
            Path.Combine(configDir, "claude_desktop_config.json"),
            "mcpServers");
    }

    private AgentInfo? ProbeClaudeCodeCli()
    {
        // Claude Code CLI stores MCP server config in ~/.claude/settings.json
        // (not ~/.claude.json which is a startup/telemetry metadata file)
        var claudeDir = Path.Combine(_home, ".claude");
        var settingsPath = Path.Combine(claudeDir, "settings.json");
        if (!File.Exists(settingsPath)) return null;

        return new AgentInfo(
            "claude-code-cli",
            "Claude Code CLI",
            settingsPath,
            "mcpServers");
    }

    private AgentInfo? ProbeCursor()
    {
        var cursorDir = Path.Combine(_home, ".cursor");
        if (!Directory.Exists(cursorDir))
        {
            // Windows: %LOCALAPPDATA%\Programs\cursor
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(Path.Combine(localAppData, "Programs", "cursor")))
                return null;
            cursorDir = Path.Combine(_home, ".cursor");
            Directory.CreateDirectory(cursorDir); // create config dir if not there
        }

        return new AgentInfo(
            "cursor",
            "Cursor",
            Path.Combine(cursorDir, "mcp.json"),
            "mcpServers");
    }

    private AgentInfo? ProbeWindsurf()
    {
        var windsurfDir = Path.Combine(_home, ".codeium", "windsurf");
        if (!Directory.Exists(windsurfDir)) return null;

        return new AgentInfo(
            "windsurf",
            "Windsurf",
            Path.Combine(windsurfDir, "mcp_config.json"),
            "mcpServers");
    }

    private AgentInfo? ProbeOpenCode()
    {
        var configDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".config", "opencode")
            : Path.Combine(_home, ".config", "opencode");

        if (!Directory.Exists(configDir)) return null;

        return new AgentInfo(
            "opencode",
            "OpenCode",
            Path.Combine(configDir, "config.json"),
            "mcp.servers");
    }

    private AgentInfo? ProbeZed()
    {
        var zedDir = OperatingSystem.IsMacOS()
            ? Path.Combine(_home, "Library", "Application Support", "Zed")
            : Path.Combine(_home, ".config", "zed");

        if (!Directory.Exists(zedDir)) return null;

        return new AgentInfo(
            "zed",
            "Zed",
            Path.Combine(zedDir, "settings.json"),
            "context_servers");
    }
}
```

- [ ] **Step 7.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~AgentDetectorTests"
```

- [ ] **Step 7.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/AgentDetector.cs \
        tests/DocAgent.Tests/Cli/AgentDetectorTests.cs
git commit -m "feat(cli): add AgentDetector — file-system probes for 6 agent tools"
```

---

### Task 8: `ConfigMerger` — JSON config read/merge/write

**Files:**
- Create: `src/DocAgent.McpServer/Cli/ConfigMerger.cs`
- Test: `tests/DocAgent.Tests/Cli/ConfigMergerTests.cs`

- [ ] **Step 8.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/ConfigMergerTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class ConfigMergerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ConfigMergerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath(string name) => Path.Combine(_dir, name);

    [Fact]
    public async Task Merge_NewFile_CreatesWithDocagentEntry()
    {
        var path = FilePath("new.json");
        var entry = BuildEntry(HostingMode.A);

        var conflict = await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);

        conflict.Should().Be(MergeConflict.None);
        var content = await File.ReadAllTextAsync(path);
        var doc = JsonNode.Parse(content)!;
        doc["mcpServers"]!["docagent"]!["command"]!.GetValue<string>().Should().Be("docagent");
    }

    [Fact]
    public async Task Merge_ExistingFile_PreservesOtherServers()
    {
        var path = FilePath("existing.json");
        await File.WriteAllTextAsync(path, """
            {"mcpServers": {"other-tool": {"command": "other"}}}
            """);

        await ConfigMerger.MergeAsync(path, "mcpServers", BuildEntry(HostingMode.A), nonInteractive: true);

        var doc = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        doc["mcpServers"]!["other-tool"]!["command"]!.GetValue<string>().Should().Be("other");
        doc["mcpServers"]!["docagent"].Should().NotBeNull();
    }

    [Fact]
    public async Task Merge_Idempotent_NoChangePreservesExactBytes()
    {
        // This tests ConfigMerger JSON file idempotency — NOT IngestionService.
        // ConfigMerger.MergeAsync has an explicit no-op guard:
        //   if (existingEntry.ToJsonString() == newEntryJson) return MergeConflict.None;
        // which skips File.WriteAllTextAsync entirely on the second call.
        // The bytes comparison is therefore valid and deterministic.
        var path = FilePath("idempotent.json");
        var entry = BuildEntry(HostingMode.A);

        await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);
        var bytesAfterFirst = await File.ReadAllBytesAsync(path);

        await ConfigMerger.MergeAsync(path, "mcpServers", entry, nonInteractive: true);
        var bytesAfterSecond = await File.ReadAllBytesAsync(path);

        bytesAfterSecond.Should().Equal(bytesAfterFirst,
            "a no-op merge must not rewrite the file (bytes must be identical)");
    }

    [Fact]
    public async Task Merge_Conflict_NonInteractiveWithoutYes_SkipsAndReturnsConflict()
    {
        var path = FilePath("conflict.json");
        await File.WriteAllTextAsync(path, """
            {"mcpServers": {"docagent": {"command": "old-command"}}}
            """);

        var conflict = await ConfigMerger.MergeAsync(
            path, "mcpServers", BuildEntry(HostingMode.A),
            nonInteractive: true, yesFlag: false);

        conflict.Should().Be(MergeConflict.Skipped);
        // File should be unchanged
        var doc = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        doc["mcpServers"]!["docagent"]!["command"]!.GetValue<string>().Should().Be("old-command");
    }

    [Fact]
    public async Task Merge_Conflict_NonInteractiveYes_Overwrites()
    {
        var path = FilePath("overwrite.json");
        await File.WriteAllTextAsync(path, """
            {"mcpServers": {"docagent": {"command": "old-command"}}}
            """);

        await ConfigMerger.MergeAsync(
            path, "mcpServers", BuildEntry(HostingMode.A),
            nonInteractive: true, yesFlag: true);

        var doc = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        doc["mcpServers"]!["docagent"]!["command"]!.GetValue<string>().Should().Be("docagent");
    }

    private static JsonObject BuildEntry(HostingMode mode)
    {
        return ConfigMerger.BuildMcpEntry(mode, binaryPath: null, sourcePath: null,
            artifactsDir: "~/.docagent/artifacts");
    }
}
```

- [ ] **Step 8.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~ConfigMergerTests" --no-build
```

- [ ] **Step 8.3: Implement `ConfigMerger`**

```csharp
// src/DocAgent.McpServer/Cli/ConfigMerger.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DocAgent.McpServer.Cli;

public enum MergeConflict { None, Skipped, Overwritten }

public static class ConfigMerger
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads <paramref name="configPath"/>, merges the docagent entry under <paramref name="keyPath"/>,
    /// and writes back. Returns the conflict status.
    /// </summary>
    public static async Task<MergeConflict> MergeAsync(
        string configPath,
        string keyPath,
        JsonObject mcpEntry,
        bool nonInteractive,
        bool yesFlag = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        JsonNode root;
        if (File.Exists(configPath))
        {
            try
            {
                var text = await File.ReadAllTextAsync(configPath);
                root = JsonNode.Parse(text) ?? new JsonObject();
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"Warning: malformed JSON in {configPath} — skipping.");
                return MergeConflict.Skipped;
            }
        }
        else
        {
            root = new JsonObject();
        }

        // Navigate/create the key path (supports "a" or "a.b")
        var parts = keyPath.Split('.');
        var parent = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parent is not JsonObject obj) return MergeConflict.Skipped;
            if (obj[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                obj[parts[i]] = child;
            }
            parent = child;
        }

        if (parent is not JsonObject servers) return MergeConflict.Skipped;
        var lastKey = parts[^1];

        // Ensure the target key exists as an object
        if (servers[lastKey] is not JsonObject serverMap)
        {
            serverMap = new JsonObject();
            servers[lastKey] = serverMap;
        }

        // Check conflict
        var newEntryJson = mcpEntry.ToJsonString();
        var existingEntry = serverMap["docagent"];
        if (existingEntry is not null)
        {
            var existingJson = existingEntry.ToJsonString();
            if (existingJson == newEntryJson) return MergeConflict.None; // idempotent no-op

            // Conflict: different value
            if (nonInteractive && !yesFlag)
            {
                Console.Error.WriteLine(
                    $"Warning: existing 'docagent' entry in {configPath} differs — skipping " +
                    "(run with --yes to overwrite).");
                return MergeConflict.Skipped;
            }

            if (!nonInteractive)
            {
                Console.Error.Write($"Conflict in {configPath}. Overwrite existing docagent entry? [O/s]: ");
                var answer = Console.ReadLine() ?? "o";
                if (answer.StartsWith('s', StringComparison.OrdinalIgnoreCase))
                    return MergeConflict.Skipped;
            }
        }

        serverMap["docagent"] = mcpEntry.DeepClone();
        await File.WriteAllTextAsync(configPath,
            root.ToJsonString(WriteOptions));

        return existingEntry is null ? MergeConflict.None : MergeConflict.Overwritten;
    }

    /// <summary>Builds the docagent MCP server JSON entry for the given hosting mode.</summary>
    public static JsonObject BuildMcpEntry(
        HostingMode mode, string? binaryPath, string? sourcePath, string artifactsDir)
    {
        var absArtifacts = artifactsDir.Replace("~",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return mode switch
        {
            HostingMode.A => new JsonObject
            {
                ["command"] = "docagent",
                ["args"] = new JsonArray(),
                ["env"] = new JsonObject
                {
                    ["DOCAGENT_ARTIFACTS_DIR"] = absArtifacts
                }
            },
            HostingMode.B => new JsonObject
            {
                ["command"] = binaryPath ?? Path.Combine(UserConfig.DefaultDocAgentDir,
                    "bin", OperatingSystem.IsWindows() ? "docagent.exe" : "docagent"),
                ["args"] = new JsonArray(),
                ["env"] = new JsonObject { ["DOCAGENT_ARTIFACTS_DIR"] = absArtifacts }
            },
            HostingMode.C => new JsonObject
            {
                ["command"] = "dotnet",
                ["args"] = new JsonArray("run", "--project", sourcePath ?? string.Empty),
                ["env"] = new JsonObject { ["DOCAGENT_ARTIFACTS_DIR"] = absArtifacts }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
```

- [ ] **Step 8.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~ConfigMergerTests"
```

- [ ] **Step 8.5: Commit**
```bash
git add src/DocAgent.McpServer/Cli/ConfigMerger.cs \
        tests/DocAgent.Tests/Cli/ConfigMergerTests.cs
git commit -m "feat(cli): add ConfigMerger — idempotent JSON config merge with conflict handling"
```

---

## Chunk 4: InstallCommand

### Task 9: `InstallCommand` — full implementation

**Files:**
- Modify: `src/DocAgent.McpServer/Cli/InstallCommand.cs` (replace stub)
- Test: `tests/DocAgent.Tests/Cli/InstallCommandTests.cs`

- [ ] **Step 9.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/InstallCommandTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using System.Text.Json.Nodes;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class InstallCommandTests : IDisposable
{
    private readonly string _fakeHome =
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public InstallCommandTests() => Directory.CreateDirectory(_fakeHome);
    public void Dispose() => Directory.Delete(_fakeHome, recursive: true);

    [Fact]
    public async Task RunAsync_YesFlag_WritesCursorConfigWhenDirExists()
    {
        // Arrange: create fake cursor dir to trigger detection
        var cursorDir = Path.Combine(_fakeHome, ".cursor");
        Directory.CreateDirectory(cursorDir);

        // Act
        var exitCode = await InstallCommand.RunAsync(
            ["--yes", "--mode", "A"],
            homeDir: _fakeHome,
            userConfigPath: Path.Combine(_fakeHome, "config.json"));

        // Assert
        exitCode.Should().Be(0);
        var configPath = Path.Combine(cursorDir, "mcp.json");
        File.Exists(configPath).Should().BeTrue();
        var doc = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        doc["mcpServers"]!["docagent"].Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_NoAgentsDetected_ExitsZeroWithMessage()
    {
        // No agent dirs in fake home
        var output = new List<string>();
        var exitCode = await InstallCommand.RunAsync(
            ["--yes"],
            homeDir: _fakeHome,
            userConfigPath: Path.Combine(_fakeHome, "config.json"),
            writeOutput: line => output.Add(line));

        exitCode.Should().Be(0);
        output.Should().Contain(l => l.Contains("No supported agents detected"));
    }

    [Fact]
    public async Task RunAsync_WritesUserConfig()
    {
        var userConfigPath = Path.Combine(_fakeHome, "config.json");

        await InstallCommand.RunAsync(
            ["--yes", "--mode", "A"],
            homeDir: _fakeHome,
            userConfigPath: userConfigPath);

        File.Exists(userConfigPath).Should().BeTrue();
        var config = await UserConfig.LoadAsync(userConfigPath);
        config.Should().NotBeNull();
        config!.HostingMode.Should().Be(HostingMode.A);
    }

    [Fact]
    public async Task RunAsync_InstallsClaudeSkillFiles()
    {
        var skillDir = Path.Combine(_fakeHome, ".claude", "plugins", "docagent");

        await InstallCommand.RunAsync(
            ["--yes"],
            homeDir: _fakeHome,
            userConfigPath: Path.Combine(_fakeHome, "config.json"),
            claudePluginsDir: skillDir);

        File.Exists(Path.Combine(skillDir, "setup-project.skill.md")).Should().BeTrue();
        File.Exists(Path.Combine(skillDir, "update.skill.md")).Should().BeTrue();
    }
}
```

- [ ] **Step 9.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~InstallCommandTests" --no-build
```

- [ ] **Step 9.3: Implement `InstallCommand`**

```csharp
// src/DocAgent.McpServer/Cli/InstallCommand.cs
namespace DocAgent.McpServer.Cli;

public static class InstallCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        string? homeDir = null,
        string? userConfigPath = null,
        string? claudePluginsDir = null,
        Action<string>? writeOutput = null)
    {
        writeOutput ??= Console.WriteLine;
        homeDir ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userConfigPath ??= UserConfig.DefaultConfigPath;

        bool yesFlag = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);
        var mode = ParseMode(args) ?? HostingMode.A;
        claudePluginsDir ??= Path.Combine(homeDir, ".claude", "plugins", "docagent");

        var artifactsDir = Path.Combine(homeDir, ".docagent", "artifacts");

        // Detect agents
        var detector = new AgentDetector(homeDir);
        var agents = detector.Detect();

        if (agents.Count == 0)
        {
            writeOutput("No supported agents detected on this machine.");
            writeOutput("See docs/Agents.md for manual configuration instructions.");
            await SaveUserConfig(userConfigPath, mode);
            await WriteSkillFiles(claudePluginsDir);
            return 0;
        }

        writeOutput("Detected agents:");
        foreach (var agent in agents)
            writeOutput($"  [✓] {agent.DisplayName,-20} → {agent.ConfigPath}");

        if (!yesFlag)
        {
            Console.Write("\nProceed? [Y/n]: ");
            var answer = Console.ReadLine() ?? "y";
            if (answer.StartsWith('n', StringComparison.OrdinalIgnoreCase))
            {
                writeOutput("Cancelled.");
                return 0;
            }
        }

        var mcpEntry = ConfigMerger.BuildMcpEntry(mode, null, null, artifactsDir);
        int written = 0;
        foreach (var agent in agents)
        {
            var conflict = await ConfigMerger.MergeAsync(
                agent.ConfigPath, agent.JsonKeyPath, mcpEntry,
                nonInteractive: yesFlag, yesFlag: yesFlag);

            if (conflict != MergeConflict.Skipped)
            {
                writeOutput($"  Wrote: {agent.ConfigPath}");
                written++;
            }
        }

        await SaveUserConfig(userConfigPath, mode);
        await WriteSkillFiles(claudePluginsDir);

        writeOutput($"\nDone. Configured {written}/{agents.Count} agent(s).");
        writeOutput("Run 'docagent init' in a project directory to set up project-level config.");
        return 0;
    }

    private static HostingMode? ParseMode(string[] args)
    {
        var idx = Array.FindIndex(args, a =>
            a.Equals("--mode", StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Length) return null;
        return Enum.TryParse<HostingMode>(args[idx + 1], ignoreCase: true, out var mode)
            ? mode : null;
    }

    private static async Task SaveUserConfig(string path, HostingMode mode)
    {
        var config = await UserConfig.LoadAsync(path) ?? new UserConfig();
        config.HostingMode = mode;
        config.InstalledAt = DateTimeOffset.UtcNow;
        await UserConfig.SaveAsync(config, path);
    }

    private static async Task WriteSkillFiles(string skillDir)
    {
        Directory.CreateDirectory(skillDir);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "setup-project.skill.md"),
            SkillContent.SetupProject);
        await File.WriteAllTextAsync(
            Path.Combine(skillDir, "update.skill.md"),
            SkillContent.Update);
    }
}
```

- [ ] **Step 9.4: Add `SkillContent` class with skill file text**

```csharp
// src/DocAgent.McpServer/Cli/SkillContent.cs
namespace DocAgent.McpServer.Cli;

/// <summary>Embedded content for the Claude Code skill files written by install.</summary>
internal static class SkillContent
{
    public static readonly string SetupProject = """
        ---
        name: docagent-setup-project
        description: Set up DocAgent for the current project — select sources, configure agents, and run first ingest
        type: project-setup
        ---

        # DocAgent Project Setup

        Guide the user through setting up DocAgent for the current project.

        ## Steps

        1. Check for `docagent.project.json` in cwd.
           - If present, ask: [Reconfigure] [Re-ingest only] [Cancel].
           - If absent, proceed to step 2.

        2. Find `.sln` and `.csproj` files in cwd and parent directories (up to 2 levels).
           Present as numbered choices. Use AskUserQuestion for selection.
           Map answer to `--primary <path>`.

        3. Find nearby secondary sources:
           - Other `.sln`/`.csproj` files (excluding primary): `--secondary dotnet:<path>`
           - Directories with `package.json`: `--secondary typescript:<path>`
           Present as multi-select using AskUserQuestion. Skip if none found.

        4. Ask: Ingest now? [Yes / No] → `--ingest` if Yes.

        5. Ask: Enable git hooks for auto re-ingest? [Yes / No / Tell me more]
           If "Tell me more": show excerpt from docs/GitHooks.md, then re-ask.
           Map No → `--no-hooks`.

        6. Run: `docagent init --non-interactive --primary <x> [--secondary <type>:<path>...] [--ingest] [--no-hooks] --yes`

        7. If --ingest was selected, stream the command output to the user.

        8. Report: list files written, symbol count and snapshot hash from JSON output, and reminder:
           "Run `/docagent:update` or `docagent update` any time to re-ingest."
        """;

    public static readonly string Update = """
        ---
        name: docagent-update
        description: Re-ingest the current project's code into DocAgent
        type: update
        ---

        # DocAgent Update

        Re-ingest the current project.

        ## Steps

        1. Check for `docagent.project.json` in cwd.
           - If absent, check for `~/.docagent/config.json`.
           - If both missing: ask "DocAgent is not set up for this project. Run setup now? [Yes / No]"
             If Yes: invoke setup-project skill. If No: stop.

        2. Run: `docagent update`

        3. Parse the JSON output:
           ```json
           {"status":"ok","projectsIngested":3,"symbolCount":4821,"durationMs":12400,"snapshotHash":"abc123"}
           ```

        4. Report to user:
           "DocAgent updated: {projectsIngested} projects, {symbolCount} symbols indexed in {durationMs/1000}s (snapshot {snapshotHash})."

        5. On non-zero exit: surface stderr to user and suggest running `docagent update` in terminal for full output.
        """;
}
```

- [ ] **Step 9.5: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~InstallCommandTests"
```

- [ ] **Step 9.6: Run full suite**
```
dotnet test
```

- [ ] **Step 9.7: Commit**
```bash
git add src/DocAgent.McpServer/Cli/InstallCommand.cs \
        src/DocAgent.McpServer/Cli/SkillContent.cs \
        tests/DocAgent.Tests/Cli/InstallCommandTests.cs
git commit -m "feat(cli): implement InstallCommand — agent detection, config writing, skill install"
```

---

## Chunk 5: InitCommand

### Task 10: `InitCommand` — full implementation

**Files:**
- Modify: `src/DocAgent.McpServer/Cli/InitCommand.cs` (replace stub)
- Test: `tests/DocAgent.Tests/Cli/InitCommandTests.cs`

- [ ] **Step 10.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/InitCommandTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using System.Text.Json.Nodes;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class InitCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public InitCommandTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task RunAsync_NonInteractive_WritesProjectJson()
    {
        var slnPath = Path.Combine(_dir, "MyApp.sln");
        await File.WriteAllTextAsync(slnPath, "");

        var exitCode = await InitCommand.RunAsync(
            ["--primary", slnPath, "--no-hooks", "--yes"],
            workingDir: _dir);

        exitCode.Should().Be(0);
        var config = await ProjectConfig.LoadFromDirAsync(_dir);
        config.Should().NotBeNull();
        config!.PrimarySource.Should().Be(slnPath);
    }

    [Fact]
    public async Task RunAsync_WritesGitignoreEntry()
    {
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");

        await InitCommand.RunAsync(
            ["--primary", slnPath, "--no-hooks", "--yes"],
            workingDir: _dir);

        var gitignore = await File.ReadAllTextAsync(Path.Combine(_dir, ".gitignore"));
        gitignore.Should().Contain(".docagent/artifacts/");
    }

    [Fact]
    public async Task RunAsync_GitignoreIdempotent_NoduplicateEntry()
    {
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");
        var gitignorePath = Path.Combine(_dir, ".gitignore");
        await File.WriteAllTextAsync(gitignorePath, ".docagent/artifacts/\n");

        await InitCommand.RunAsync(["--primary", slnPath, "--no-hooks", "--yes"], workingDir: _dir);
        await InitCommand.RunAsync(["--primary", slnPath, "--no-hooks", "--yes"], workingDir: _dir);

        var lines = (await File.ReadAllTextAsync(gitignorePath))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Trim() == ".docagent/artifacts/");
        lines.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WritesClaudioMdBlock()
    {
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");

        await InitCommand.RunAsync(
            ["--primary", slnPath, "--no-hooks", "--yes"],
            workingDir: _dir);

        var claudeMd = await File.ReadAllTextAsync(Path.Combine(_dir, "CLAUDE.md"));
        claudeMd.Should().Contain("<!-- docagent -->");
        claudeMd.Should().Contain("DocAgent");
        claudeMd.Should().Contain("<!-- /docagent -->");
    }

    [Fact]
    public async Task RunAsync_ClaudeMdIdempotent_ReplacesExistingBlock()
    {
        var slnPath = Path.Combine(_dir, "App.sln");
        await File.WriteAllTextAsync(slnPath, "");

        await InitCommand.RunAsync(["--primary", slnPath, "--no-hooks", "--yes"], workingDir: _dir);
        await InitCommand.RunAsync(["--primary", slnPath, "--no-hooks", "--yes"], workingDir: _dir);

        var content = await File.ReadAllTextAsync(Path.Combine(_dir, "CLAUDE.md"));
        var count = 0;
        var idx = 0;
        while ((idx = content.IndexOf("<!-- docagent -->", idx, StringComparison.Ordinal)) >= 0)
        { count++; idx++; }
        count.Should().Be(1, "sentinel block should appear exactly once");
    }

    [Fact]
    public async Task RunAsync_InvalidPrimary_ReturnsNonZero()
    {
        var exitCode = await InitCommand.RunAsync(
            ["--primary", "/does/not/exist.sln", "--yes"],
            workingDir: _dir);

        exitCode.Should().NotBe(0);
        File.Exists(Path.Combine(_dir, "docagent.project.json")).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WithSecondarySource_IncludedInProjectJson()
    {
        var slnPath = Path.Combine(_dir, "App.sln");
        var libPath = Path.Combine(_dir, "Lib.sln");
        await File.WriteAllTextAsync(slnPath, "");
        await File.WriteAllTextAsync(libPath, "");

        await InitCommand.RunAsync(
            ["--primary", slnPath, "--secondary", $"dotnet:{libPath}", "--no-hooks", "--yes"],
            workingDir: _dir);

        var config = await ProjectConfig.LoadFromDirAsync(_dir);
        config!.SecondarySources.Should().HaveCount(1);
        config.SecondarySources[0].Type.Should().Be("dotnet");
    }
}
```

- [ ] **Step 10.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~InitCommandTests" --no-build
```

- [ ] **Step 10.3: Implement `InitCommand`**

```csharp
// src/DocAgent.McpServer/Cli/InitCommand.cs
namespace DocAgent.McpServer.Cli;

public static class InitCommand
{
    private const string ClaudeBlock = """
        <!-- docagent -->
        ## DocAgent (code documentation MCP)
        This project uses DocAgent to serve symbol graph and documentation queries.
        - Re-ingest after significant code changes: run `/docagent:update` or `docagent update`
        - Git hooks for automatic re-ingest: see `docs/GitHooks.md`
        - MCP tools available: search_symbols, get_symbol, find_implementations, explain_project, and more
        <!-- /docagent -->
        """;

    public static async Task<int> RunAsync(string[] args, string? workingDir = null)
    {
        workingDir ??= Directory.GetCurrentDirectory();
        bool yesFlag = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);
        bool noHooks = args.Contains("--no-hooks", StringComparer.OrdinalIgnoreCase);
        bool ingest = args.Contains("--ingest", StringComparer.OrdinalIgnoreCase);

        var primarySource = GetFlag(args, "--primary");
        var secondarySources = GetAllFlags(args, "--secondary");

        // Resolve / validate primary source
        if (string.IsNullOrWhiteSpace(primarySource))
            primarySource = AutoDetectPrimarySource(workingDir);

        if (string.IsNullOrWhiteSpace(primarySource) ||
            (!File.Exists(primarySource) && !Directory.Exists(primarySource)))
        {
            Console.Error.WriteLine(
                primarySource is null
                    ? "No .sln or .csproj found in current directory. Use --primary <path>."
                    : $"Primary source not found: {primarySource}");
            return 1;
        }

        var config = new ProjectConfig
        {
            PrimarySource = primarySource,
            SecondarySources = secondarySources
                .Select(ParseSecondarySource)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList()
        };

        var projectJsonPath = Path.Combine(workingDir, "docagent.project.json");
        await ProjectConfig.SaveAsync(config, projectJsonPath);
        Console.Error.WriteLine($"Wrote: {projectJsonPath}");

        await AppendGitignore(workingDir);
        await UpsertClaudeMdBlock(workingDir);

        if (ingest)
        {
            Console.Error.WriteLine("Running initial ingest...");
            var updateExit = await UpdateCommand.RunAsync([], workingDir: workingDir);
            if (updateExit != 0) return updateExit;
        }

        if (!noHooks)
        {
            if (yesFlag)
                await HooksCommand.RunAsync(["enable"], workingDir: workingDir);
            // else: leave hooks disabled (opt-in)
        }

        return 0;
    }

    private static string? AutoDetectPrimarySource(string dir)
    {
        var slnFiles = Directory.GetFiles(dir, "*.sln");
        if (slnFiles.Length == 1) return slnFiles[0];
        if (slnFiles.Length > 1) return null; // ambiguous — require --primary
        var csprojFiles = Directory.GetFiles(dir, "*.csproj");
        return csprojFiles.Length == 1 ? csprojFiles[0] : null;
    }

    private static SecondarySource? ParseSecondarySource(string spec)
    {
        var idx = spec.IndexOf(':');
        if (idx < 0) return null;
        return new SecondarySource(spec[..idx], spec[(idx + 1)..]);
    }

    private static async Task AppendGitignore(string dir)
    {
        const string entry = ".docagent/artifacts/";
        var path = Path.Combine(dir, ".gitignore");

        if (File.Exists(path))
        {
            var lines = await File.ReadAllLinesAsync(path);
            if (lines.Any(l => l.Trim() == entry)) return; // already present
        }

        await File.AppendAllTextAsync(path,
            $"\n# DocAgent snapshot artifacts\n{entry}\n");
        Console.Error.WriteLine($"Updated: {path}");
    }

    private static async Task UpsertClaudeMdBlock(string dir)
    {
        var path = Path.Combine(dir, "CLAUDE.md");
        const string start = "<!-- docagent -->";
        const string end = "<!-- /docagent -->";

        string existing = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;

        var startIdx = existing.IndexOf(start, StringComparison.Ordinal);
        var endIdx = existing.IndexOf(end, StringComparison.Ordinal);

        string newContent;
        if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
        {
            // Replace existing block
            newContent = existing[..startIdx] + ClaudeBlock +
                         existing[(endIdx + end.Length)..];
        }
        else
        {
            // Append new block
            newContent = (existing.Length > 0 && !existing.EndsWith('\n')
                ? existing + "\n\n"
                : existing) + ClaudeBlock;
        }

        await File.WriteAllTextAsync(path, newContent);
        Console.Error.WriteLine($"Updated: {path}");
    }

    private static string? GetFlag(string[] args, string flag)
    {
        var idx = Array.FindIndex(args, a =>
            a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static List<string> GetAllFlags(string[] args, string flag)
    {
        var results = new List<string>();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                results.Add(args[i + 1]);
        return results;
    }
}
```

- [ ] **Step 10.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~InitCommandTests"
```

- [ ] **Step 10.5: Run full suite**
```
dotnet test
```

- [ ] **Step 10.6: Commit**
```bash
git add src/DocAgent.McpServer/Cli/InitCommand.cs \
        tests/DocAgent.Tests/Cli/InitCommandTests.cs
git commit -m "feat(cli): implement InitCommand — project setup, gitignore, CLAUDE.md block"
```

---

## Chunk 6: HooksCommand

### Task 11: `HooksCommand` — git hooks enable/disable

**Files:**
- Modify: `src/DocAgent.McpServer/Cli/HooksCommand.cs` (replace stub)
- Test: `tests/DocAgent.Tests/Cli/HooksCommandTests.cs`

- [ ] **Step 11.1: Write failing tests**

```csharp
// tests/DocAgent.Tests/Cli/HooksCommandTests.cs
using DocAgent.McpServer.Cli;
using FluentAssertions;
using Xunit;

namespace DocAgent.Tests.Cli;

public sealed class HooksCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private string GitHooksDir => Path.Combine(_dir, ".git", "hooks");

    public HooksCommandTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(GitHooksDir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Enable_CreatesPostCommitAndPostMerge()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);

        File.Exists(Path.Combine(GitHooksDir, "post-commit")).Should().BeTrue();
        File.Exists(Path.Combine(GitHooksDir, "post-merge")).Should().BeTrue();
    }

    [Fact]
    public async Task Enable_HookContentContainsSentinelAndCommand()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);

        var content = await File.ReadAllTextAsync(Path.Combine(GitHooksDir, "post-commit"));
        content.Should().Contain("# BEGIN docagent-mcp");
        content.Should().Contain("docagent update --quiet");
        content.Should().Contain("# END docagent-mcp");
    }

    [Fact]
    public async Task Enable_Idempotent_DoesNotDuplicateSentinelBlock()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);

        var content = await File.ReadAllTextAsync(Path.Combine(GitHooksDir, "post-commit"));
        content.Split("# BEGIN docagent-mcp").Length.Should().Be(2, "block appears exactly once");
    }

    [Fact]
    public async Task Enable_ExistingHook_AppendsNotOverwrites()
    {
        var hookPath = Path.Combine(GitHooksDir, "post-commit");
        await File.WriteAllTextAsync(hookPath, "#!/bin/sh\necho existing\n");

        await HooksCommand.RunAsync(["enable"], workingDir: _dir);

        var content = await File.ReadAllTextAsync(hookPath);
        content.Should().Contain("echo existing");
        content.Should().Contain("docagent update --quiet");
    }

    [Fact]
    public async Task Disable_RemovesSentinelBlock()
    {
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);
        await HooksCommand.RunAsync(["disable"], workingDir: _dir);

        var hookPath = Path.Combine(GitHooksDir, "post-commit");
        if (File.Exists(hookPath))
        {
            var content = await File.ReadAllTextAsync(hookPath);
            content.Should().NotContain("# BEGIN docagent-mcp");
        }
        // File may not exist if docagent was the only content — both outcomes are valid
    }

    [Fact]
    public async Task Disable_ExistingHookWithOtherContent_PreservesOtherContent()
    {
        var hookPath = Path.Combine(GitHooksDir, "post-commit");
        await File.WriteAllTextAsync(hookPath, "#!/bin/sh\necho existing\n");
        await HooksCommand.RunAsync(["enable"], workingDir: _dir);
        await HooksCommand.RunAsync(["disable"], workingDir: _dir);

        File.Exists(hookPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(hookPath);
        content.Should().Contain("echo existing");
        content.Should().NotContain("# BEGIN docagent-mcp");
    }

    [Fact]
    public async Task Enable_NoGitDir_ReturnsNonZero()
    {
        var noGitDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(noGitDir);
        try
        {
            var exitCode = await HooksCommand.RunAsync(["enable"], workingDir: noGitDir);
            exitCode.Should().NotBe(0);
        }
        finally { Directory.Delete(noGitDir); }
    }
}
```

- [ ] **Step 11.2: Run tests — confirm they fail**
```
dotnet test --filter "FullyQualifiedName~HooksCommandTests" --no-build
```

- [ ] **Step 11.3: Implement `HooksCommand`**

```csharp
// src/DocAgent.McpServer/Cli/HooksCommand.cs
namespace DocAgent.McpServer.Cli;

public static class HooksCommand
{
    private const string SentinelBegin = "# BEGIN docagent-mcp";
    private const string SentinelEnd = "# END docagent-mcp";

    private static readonly string[] HookNames = ["post-commit", "post-merge"];

    public static async Task<int> RunAsync(string[] args, string? workingDir = null)
    {
        workingDir ??= Directory.GetCurrentDirectory();
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

        var gitHooksDir = Path.Combine(workingDir, ".git", "hooks");
        if (!Directory.Exists(gitHooksDir))
        {
            Console.Error.WriteLine(
                "No .git/hooks directory found. Run 'docagent hooks enable' from the root of a git repository.");
            return 1;
        }

        return sub switch
        {
            "enable" => await Enable(gitHooksDir, workingDir),
            "disable" => await Disable(gitHooksDir),
            _ => PrintUsage()
        };
    }

    private static async Task<int> Enable(string hooksDir, string workingDir)
    {
        var userConfig = await UserConfig.LoadAsync();
        var hookBody = BuildHookBody(userConfig?.HostingMode ?? HostingMode.A,
            userConfig?.BinaryPath);

        foreach (var hookName in HookNames)
        {
            var path = Path.Combine(hooksDir, hookName);
            await UpsertHookBlock(path, hookBody);
            Console.Error.WriteLine($"Installed hook: {path}");
        }

        return 0;
    }

    private static async Task<int> Disable(string hooksDir)
    {
        foreach (var hookName in HookNames)
        {
            var path = Path.Combine(hooksDir, hookName);
            if (!File.Exists(path)) continue;
            await RemoveHookBlock(path);
            Console.Error.WriteLine($"Removed docagent block from: {path}");
        }
        return 0;
    }

    private static string BuildHookBody(HostingMode mode, string? binaryPath) =>
        mode switch
        {
            HostingMode.B when binaryPath is not null =>
                $""""{binaryPath}" update --quiet || true""",
            _ => "docagent update --quiet || true"
        };

    private static async Task UpsertHookBlock(string path, string hookBody)
    {
        string existing = File.Exists(path) ? await File.ReadAllTextAsync(path) : "#!/bin/sh\n";

        var newBlock = $"\n{SentinelBegin}\n{hookBody}\n{SentinelEnd}\n";

        var startIdx = existing.IndexOf(SentinelBegin, StringComparison.Ordinal);
        var endIdx = existing.IndexOf(SentinelEnd, StringComparison.Ordinal);

        string newContent;
        if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
        {
            // Replace existing block in-place
            newContent = existing[..startIdx].TrimEnd() +
                         newBlock +
                         existing[(endIdx + SentinelEnd.Length)..].TrimStart();
        }
        else
        {
            // Append block
            newContent = (existing.TrimEnd()) + newBlock;
        }

        await File.WriteAllTextAsync(path, newContent);

        // Ensure executable on Unix
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"");
                chmod?.WaitForExit(2000);
            }
            catch { /* best-effort */ }
        }
    }

    private static async Task RemoveHookBlock(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        var startIdx = content.IndexOf(SentinelBegin, StringComparison.Ordinal);
        var endIdx = content.IndexOf(SentinelEnd, StringComparison.Ordinal);

        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx) return;

        var before = content[..startIdx].TrimEnd();
        var after = content[(endIdx + SentinelEnd.Length)..].TrimStart();
        var newContent = (before + "\n" + after).Trim();

        if (string.IsNullOrWhiteSpace(newContent) || newContent == "#!/bin/sh")
            File.Delete(path);
        else
            await File.WriteAllTextAsync(path, newContent + "\n");
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: docagent hooks <enable|disable>");
        return 1;
    }
}
```

- [ ] **Step 11.4: Run tests — confirm they pass**
```
dotnet test --filter "FullyQualifiedName~HooksCommandTests"
```

- [ ] **Step 11.5: Run full suite**
```
dotnet test
```

- [ ] **Step 11.6: Commit**
```bash
git add src/DocAgent.McpServer/Cli/HooksCommand.cs \
        tests/DocAgent.Tests/Cli/HooksCommandTests.cs
git commit -m "feat(cli): implement HooksCommand — git hook enable/disable with sentinel blocks"
```

---

## Chunk 7: Scripts, Documentation & Smoke Test

### Task 12: Bootstrapper scripts

**Files:**
- Create: `scripts/install-user.cs`
- Create: `scripts/setup-project.cs`
- Create: `scripts/README.md`

- [ ] **Step 12.1: Write `scripts/install-user.cs`**

```csharp
#!/usr/bin/env dotnet-script
// Run with: dotnet run scripts/install-user.cs [-- --version <x.y.z>] [-- --mode B] [-- --yes]
// Requires: .NET 10 SDK

using System.Diagnostics;

var version = GetFlag(args, "--version");
var mode = GetFlag(args, "--mode") ?? "A";
var yes = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);

Console.Error.WriteLine("DocAgent Install — user-level setup");
Console.Error.WriteLine("=====================================");

// Step 1: Check if docagent is already on PATH
if (IsOnPath("docagent"))
{
    Console.Error.WriteLine("docagent is already installed. Running 'docagent install'...");
    return RunProcess("docagent", BuildInstallArgs(mode, yes));
}

// Step 2: Mode C — run bootstrap logic inline (dotnet run too slow for delegation)
if (mode.Equals("C", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(
        "Mode C selected: MCP server via 'dotnet run'. Note: CLI subcommands (install, init, update) " +
        "require Mode A or B. User-level agent configs will not be written automatically in Mode C. " +
        "See docs/Agents.md for manual configuration.");
    return 0;
}

// Step 3: Install global tool (Mode A default)
Console.Error.WriteLine("Installing DocAgent global tool...");
var installArgs = new List<string> { "tool", "install", "-g", "DocAgent.McpServer" };
if (version is not null) installArgs.AddRange(["--version", version]);

var result = RunProcess("dotnet", installArgs);
if (result != 0)
{
    Console.Error.WriteLine($"dotnet tool install failed (exit {result}).");
    if (yes)
    {
        Console.Error.WriteLine("--yes mode: falling back to Mode B (self-contained binary)...");
        // Mode B fallback: publish self-contained binary
        var pubResult = RunProcess("dotnet", [
            "publish",
            "--configuration", "Release",
            "--runtime", GetRid(),
            "--self-contained",
            "--output", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docagent", "bin")
        ]);
        if (pubResult != 0)
        {
            Console.Error.WriteLine("Mode B fallback also failed.");
            return 1;
        }
        mode = "B";
    }
    else
    {
        Console.Error.WriteLine("Options: retry with --version <x>, use --mode B, or use --mode C.");
        return 1;
    }
}

// Step 4: Delegate to installed binary
return RunProcess("docagent", BuildInstallArgs(mode, yes));

// ── Helpers ──────────────────────────────────────────────────────────────────

static bool IsOnPath(string cmd)
{
    try
    {
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "where" : "which",
            Arguments = cmd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        p?.WaitForExit(3000);
        return p?.ExitCode == 0;
    }
    catch { return false; }
}

static int RunProcess(string exe, IEnumerable<string> arguments)
{
    var p = Process.Start(new ProcessStartInfo
    {
        FileName = exe,
        Arguments = string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
        UseShellExecute = false
    });
    p?.WaitForExit();
    return p?.ExitCode ?? 1;
}

static string? GetFlag(string[] a, string flag)
{
    var idx = Array.FindIndex(a, x => x.Equals(flag, StringComparison.OrdinalIgnoreCase));
    return idx >= 0 && idx + 1 < a.Length ? a[idx + 1] : null;
}

static string GetRid() =>
    OperatingSystem.IsWindows() ? "win-x64" :
    OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";

static IEnumerable<string> BuildInstallArgs(string mode, bool yes)
{
    var a = new List<string> { "install", "--mode", mode };
    if (yes) a.Add("--yes");
    return a;
}
```

- [ ] **Step 12.2: Write `scripts/setup-project.cs`**

```csharp
#!/usr/bin/env dotnet-script
// Run with: dotnet run scripts/setup-project.cs [-- --primary <path>] [-- --yes]
// Requires: .NET 10 SDK + docagent global tool installed (run install-user.cs first)

using System.Diagnostics;

if (!IsOnPath("docagent"))
{
    Console.Error.WriteLine(
        "docagent is not installed. Run 'dotnet run scripts/install-user.cs' first.");
    return 1;
}

var passThrough = args.ToList();
return RunProcess("docagent", ["init", ..passThrough]);

static bool IsOnPath(string cmd)
{
    try
    {
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "where" : "which",
            Arguments = cmd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        p?.WaitForExit(3000);
        return p?.ExitCode == 0;
    }
    catch { return false; }
}

static int RunProcess(string exe, IEnumerable<string> arguments)
{
    var p = Process.Start(new ProcessStartInfo
    {
        FileName = exe,
        Arguments = string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
        UseShellExecute = false
    });
    p?.WaitForExit();
    return p?.ExitCode ?? 1;
}
```

- [ ] **Step 12.3: Write `scripts/README.md`**

```markdown
# DocAgent Setup Scripts

Cross-platform setup scripts using .NET 10 single-file script execution.

## Prerequisites

- .NET 10 SDK

## User-level install (run once per machine)

```cmd
dotnet run scripts/install-user.cs
```

Options:
- `-- --yes` — skip all confirmation prompts
- `-- --mode A` (default) | `B` | `C` — hosting mode (see docs/Setup.md)
- `-- --version 2.1.0` — pin to a specific tool version

## Project setup (run in each project)

```cmd
dotnet run scripts/setup-project.cs
```

Or, once `docagent` is installed:
```cmd
docagent init
```

Options:
- `-- --primary <path>` — path to .sln or .csproj
- `-- --secondary dotnet:<path>` — additional .sln (repeatable)
- `-- --secondary typescript:<path>` — TypeScript project (repeatable)
- `-- --ingest` — run first ingest immediately
- `-- --yes` — non-interactive
```

- [ ] **Step 12.4: Build scripts to confirm they parse correctly**
```bash
dotnet run scripts/install-user.cs -- --help 2>&1 || true
dotnet run scripts/setup-project.cs -- --help 2>&1 || true
```

- [ ] **Step 12.5: Commit**
```bash
git add scripts/install-user.cs scripts/setup-project.cs scripts/README.md
git commit -m "feat(scripts): add .NET 10 single-file bootstrapper scripts"
```

---

### Task 13: Documentation

**Files:**
- Create: `docs/Setup.md`
- Create: `docs/Agents.md`
- Create: `docs/GitHooks.md`

- [ ] **Step 13.1: Write `docs/Setup.md`**

```markdown
# DocAgent Setup Guide

## Quick Start (3 steps)

**Step 1 — Install the tool and configure your agents:**
```cmd
dotnet run scripts/install-user.cs
```
This installs the `docagent` global tool and writes MCP server config for every detected agent
(Claude Code, Cursor, VS Code, Windsurf, OpenCode, Zed).

**Step 2 — Set up your project:**
```cmd
cd /path/to/your/project
docagent init
```
This creates `docagent.project.json`, updates `.gitignore` and `CLAUDE.md`, and optionally runs a first ingest.

**Step 3 — Re-ingest after code changes:**
```cmd
docagent update
```
Or use the Claude Code skill: `/docagent:update`

---

## Prerequisites

- .NET 10 SDK (required — used for both the tool and the setup scripts)
- At least one supported AI agent tool installed (see [Agents.md](Agents.md))

---

## Hosting Modes

### Mode A — Global tool (recommended)

`dotnet tool install -g DocAgent.McpServer` installs `docagent` on your PATH.

```cmd
dotnet run scripts/install-user.cs -- --mode A
```

### Mode B — Self-contained binary

Publishes a standalone binary to `~/.docagent/bin/docagent[.exe]`. No .NET runtime needed at runtime.

```cmd
dotnet run scripts/install-user.cs -- --mode B
```

### Mode C — Run from source (MCP server only)

Points agents directly at `dotnet run --project <path>`. CLI subcommands (`init`, `update`, `hooks`)
are not available via Mode C — use Mode A or B for those.

---

## Version Pinning

```cmd
dotnet run scripts/install-user.cs -- --version 2.1.0
```

To upgrade later:
```cmd
dotnet tool update -g DocAgent.McpServer
docagent update   # re-ingest with new binary
```

---

## Troubleshooting

| Problem | Solution |
|---------|---------|
| `docagent` not found after install | Restart terminal; check `~/.dotnet/tools` is on PATH |
| Agent not detecting DocAgent | Run `docagent install --yes` again; check agent was restarted |
| Ingest times out | Increase `DocAgent:IngestionTimeoutSeconds` in `appsettings.json` (default: 1800s) |
| Read-only config file | See manual config in [Agents.md](Agents.md) |

---

## Manual Uninstall

1. Remove agent configs: delete the `"docagent"` key from each agent's config file (see [Agents.md](Agents.md))
2. Remove hooks: `docagent hooks disable`
3. Remove global tool: `dotnet tool uninstall -g DocAgent.McpServer`
4. Remove artifacts: delete `~/.docagent/` and any project `.docagent/` directories
```

- [ ] **Step 13.2: Write `docs/Agents.md`**

````markdown
# Per-Agent Configuration Reference

This file documents the MCP config file location and JSON structure for each supported agent.
It also serves as the guide for **adding support for a new agent**.

---

## Adding a new agent

1. Add a `Probe<AgentName>()` method to `src/DocAgent.McpServer/Cli/AgentDetector.cs`
2. Add the probe to the `AllProbes()` array
3. Add an entry to this file following the template below

---

## Claude Desktop

**Windows config:** `%APPDATA%\Claude\claude_desktop_config.json`
**macOS config:** `~/Library/Application Support/Claude/claude_desktop_config.json`
**Restart required:** Yes

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
    }
  }
}
```

---

## Claude Code CLI

**Config:** `~/.claude.json`
**Restart required:** No (reloads on next session)

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
    }
  }
}
```

---

## VS Code / GitHub Copilot

**Project-level config:** `.vscode/mcp.json` in the workspace root
**Restart required:** No (VS Code picks up on save)

```json
{
  "servers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
    }
  }
}
```

---

## Cursor

**Config:** `~/.cursor/mcp.json`
**Restart required:** Yes

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
    }
  }
}
```

---

## Windsurf

**Config:** `~/.codeium/windsurf/mcp_config.json`
**Restart required:** Yes

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": [],
      "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
    }
  }
}
```

---

## OpenCode

**Config:** `~/.config/opencode/config.json`
**Restart required:** No

```json
{
  "mcp": {
    "servers": {
      "docagent": {
        "command": "docagent",
        "args": [],
        "env": { "DOCAGENT_ARTIFACTS_DIR": "/abs/path/.docagent/artifacts" }
      }
    }
  }
}
```

---

## Zed

**Config:** `~/.config/zed/settings.json`
**Restart required:** No (reloads automatically)

```json
{
  "context_servers": {
    "docagent": {
      "command": { "path": "docagent", "args": [] },
      "settings": {}
    }
  }
}
```
````

- [ ] **Step 13.3: Write `docs/GitHooks.md`**

```markdown
# Git Hooks — Automatic Re-Ingest

DocAgent can automatically re-ingest your project after every commit and merge.
This is **opt-in** — hooks are never installed without your explicit consent.

## Enable

```cmd
docagent hooks enable
```

This installs `post-commit` and `post-merge` hooks into `.git/hooks/`.

**What the hook does:**
```sh
#!/bin/sh
# BEGIN docagent-mcp
docagent update --quiet || true
# END docagent-mcp
```

- `--quiet` suppresses all output unless there is an error.
- `|| true` ensures a failed ingest never blocks your commit.
- The sentinel comments (`BEGIN`/`END docagent-mcp`) allow clean removal.

## Disable

```cmd
docagent hooks disable
```

Removes the docagent sentinel block from hook files.
If the hook file becomes empty, it is deleted.

## Skip a single commit

```cmd
git commit --no-verify
```

This skips all hooks (not just DocAgent).

## Team usage

Hooks live in `.git/hooks/` and are **not committed to the repo**.
Each team member opts in by running `docagent hooks enable`.
Mention this in your project's CONTRIBUTING.md if you want the team to use it.

## Mode B note

If installed in Mode B (self-contained binary), the hook uses the absolute binary path
(e.g. `~/.docagent/bin/docagent`). If you move the binary, re-run `docagent hooks enable`
to update the hook.
```

- [ ] **Step 13.4: Run full test suite one final time**
```
dotnet test
```
Expected: all pass

- [ ] **Step 13.5: Commit docs**
```bash
git add docs/Setup.md docs/Agents.md docs/GitHooks.md
git commit -m "docs: add Setup.md, Agents.md, GitHooks.md for MCP install system"
```

---

### Task 14: Smoke test — end-to-end CLI routing

- [ ] **Step 14.1: Build the server**
```
dotnet build src/DocAgent.McpServer/DocAgent.McpServer.csproj
```

- [ ] **Step 14.2: Verify CLI routing — `update` with no project config prints helpful error**
```
dotnet run --project src/DocAgent.McpServer -- update
```
Expected output (stderr): `docagent.project.json not found in current directory. Run 'docagent init' to set up this project.`

- [ ] **Step 14.3: Verify CLI routing — unrecognised arg falls through to MCP mode (just check it starts, then Ctrl+C)**
```
dotnet run --project src/DocAgent.McpServer -- --stdio
```
Expected: server starts (waits on stdin) — press Ctrl+C to stop. No CLI handler triggered.

- [ ] **Step 14.4: Verify `hooks` with no git dir**
```
cd /tmp && dotnet run --project /path/to/src/DocAgent.McpServer -- hooks enable
```
Expected (stderr): message about missing `.git/hooks` directory, exit code 1

- [ ] **Step 14.5: Final full test run**
```
dotnet test
```
Expected: all pass

- [ ] **Step 14.6: Final commit**
```bash
git add -A
git commit -m "feat: MCP setup & install system — scripts, CLI commands, skills, docs"
```
