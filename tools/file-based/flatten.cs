#!/usr/bin/env dotnet
#:package Spectre.Console@*
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false

using System.Text;
using Spectre.Console;

if (args.Length < 2)
{
    AnsiConsole.MarkupLine("[red]Usage:[/] dotnet run flatten.cs -- <repoRoot> <outFile>");
    return;
}

var repoRoot = Path.GetFullPath(args[0]);
var outFile = Path.GetFullPath(args[1]);

var csFiles = Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
    .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();

var sb = new StringBuilder();
sb.AppendLine("// Flattened view for review (NOT guaranteed to compile).");
sb.AppendLine($"// Repo: {repoRoot}");
sb.AppendLine($"// Files: {csFiles.Count}");
sb.AppendLine();

foreach (var f in csFiles)
{
    sb.AppendLine($"// ---- FILE: {Path.GetRelativePath(repoRoot, f)} ----");
    sb.AppendLine(File.ReadAllText(f));
    sb.AppendLine();
}

Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
AnsiConsole.MarkupLine($"[green]Wrote[/] {outFile}");
