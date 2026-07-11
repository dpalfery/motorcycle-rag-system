# Fix `validate-docs.sh` ripgrep dependency (resolves false-positive catalog error)

**Status:** Draft
**Date:** 2026-07-11
**Goal:** Make `scripts/validate-docs.sh` run successfully in environments without ripgrep, eliminating both reported errors.

---

## 1. Problem / Motivation

Running `bash scripts/validate-docs.sh` in an environment without ripgrep produces:

```
scripts/validate-docs.sh: line 83: rg: command not found
Catalog is missing component: MotorcycleRAG system
Error: Process completed with exit code 1.
```

These are **two symptoms of a single root cause**, not two independent bugs.

**Root-cause chain (pre-fix behavior, verified against live source before this change):**

1. Before this fix, `scripts/validate-docs.sh:83` invoked `rg --fixed-strings --quiet "$component" 6-Docs/catalog.md`. `rg` (ripgrep) was **not** guaranteed to be installed. It was the *only* use of `rg` in the script (confirmed: no other `rg` reference existed anywhere in `scripts/*.sh`).
2. The script has `set -euo pipefail` (line 2) but a command used as an `if` condition is **exempt** from `set -e`. When `rg` was absent, the shell printed `rg: command not found` to stderr and returned exit code **127**.
3. Line 83 was `if ! rg ...; then`. `!` negated the non-zero (127) result to success, so the `then` branch always executed whenever `rg` could not run — printing `Catalog is missing component: <first component>` (line 84) and `exit 1` (line 85).
4. The loop stopped at the **first** component (`MotorcycleRAG system`) because of the immediate `exit 1`, so only one "missing component" line appeared. With a working search tool, **all 9 components were found** (see §3).

**Why it slipped through:** CI (`.github/workflows/docs.yml:26`, `runs-on: ubuntu-latest`) runs on GitHub-hosted Ubuntu runners where ripgrep is preinstalled at `/usr/bin/rg`, so the workflow passes. The bug only reproduces in clean environments lacking ripgrep (e.g., a stock macOS/zsh machine without `brew install ripgrep`, or an isolated pre-commit environment). It is a latent portability defect, not a catalog-content defect.

## 2. Approved decisions

- **D1 (catalog is correct — no edit required):** The reported `Catalog is missing component: MotorcycleRAG system` is a **false positive** caused solely by the missing `rg` binary. `6-Docs/catalog.md` already contains every component the validator expects (verified, see §3). **No catalog entry is to be added, removed, or reworded.** The validator's string-matching expectation is also correct. Approved.

- **D2 (search-tool strategy — recommendation: option a):** Replace the lone `rg` invocation with an equivalent `grep` call, removing the ripgrep dependency entirely. The PM may instead choose option (b) or (c) from §6; this plan defaults to (a) and is implementable immediately under that default.

## 3. Investigation findings

**Files examined (read-only):**
- `scripts/validate-docs.sh` (153 lines) — the validator. Full structure: required-files presence check (51–56), required-AGENTS check (58–63), lowercase-agents guard (65–68), **catalog component search (70–87)**, per-component doc-trio check (89–105), canonical-markdown link validation (107–151).
- `6-Docs/catalog.md` (22 lines) — the component catalog; a single Markdown table (rows 7–18).
- `6-Docs/documentation-standard.md` (64 lines) — canonical doc standard; confirms catalog is "the complete component inventory" and each entry needs owner/status/last-reviewed.
- `.github/workflows/docs.yml` (68 lines) — `runs-on: ubuntu-latest`; calls the script at line 42.
- `.pre-commit-config.yaml` (17 lines) — also calls the script (line 6).

**Catalog completeness proof (definitive).** The validator's `components[]` array (lines 70–80) lists 9 strings. Each was confirmed present in `6-Docs/catalog.md` via `grep -F`:

| Validator component (script) | Present in catalog (line) |
| --- | --- |
| `MotorcycleRAG system` | row 7 ✓ |
| `MotorcycleRAG API` | row 8 ✓ |
| `MotorcycleRAG Admin Desktop` | row 9 ✓ |
| `MotorcycleRAG Mobile App` | row 10 ✓ |
| `MotorcycleRAG Web UI and BFF` | row 11 ✓ |
| `Local Processing Service` | row 12 ✓ |
| `Azure Environment` | row 15 ✓ |
| `Database Setup CLI` | row 16 ✓ |
| `SkillForge` | row 17 ✓ |

Conclusion: 9/9 present. The "missing component" message can **only** be produced when the search tool fails to execute, never because an entry is absent.

