# Skill: UnusualChangeReview

## Purpose
Detect unusual or risky changes between two builds (or commits) and offer safe remediation options.

## Inputs
- `baseSnapshotRef`: snapshot ID / commit hash / build ID
- `targetSnapshotRef`: snapshot ID / commit hash / build ID
- `repoRoot`: allowlisted path
- `policy`: thresholds (e.g., public API change risk scoring)

## Outputs
A structured report:
- Summary risk score
- Findings grouped by category
  - Public API changes
  - Doc/API divergence
  - Dependency changes
  - “Suspicious” edits (large behavioral change with minimal doc/test change)
- Suggested actions
  - create branch
  - create worktree
  - apply automated edits (optional)
  - open PR

## Operational flow (git + worktrees)
1. Create worktree: `wt/review-<id>` (branch `review/<id>`)
2. Run snapshot ingestion for both refs
3. Produce diff report
4. If user chooses:
   - **Remove changes**: `git revert` / `git checkout -- <paths>` in worktree
   - **Incorporate changes**: update docs/tests, regenerate snapshot, re-run report
5. Open PR back to main

## Safety constraints
- No auto-commit without explicit user instruction
- No pushes
- Worktree operates on a dedicated branch
