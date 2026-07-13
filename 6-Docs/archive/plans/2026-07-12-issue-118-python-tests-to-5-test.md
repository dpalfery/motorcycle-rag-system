# Move Python test suite to 5-Test

**Status:** Archived
**Date:** 2026-07-12
**Archived:** 2026-07-12
**Goal:** Relocate the Python local-processing-service test suite from `2-Application/local-processing-service/tests/` to `5-Test/local-processing-service.Tests/`, so every test suite in the repository lives under `5-Test/`, and update all configuration, CI, and documentation that assumes the old path. Tracks [issue #118](https://github.com/dpalfery/motorcycle-rag-system/issues/118).

## Decision

Option A from issue #118: **move the tests** rather than document colocation as an exception. Rationale (per the issue author): having all tests in one place is preferable to hunting for colocated tests when it is technically feasible, and it is feasible here — nothing about the processor's tests requires living next to `src/`.

## Current state (verified on `develop`, 2026-07-12)

- 24 pytest modules plus support files live under `2-Application/local-processing-service/tests/`, organized as: top-level `test_*.py`, `embeddings/`, `integration/`, `search/` (empty package placeholder), and `support/in_memory_search_shim.py`.
- **`tests/` is a Python package.** `__init__.py` exists at `tests/`, `tests/embeddings/`, `tests/integration/`, and `tests/search/`. `tests/support/` has no `__init__.py`.
- **Nothing imports the test package.** No file does `from tests …`, `import tests`, `from support …`, `from search …`, relative imports, or `sys.path` manipulation. The `__init__.py` files are effectively unused for imports.
- Tests import their subjects from `src/` via `pythonpath = ["src"]`, e.g. `from api.api_client import ApiClient`, `import embeddings.openai_embedder`. `src/api/` and `src/embeddings/` both exist.
- `tests/support/in_memory_search_shim.py` is a **standalone HTTP shim**, launched as a separate process and reached over `MCR_E2E_SEARCH_SHIM_URL`; it is not imported or collected by pytest. Repo-wide grep finds **no** references to its path outside the tests tree, so relocating it requires no follow-up edits.
- The service `pyproject.toml` is the **only** pytest config in the repo — there is no root or `5-Test/` `pyproject.toml`/`pytest.ini`/`tox.ini`/`setup.cfg`/`conftest.py` that could hijack rootdir discovery.
- A project-local `.venv` (`2-Application/local-processing-service/.venv`, CPython 3.14 via uv) is present, so the move can be verified locally with a real pytest run.

## Technical gotchas discovered (why this is not a plain `git mv`)

1. **Dotted directory name vs. Python package name.** The target dir `local-processing-service.Tests` contains `.` and `-`, which are illegal in a Python module/package name. Under pytest's default `prepend` import mode, a top-level `__init__.py` in the moved directory would make pytest derive the module name `local-processing-service.Tests.<mod>`, which is invalid and fails collection. So the moved top directory must **not** be an importable package.

2. **`embeddings` package-name collision.** If we removed only the top-level `__init__.py` but kept `tests/embeddings/__init__.py`, pytest would insert the moved directory onto `sys.path` and import the test subpackage as top-level `embeddings` — colliding with the **source** `embeddings` package (`src/embeddings`). Tests that do `import embeddings.openai_embedder` would then resolve to the test package and fail. Removing **all** `__init__.py` avoids this: each test module is imported by basename, `import embeddings…` cleanly resolves to `src/embeddings` via `pythonpath`, and the directory name becomes irrelevant to imports. Basenames are unique across the whole tree (verified), so `prepend` mode has no basename clash.

3. **Rootdir discovery when `testpaths` points outside the config dir.** pytest must keep rootdir at `2-Application/local-processing-service/` so `pythonpath = ["src"]`, `asyncio_mode = auto`, and the custom markers load. If the coverage runner passes the external test directory as a **positional** argument, pytest's rootdir search starts at that path and, finding no config upward (there is no root pytest config), would pick the wrong rootdir and drop the service's pytest settings — breaking `import`s and async tests. Fix: invoke pytest **from** the service directory with **no positional path**, letting `testpaths` drive collection (rootdir is then the cwd, where the config lives), and repoint `--ignore` at the new integration path.

## Chosen approach

- Target directory: `5-Test/local-processing-service.Tests/` (matches the `.Tests` sibling convention and the issue text).
- **Remove all four `__init__.py` files** during the move; rely on `prepend`-mode basename collection (basenames are unique). The empty `search/` package placeholder disappears with its `__init__.py` — acceptable, it holds no tests.
- Keep rootdir at the service directory by driving collection through `testpaths` and running pytest from that directory (never passing the external path positionally).

## Change list

1. **Move files.** `git mv` the contents of `2-Application/local-processing-service/tests/` into `5-Test/local-processing-service.Tests/`, preserving `embeddings/`, `integration/`, and `support/`. Then `git rm` the four `__init__.py` files (top-level, `embeddings/`, `integration/`, `search/`).

2. **`5-Test/local-processing-service.Tests/integration/test_real_pdf_processing.py`** (line ~116): change `Path(__file__).resolve().parents[4]` → `parents[3]`. New depth: `integration` → `local-processing-service.Tests` → `5-Test` → repo root = 3 parents. This is the only file in the suite using `__file__`/`parents[]` path math (verified).

3. **`2-Application/local-processing-service/pyproject.toml`** `[tool.pytest.ini_options]`: `testpaths = ["tests"]` → `testpaths = ["../../5-Test/local-processing-service.Tests"]`. **Leave `pythonpath = ["src"]` unchanged** — it resolves relative to the config dir (rootdir), which does not move. (`namespace_packages = true` at ~line 89 is under `[tool.mypy]`, not pytest — do not touch for this change.)

4. **`5-Test/scripts/run_unit_coverage.py`** `run_python_suite()` (lines ~92–103): keep `cwd = workingDirectory` (the service dir) and `--cov=src`. Drop the positional `"tests"` argument (let `testpaths` drive collection so rootdir stays correct). Change `--ignore=tests/integration` → `--ignore=../../5-Test/local-processing-service.Tests/integration`.

5. **`5-Test/scripts/coverage-config.json`** `python-unit` suite: `testRoots` `2-Application/local-processing-service/tests/` → `5-Test/local-processing-service.Tests/`. Keep `workingDirectory: 2-Application/local-processing-service` and `sourceRoots: 2-Application/local-processing-service/src/` unchanged. (Note: `testRoots` is descriptive metadata not consumed by the runner today, but keep it accurate.)

6. **`.github/workflows/pr-gate.yml`** — the `changes` job `paths-filter` `code` group (line ~49) only watches `2-Application/local-processing-service/**/*.py`. Add `5-Test/local-processing-service.Tests/**/*.py` (or a broader `5-Test/**/*.py`) so a test-only PR still sets the `code` flag and runs the `python-unit` suite. **Without this, CI silently skips Python tests on test-only PRs.** This is the only workflow step that runs `python-unit` in pr-gate (the second `run_unit_coverage.py` call at line ~357 is `mobileapp-unit`).

7. **`.github/workflows/nightly.yml`** — runs `python-unit` (lines ~89/94) but has **no** `paths-filter` (schedule-triggered full run), so no trigger change is needed. Verify no hardcoded old test path remains.

8. **Add `5-Test/local-processing-service.Tests/AGENTS.md`** mirroring the sibling pattern (e.g. `5-Test/MotorcycleRAG.MobileApp.Tests/AGENTS.md`): "Applies to" scope line, "Read before changing" links to processor docs, scoped constraints (deterministic, no token/PII logging, local-first boundaries), and a "Verify" command.

9. **`2-Application/local-processing-service/AGENTS.md`** — update the "Verify" note to say the suite now lives under `5-Test/local-processing-service.Tests/` while the run command (`.venv/bin/python -m pytest` from the service dir) still works via `testpaths`.

10. **`6-Docs/rules/architecture-general.md`** "5-Test Layer" section (~line 310) currently lists only the five .NET test projects. Add the Python `local-processing-service.Tests` suite so the section matches the folder contents.

## Verification

- `.venv/bin/python -m pytest` from `2-Application/local-processing-service/` discovers and runs the full suite from the new location (rootdir stays correct; `src` imports resolve; async tests run).
- `python3 5-Test/scripts/run_unit_coverage.py --suite python-unit` passes, ignores `integration`, and reports coverage on `src`.
- Confirm no basename collision / "import file mismatch" errors after removing `__init__.py`.
- `git grep` for `local-processing-service/tests` and `local-processing-service.*testpaths` shows no stale live references (archived plans under `6-Docs/archive/**` are intentionally left as historical record).

## Acceptance criteria

- [x] `2-Application/local-processing-service/tests/` no longer exists; contents live at `5-Test/local-processing-service.Tests/` with `embeddings/`, `integration/`, `support/` preserved and `__init__.py` files removed.
- [x] `parents[4]` → `parents[3]` in the relocated `test_real_pdf_processing.py`.
- [x] `pyproject.toml` `testpaths` repointed; `.venv/bin/python -m pytest` from the service dir runs the full suite green (394 tests passing, per python-dev verification).
- [x] `run_unit_coverage.py` and `coverage-config.json` updated; `run_unit_coverage.py --suite python-unit` passes.
- [x] `pr-gate.yml` `code` filter includes the new path; a PR touching only `5-Test/local-processing-service.Tests/**` triggers `build-test`.
- [x] New `5-Test/local-processing-service.Tests/AGENTS.md` added following the sibling pattern.
- [x] `6-Docs/rules/architecture-general.md` 5-Test section and `2-Application/local-processing-service/AGENTS.md` reflect the new location.
- [x] No stale live references to the old path remain.

## Out of scope

- Web UI and Admin Desktop colocated tests (`1-Presentation/MotorcycleRag.WebUI/tests`, `.../src/test`, `1-Presentation/MotorcycleRAG.AdminDesktop/src/test`) — same pattern, but a separate decision/issue.
- Archived plan references to the old path under `6-Docs/archive/**` — historical, intentionally not edited.

## Progress

- 2026-07-12: Research complete (current-state inventory, three collection gotchas, full change list, verification approach). Implementation not yet started. Plan recorded from issue #118 research.
- 2026-07-12: Implementation complete across three parallel tasks, each individually reviewed and approved:
  - **Tests moved** (python-dev): `2-Application/local-processing-service/tests/` relocated to `5-Test/local-processing-service.Tests/`, `embeddings/`, `integration/`, and `support/` preserved, all four `__init__.py` files removed, `parents[4]` → `parents[3]` fixed in `integration/test_real_pdf_processing.py`. `pyproject.toml` `testpaths` repointed at the new location; `run_unit_coverage.py` and `coverage-config.json` updated to match. Full suite (394 tests) verified passing via `.venv/bin/python -m pytest` from the service directory.
  - **CI updated** (github-devops): `pr-gate.yml` `changes` job `code` path filter extended with `5-Test/local-processing-service.Tests/**/*.py`, so a test-only PR still sets the `code` flag and triggers `build-test`/`python-unit`. `nightly.yml` confirmed to have no hardcoded old path (schedule-triggered full run, no filter needed).
  - **Docs updated** (docs-dev): New `5-Test/local-processing-service.Tests/AGENTS.md` added following the `.Tests` sibling pattern (scope, read-before-changing links, scoped constraints incl. the no-`__init__.py` collection rule, verify command). `2-Application/local-processing-service/AGENTS.md` "Verify" section updated to point at the relocated suite while confirming the run command is unchanged. `6-Docs/rules/architecture-general.md` 5-Test Layer section now lists `local-processing-service.Tests` alongside the .NET test projects.
  - **Closeout verification** (docs-dev): Confirmed via direct filesystem/grep checks — old path absent, new path present with expected structure and no `__init__.py` files, `parents[3]` in place, `pyproject.toml`/`run_unit_coverage.py`/`coverage-config.json` all repointed consistently, `pr-gate.yml` filter includes the new path, both AGENTS.md files and `architecture-general.md` updated, and a repo-wide grep (excluding `6-Docs/archive/**`) for the literal old path string `local-processing-service/tests` found no live references outside this plan document's own historical narrative. All 8 acceptance criteria met. Plan archived; see `6-Docs/plans/README.md` archive register.
