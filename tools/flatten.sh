#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="${1:?repo root required}"
OUT_FILE="${2:?out file required}"

repo="$(cd "$REPO_ROOT" && pwd)"
out="$(mkdir -p "$(dirname "$OUT_FILE")" && cd "$(dirname "$OUT_FILE")" && pwd)/$(basename "$OUT_FILE")"

{
  echo "// Flattened view for review (NOT guaranteed to compile)."
  echo "// Repo: $repo"
  echo "// Files: $(find "$repo" -type f -name '*.cs' ! -path '*/bin/*' ! -path '*/obj/*' | wc -l | tr -d ' ')"
  echo
  while IFS= read -r f; do
    rel="${f#$repo/}"
    echo "// ---- FILE: $rel ----"
    cat "$f"
    echo
  done < <(find "$repo" -type f -name '*.cs' ! -path '*/bin/*' ! -path '*/obj/*' | sort)
} > "$out"

echo "Wrote $out"
