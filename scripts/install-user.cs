#!/usr/bin/env dotnet-script
// install-user.cs — DocAgent user-level installer
// Usage: dotnet run scripts/install-user.cs [--version <ver>] [--mode A|B|C] [--yes]
//
// Installs the `docagent` global tool, then delegates to `docagent install`
// to configure MCP agents on the current machine.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

// ── Argument parsing ────────────────────────────────────────────────────────

string? version = null;
string? mode = null;
bool yes = false;
var passthrough = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--version" when i + 1 < args.Length:
            version = args[++i];
            passthrough.Add("--version");
            passthrough.Add(version);
            break;
        case "--mode" when i + 1 < args.Length:
            mode = args[++i].ToUpperInvariant();
            passthrough.Add("--mode");
            passthrough.Add(mode);
            break;
        case "--yes":
        case "-y":
            yes = true;
            passthrough.Add("--yes");
            break;
        default:
            passthrough.Add(args[i]);
            break;
    }
}

// ── Helpers ─────────────────────────────────────────────────────────────────

static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

static (int exitCode, string output) Run(string exe, string arguments, bool captureOutput = false)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        RedirectStandardOutput = captureOutput,
        RedirectStandardError  = captureOutput,
        UseShellExecute        = false,
    };

    using var proc = Process.Start(psi)!;
    string output = captureOutput ? proc.StandardOutput.ReadToEnd() : string.Empty;
    proc.WaitForExit();
    return (proc.ExitCode, output.Trim());
}

static bool IsOnPath(string tool)
{
    string finder = IsWindows() ? "where" : "which";
    var (code, _) = Run(finder, tool, captureOutput: true);
    return code == 0;
}

static void PrintLine(string message) => Console.WriteLine(message);
static void PrintError(string message) => Console.Error.WriteLine($"error: {message}");

// ── Check if docagent is already installed ──────────────────────────────────

if (IsOnPath("docagent"))
{
    PrintLine("docagent is already installed. Delegating to `docagent install`...");
    var (code, _) = Run("docagent", "install " + string.Join(" ", passthrough));
    return code;
}

// ── Mode C: CLI commands unavailable ────────────────────────────────────────

if (string.Equals(mode, "C", StringComparison.OrdinalIgnoreCase))
{
    PrintLine("docagent is not installed.");
    PrintLine("");
    PrintLine("Mode C (direct dotnet run) does not require a global tool installation.");
    PrintLine("Your MCP server will be launched directly by the agent host using:");
    PrintLine("  dotnet run --project <path/to/DocAgent.McpServer>");
    PrintLine("");
    PrintLine("No further setup is needed for Mode C. Configure your agent host manually");
    PrintLine("using the JSON snippet in docs/Agents.md.");
    return 0;
}

// ── Install via dotnet tool ──────────────────────────────────────────────────

string installArgs = "tool install -g DocAgent.McpServer";
if (version is not null)
    installArgs += $" --version {version}";

PrintLine($"Running: dotnet {installArgs}");
var (installCode, _) = Run("dotnet", installArgs);

if (installCode != 0)
{
    // ── Fallback to Mode B (self-contained publish) ──────────────────────────

    if (yes)
    {
        PrintLine("");
        PrintLine("`dotnet tool install` failed. Falling back to Mode B (self-contained publish)...");

        string homeDir   = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string binDir    = Path.Combine(homeDir, ".docagent", "bin");
        string outputExe = Path.Combine(binDir, IsWindows() ? "docagent.exe" : "docagent");

        Directory.CreateDirectory(binDir);

        string rid = IsWindows() ? "win-x64" : (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" : "linux-x64");
        string publishArgs = $"publish src/DocAgent.McpServer -r {rid} -c Release --self-contained true -o \"{binDir}\"";

        PrintLine($"Running: dotnet {publishArgs}");
        var (pubCode, _) = Run("dotnet", publishArgs);

        if (pubCode != 0)
        {
            PrintError("Self-contained publish also failed. Check build errors above.");
            return 1;
        }

        PrintLine($"Published to {binDir}");
        PrintLine("");
        PrintLine($"Add {binDir} to your PATH, then re-run this script (or run `docagent install`).");
        PrintLine("Example (bash):  export PATH=\"$PATH:{binDir}\"");
        return 0;
    }
    else
    {
        PrintError("`dotnet tool install -g DocAgent.McpServer` failed.");
        PrintLine("");
        PrintLine("Suggestions:");
        PrintLine("  1. Ensure you have a NuGet source configured that publishes DocAgent.McpServer.");
        PrintLine("  2. Re-run with --yes to fall back to a self-contained binary in ~/.docagent/bin/.");
        PrintLine("  3. Use --mode C to skip global tool installation entirely.");
        PrintLine("  4. See docs/Setup.md for manual installation instructions.");
        return 1;
    }
}

// ── Delegate to docagent install ────────────────────────────────────────────

PrintLine("Installation succeeded. Running `docagent install`...");
{
    var (code, _) = Run("docagent", "install " + string.Join(" ", passthrough));
    return code;
}
