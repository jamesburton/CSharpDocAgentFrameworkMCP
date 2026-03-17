# Git Hooks

DocAgent can install a git pre-commit hook that runs a lightweight documentation-coverage check before each commit. The hook is opt-in and per-repository.

---

## Enabling the Hook

```bash
docagent hooks enable
```

This writes `.git/hooks/pre-commit` in the current repository. The hook is not committed to the repo; each contributor must run `docagent hooks enable` in their own clone.

To enable during `docagent init`:

```bash
docagent init --hooks
```

---

## What the Hook Does

On each `git commit`, the hook:

1. Runs `docagent check --staged` against the files in the index.
2. If the check finds **undocumented public symbols** added in the staged diff, it prints a summary and exits with code 1, blocking the commit.
3. If all new public symbols have doc comments (or the change adds no new public symbols), it exits with code 0 and the commit proceeds.

**Hook script (written to `.git/hooks/pre-commit`):**

```sh
#!/usr/bin/env sh
# DocAgent pre-commit hook — documentation coverage check
# Installed by: docagent hooks enable
# Remove with:  docagent hooks disable

docagent check --staged
exit $?
```

The `docagent check --staged` command reads the git index directly (no working-tree side-effects) and is fast for typical incremental commits.

---

## Disabling the Hook

```bash
docagent hooks disable
```

This removes `.git/hooks/pre-commit`. If the file was not written by DocAgent (the marker comment is absent), the command prints a warning and exits without modifying the file.

To skip the hook for a single commit without disabling it:

```bash
git commit --no-verify
```

Use `--no-verify` sparingly. It bypasses **all** pre-commit hooks, not just the DocAgent one.

---

## Team Usage

- The hook file lives in `.git/hooks/`, which is not tracked by git. Every developer must run `docagent hooks enable` after cloning.
- To encourage adoption, add a note to your project's `README.md` or `CONTRIBUTING.md`:
  > After cloning, run `docagent hooks enable` to activate the documentation-coverage pre-commit check.
- In CI, run `docagent check` as a separate step rather than relying on the git hook. The hook is a developer-experience convenience, not a CI gate.

---

## Mode B Path Note

If you installed DocAgent in Mode B (self-contained binary at `~/.docagent/bin/docagent`), ensure `~/.docagent/bin` is on your PATH **before** the hook runs. The hook script calls `docagent` by name; if it is not on PATH at commit time the hook will fail with `command not found`.

Add to your shell profile:

```bash
# ~/.bashrc or ~/.zshrc
export PATH="$HOME/.docagent/bin:$PATH"
```
