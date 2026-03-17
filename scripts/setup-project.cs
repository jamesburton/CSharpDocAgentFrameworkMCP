#!/usr/bin/env dotnet-script
// setup-project.cs — DocAgent project initialiser bootstrapper
// Usage: dotnet run scripts/setup-project.cs [args passed to `docagent init`]
//
// Checks that docagent is installed, then delegates to `docagent init`.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

// ── Guard: docagent must be installed ───────────────────────────────────────

if (!IsOnPath("docagent"))
{
    Console.Error.WriteLine("docagent is not installed. Run 'dotnet run scripts/install-user.cs' first.");
    return 1;
}

// ── Delegate to docagent init ────────────────────────────────────────────────

string passthroughArgs = string.Join(" ", args);
var (exitCode, _) = Run("docagent", "init " + passthroughArgs);
return exitCode;