**Search-tool portability.** `grep` is already used by the script itself at line 150 (`grep -oE '\]\([^)]*\)' "$file"`), so `grep` is a proven-present dependency on every supported platform (BSD grep on macOS, GNU grep on Linux/CI). `grep -Fq` (fixed-string, quiet) is functionally equivalent to `rg --fixed-strings --quiet` for plain-ASCII component names and returns the same exit-code semantics (0 = match, 1 = no match, 2 = error), so the `if ! ...` logic at line 83 behaves identically. The catalog file is guaranteed to exist before line 83 is reached (it is validated in the required-files loop at lines 51–56), so the grep error path (exit 2) cannot fire.

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Fix | `scripts/validate-docs.sh` | Apply D2 (option a): replace the `rg` invocation at line 83 with a portable `grep -Fq` invocation. Exact change in §5. | None (plain bash). `github-devops` adjacent for CI context only. |
| 2 | Verify | `6-Docs/catalog.md` (read-only) | Confirm no catalog edit is needed (per D1). No file change; this task is documentation of the no-op finding. | `app-docs-standard` (read-only reference only). |

## 5. Sequencing / dependency graph

- **Task 1 has no dependencies** and is the only mutating change. Task 2 is verification-only.
- The two reported issues are **not** independent: Issue 2 is a downstream symptom of Issue 1. Fixing Task 1 resolves both simultaneously. There is nothing to parallelize — a single one-line change closes the work.
- Order: Task 1 → (verify via §9).

**Exact change for Task 1 (option a):**

File: `scripts/validate-docs.sh`, line 83.

Pre-fix (before this change):
```bash
  if ! rg --fixed-strings --quiet "$component" 6-Docs/catalog.md; then
```

Replace with (short form):
```bash
  if ! grep -Fq "$component" 6-Docs/catalog.md; then
```

Equivalent long form (also acceptable on both BSD and GNU grep):
```bash
  if ! grep --fixed-strings --quiet "$component" 6-Docs/catalog.md; then
```

No other line in the script references `rg`, so this single edit fully removes the ripgrep dependency.

## 6. Residual decisions / risks

- **D2 option selection (owner: PM).** Three strategies were assessed; recommendation is (a). Trade-offs:
  - **(a) Replace `rg` with `grep` (RECOMMENDED).** Pros: grep is already a script dependency and is present on macOS/Linux/CI; zero new prerequisites; one-line change; fixes both issues; matches the repo's "fix root cause, no workarounds" rule (the root cause is an unnecessary non-portable dependency). Cons: none material (`-F`/`-q` are universally supported; performance on a 22-line file is irrelevant).
  - **(b) Hard-dependency check + document `rg` as prerequisite.** Add `command -v rg` guard near the top and document ripgrep in `CONTRIBUTING.md`/README. Pros: keeps `rg`. Cons: adds contributor friction (`brew install ripgrep` on macOS), inconsistent with the script's existing grep usage, requires editing more files, and local runs still fail until ripgrep is installed. Not recommended.
  - **(c) Auto-detect: `rg` if present, else `grep`.** Pros: works everywhere, keeps rg speed where available. Cons: adds branching/two code paths for a trivial fixed-string search; little benefit. Not recommended.
- **Risk (low):** If a future contributor re-introduces `rg` elsewhere in the script, the portability regression could recur. Mitigation: keep the script grep-based and rely on §9 verification running on a ripgrep-free path.

## 7. Out of scope

- **Catalog content changes.** Out of scope because the catalog is verified complete (D1). Any catalog restructuring belongs in a separate doc-standard task.
- **Rewriting the validator in Python or adding a test harness for shell scripts.** Out of scope; the one-line grep fix is sufficient and the repo has no shell-test framework.
- **CI runner changes.** Out of scope; CI already passes via preinstalled ripgrep and will continue to pass via grep. No `docs.yml` change is required.
- **Installing ripgrep on local machines.** Out of scope; the point of option (a) is to remove that requirement.

## 8. Required skills

- Plain bash for the script edit (no specialized repo skill covers shell scripts).
- `github-devops` — adjacent only, if the implementer wants CI/pre-commit context; not required to make the change.
- `app-docs-standard` — read-only, to confirm the catalog format during verification; no doc edits are produced.

## 9. Verification harness

**Primary gate (must pass on a ripgrep-free PATH):**

1. In an environment where `command -v rg` returns nothing (simulate if needed, e.g. `PATH="$(echo "$PATH" | tr ':' '\n' | grep -v ripgrep | paste -sd: -)"` or a clean checkout), run:
   ```bash
   bash scripts/validate-docs.sh
   ```
   **Success =** exit code 0 and the final line printed exactly:
   ```
   Documentation structure, agent instruction hierarchy, catalog, and local-link validation passed.
   ```
   with **no** `rg: command not found` and **no** `Catalog is missing component:` output.

2. Prove the fix does not silently rely on rg:
   ```bash
   command -v rg || echo "rg absent — script must still pass"
   ```

**Secondary gates:**

3. Syntax check: `bash -n scripts/validate-docs.sh` (exit 0).
4. Confirm grep equivalence directly: `grep -Fq "MotorcycleRAG system" 6-Docs/catalog.md && echo found` prints `found` (proves the search succeeds without rg).
5. CI parity: the Documentation Quality workflow (`.github/workflows/docs.yml`) must still pass on `ubuntu-latest` after the change (it will, since grep is present there too).

**Code review:** Optional `code-review` pass on the one-line diff. No security review needed (no secrets, no new I/O, no privilege change). No Azure validation needed.
