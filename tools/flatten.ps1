param(
  [Parameter(Mandatory=$true)][string]$RepoRoot,
  [Parameter(Mandatory=$true)][string]$OutFile
)

$repo = (Resolve-Path $RepoRoot).Path
$out  = (Resolve-Path (Split-Path $OutFile -Parent)).Path + "\" + (Split-Path $OutFile -Leaf)

$files = Get-ChildItem -Path $repo -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\obj\\' } |
  Sort-Object FullName

"// Flattened view for review (NOT guaranteed to compile)." | Out-File -FilePath $out -Encoding utf8
"// Repo: $repo" | Out-File -FilePath $out -Append -Encoding utf8
"// Files: $($files.Count)" | Out-File -FilePath $out -Append -Encoding utf8
"" | Out-File -FilePath $out -Append -Encoding utf8

foreach ($f in $files) {
  "// ---- FILE: $([IO.Path]::GetRelativePath($repo, $f.FullName)) ----" | Out-File -FilePath $out -Append -Encoding utf8
  Get-Content $f.FullName | Out-File -FilePath $out -Append -Encoding utf8
  "" | Out-File -FilePath $out -Append -Encoding utf8
}

Write-Host "Wrote $out"
