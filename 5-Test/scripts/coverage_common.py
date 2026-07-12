#!/usr/bin/env python3
"""Shared helpers for repo coverage tooling."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
CONFIG_PATH = SCRIPT_DIR / "coverage-config.json"


def load_config(config_path: Path | None = None) -> dict[str, Any]:
    path = config_path or CONFIG_PATH
    return json.loads(path.read_text(encoding="utf-8"))


def current_platform() -> str:
    if sys.platform.startswith("linux"):
        return "linux"
    if sys.platform == "darwin":
        return "darwin"
    if sys.platform in {"win32", "cygwin"}:
        return "win32"
    return sys.platform


def repo_relative(path: Path) -> str:
    return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()


def normalize_repo_path(path_text: str) -> str:
    text = path_text.replace("\\", "/")
    candidate = Path(text)
    if candidate.is_absolute():
        try:
            return repo_relative(candidate)
        except ValueError:
            return candidate.as_posix()
    return Path(text).as_posix().lstrip("./")


def suite_by_name(config: dict[str, Any], name: str) -> dict[str, Any]:
    for suite in config["suites"]:
        if suite["name"] == name:
            return suite
    raise KeyError(f"Unknown suite: {name}")


def suite_results_dir(results_root: Path, suite: dict[str, Any]) -> Path:
    return results_root / suite["resultsSubdirectory"]


def ensure_clean_dir(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def resolve_python_executable(service_dir: Path) -> list[str]:
    candidates = [
        service_dir / ".venv" / "bin" / "python",
        service_dir / ".venv" / "Scripts" / "python.exe",
    ]
    for candidate in candidates:
        if candidate.exists():
            return [str(candidate)]
    for name in ("python3", "python", "py"):
        resolved = shutil.which(name)
        if resolved:
            if name == "py":
                return [resolved, "-3"]
            return [resolved]
    raise FileNotFoundError("Unable to locate Python runtime for coverage tooling.")


def run_command(
    args: list[str],
    *,
    cwd: Path,
    env: dict[str, str] | None = None,
) -> int:
    merged_env = os.environ.copy()
    if env:
        merged_env.update(env)

    process = subprocess.run(args, cwd=cwd, env=merged_env, check=False)
    return process.returncode


def find_latest_file(root: Path, pattern: str) -> Path | None:
    matches = sorted(root.rglob(pattern), key=lambda item: item.stat().st_mtime, reverse=True)
    return matches[0] if matches else None


def platform_supported(suite: dict[str, Any], platform_name: str | None = None) -> bool:
    current = platform_name or current_platform()
    return current in set(suite.get("supportedPlatforms", []))


def detect_changed_files(repo_root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "status", "--porcelain"],
        cwd=repo_root,
        capture_output=True,
        text=True,
        check=False,
    )
    files: list[str] = []
    for line in result.stdout.splitlines():
        if len(line) < 4:
            continue
        files.append(line[3:])
    return files


def ensure_clean_worktree(repo_root: Path) -> None:
    changed = detect_changed_files(repo_root)
    if changed:
        raise RuntimeError(
            "Auto-generation requires a clean worktree. "
            f"Found {len(changed)} changed paths."
        )


def git_hard_reset(repo_root: Path) -> None:
    subprocess.run(["git", "reset", "--hard", "HEAD"], cwd=repo_root, check=True)
    subprocess.run(["git", "clean", "-fd"], cwd=repo_root, check=True)


def codex_cli() -> str:
    cli = shutil.which("codex")
    if not cli:
        raise FileNotFoundError("codex CLI is required for auto-generation.")
    return cli


def choose_test_targets_for_suite(suite: dict[str, Any]) -> list[str]:
    targets = suite.get("testRoots", [])
    if suite["kind"] == "dotnet":
        return [suite["project"]]
    return targets
