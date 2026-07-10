#!/usr/bin/env python3
"""Use Codex CLI to write tests for the weakest-covered files until the budget is exhausted."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
import shutil
from pathlib import Path
from typing import Any

from coverage_common import (
    CONFIG_PATH,
    REPO_ROOT,
    choose_test_targets_for_suite,
    codex_cli,
    load_config,
    run_command,
    suite_by_name,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=CONFIG_PATH)
    parser.add_argument("--results-dir", type=Path, required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--budget", type=int, default=5)
    parser.add_argument("--threshold", type=float, default=None)
    parser.add_argument("--log-path", type=Path, default=None)
    parser.add_argument("--summary-path", type=Path, default=None)
    parser.add_argument("--model", default="")
    parser.add_argument("--generation-timeout-seconds", type=int, default=900)
    return parser.parse_args()


def load_summary(summary_path: Path) -> dict[str, Any]:
    return json.loads(summary_path.read_text(encoding="utf-8"))


def weakest_candidates(summary: dict[str, Any], threshold: float) -> list[dict[str, Any]]:
    failures = [item for item in summary["weakestFiles"] if item["linePercent"] < threshold]
    if failures:
        return failures
    return summary["weakestFiles"]


def suite_for_candidate(config: dict[str, Any], candidate: dict[str, Any]) -> dict[str, Any]:
    suite_names = candidate.get("suiteNames") or []
    if not suite_names:
        raise RuntimeError(f"No suite mapping found for candidate {candidate['path']}")
    return suite_by_name(config, suite_names[0])


def targeted_coverage(
    *,
    repo_root: Path,
    suite_name: str,
    config_path: Path,
    results_dir: Path,
    configuration: str,
) -> tuple[int, dict[str, Any]]:
    run_args = [
        sys.executable,
        str((repo_root / "5-Test/scripts/run_unit_coverage.py").resolve()),
        "--config",
        str(config_path.resolve()),
        "--results-dir",
        str(results_dir.resolve()),
        "--configuration",
        configuration,
        "--suite",
        suite_name,
        "--threshold",
        "0",
    ]
    exit_code = run_command(run_args, cwd=repo_root)
    summary_path = results_dir / "CoverageReport" / "coverage-summary.json"
    return exit_code, load_summary(summary_path)


def find_candidate_coverage(summary: dict[str, Any], path: str) -> float | None:
    for entry in summary["weakestFiles"]:
        if entry["path"] == path:
            return float(entry["linePercent"])
    return None


def build_prompt(candidate: dict[str, Any], suite: dict[str, Any], threshold: float) -> str:
    targets = choose_test_targets_for_suite(suite)
    return f"""
Add targeted unit tests for `{candidate['path']}` in this repository.

Constraints:
- Only edit or create tests in these targets: {", ".join(targets)}
- Do not modify production code.
- No snapshot or golden tests.
- Prefer branch, validation, failure-mode, mapping, and boundary-condition coverage.
- Avoid tests that lock in incidental implementation details.
- Keep tests deterministic and idiomatic for the existing suite.
- The current file line coverage is {candidate['linePercent']:.2f}% and the repo threshold is {threshold:.2f}%.

Outcome:
- Add the smallest valuable batch of unit tests that should materially improve coverage for `{candidate['path']}`.
- Stop after writing tests; do not change CI or tooling.
""".strip()


def write_logs(
    log_entries: list[dict[str, Any]],
    *,
    budget: int,
    threshold: float,
    log_path: Path,
) -> None:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.write_text(json.dumps(log_entries, indent=2) + "\n", encoding="utf-8")

    markdown_path = log_path.with_suffix(".md")
    markdown_lines = [
        "# Auto-Generated Coverage Test Run",
        "",
        f"- Budget: `{budget}`",
        f"- Threshold: `{threshold:.2f}%`",
        "",
    ]
    for entry in log_entries:
        after_value = entry.get("afterLinePercent")
        after_text = "n/a" if after_value is None else f"{after_value:.2f}%"
        markdown_lines.append(
            f"- `{entry['candidate']}` via `{entry['suite']}`: `{entry['status']}` "
            f"(before `{entry['beforeLinePercent']:.2f}%`, after `{after_text}`)"
        )
    markdown_path.write_text("\n".join(markdown_lines) + "\n", encoding="utf-8")


def repo_relative_path(repo_root: Path, path: Path) -> Path:
    return path.resolve().relative_to(repo_root.resolve())


def create_temp_worktree(repo_root: Path) -> Path:
    temp_dir = Path(tempfile.mkdtemp(prefix="coverage-autogen-worktree-"))
    subprocess.run(
        ["git", "worktree", "add", "--detach", str(temp_dir), "HEAD"],
        cwd=repo_root,
        check=True,
        capture_output=True,
        text=True,
    )

    result = subprocess.run(
        ["git", "status", "--porcelain"],
        cwd=repo_root,
        capture_output=True,
        text=True,
        check=False,
    )
    for line in result.stdout.splitlines():
        if len(line) < 4:
            continue
        relative_path = line[3:]
        file_path = repo_root / relative_path
        target_path = temp_dir / relative_path

        if file_path.is_dir():
            if target_path.exists():
                shutil.rmtree(target_path)
            shutil.copytree(file_path, target_path)
            continue

        if file_path.is_file():
            target_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file_path, target_path)
            continue

        if target_path.exists():
            if target_path.is_dir():
                shutil.rmtree(target_path)
            else:
                target_path.unlink()

    return temp_dir


def remove_temp_worktree(repo_root: Path, temp_dir: Path) -> None:
    subprocess.run(
        ["git", "worktree", "remove", "--force", str(temp_dir)],
        cwd=repo_root,
        check=True,
        capture_output=True,
        text=True,
    )


def sync_suite_test_changes(repo_root: Path, temp_repo_root: Path, suite: dict[str, Any]) -> None:
    sync_paths = {Path(path) for path in suite.get("testRoots", [])}
    if suite.get("project"):
        sync_paths.add(Path(suite["project"]))

    for relative_path in sorted(sync_paths):
        source = temp_repo_root / relative_path
        target = repo_root / relative_path
        if not source.exists():
            continue

        if source.is_dir():
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(source, target)
            continue

        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def main() -> int:
    args = parse_args()
    config = load_config(args.config)
    threshold = args.threshold or float(config["thresholds"]["fileLinePercent"])
    results_dir = args.results_dir.resolve()
    summary_path = args.summary_path or (results_dir / "CoverageReport" / "coverage-summary.json")
    log_path = args.log_path or (results_dir / "auto-generation-log.json")
    summary = load_summary(summary_path)
    budget = max(args.budget, 0)
    log_entries: list[dict[str, Any]] = []

    for candidate in weakest_candidates(summary, threshold)[:budget]:
        suite = suite_for_candidate(config, candidate)
        prompt = build_prompt(candidate, suite, threshold)
        before = float(candidate["linePercent"])
        log_entry: dict[str, Any] = {
            "candidate": candidate["path"],
            "suite": suite["name"],
            "beforeLinePercent": before,
            "status": "attempted",
        }

        last_message_file = Path(tempfile.mkstemp(prefix="codex-last-message-", suffix=".txt")[1])
        temp_repo_root = create_temp_worktree(REPO_ROOT)
        try:
            temp_config_path = temp_repo_root / repo_relative_path(REPO_ROOT, args.config.resolve())
            command = [
                codex_cli(),
                "exec",
                "--cd",
                str(temp_repo_root),
                "--skip-git-repo-check",
                "--dangerously-bypass-approvals-and-sandbox",
                "--output-last-message",
                str(last_message_file),
            ]
            if args.model:
                command.extend(["--model", args.model])
            command.append(prompt)
            process = subprocess.run(
                command,
                cwd=temp_repo_root,
                check=False,
                timeout=max(args.generation_timeout_seconds, 1),
            )
            codex_exit = process.returncode
            log_entry["codexExitCode"] = codex_exit
            log_entry["codexLastMessagePath"] = str(last_message_file)

            targeted_results_dir = results_dir / "autogen" / suite["name"]
            exit_code, targeted_summary = targeted_coverage(
                repo_root=temp_repo_root,
                suite_name=suite["name"],
                config_path=temp_config_path,
                results_dir=targeted_results_dir,
                configuration=args.configuration,
            )
            after = find_candidate_coverage(targeted_summary, candidate["path"])
            log_entry["targetedCoverageExitCode"] = exit_code
            log_entry["afterLinePercent"] = after

            if codex_exit != 0 or after is None or after <= before:
                log_entry["status"] = "rejected"
            else:
                log_entry["status"] = "accepted"
                sync_suite_test_changes(REPO_ROOT, temp_repo_root, suite)
                summary = targeted_summary
        except subprocess.TimeoutExpired as exc:
            log_entry["status"] = "timed_out"
            log_entry["error"] = (
                f"codex exec exceeded timeout after {max(args.generation_timeout_seconds, 1)} seconds"
            )
            if exc.stdout:
                log_entry["codexStdout"] = exc.stdout.decode("utf-8", errors="replace")
            if exc.stderr:
                log_entry["codexStderr"] = exc.stderr.decode("utf-8", errors="replace")
        except Exception as exc:  # pragma: no cover - defensive logging
            log_entry["status"] = "error"
            log_entry["error"] = str(exc)
        finally:
            log_entries.append(log_entry)
            write_logs(log_entries, budget=budget, threshold=threshold, log_path=log_path)
            remove_temp_worktree(REPO_ROOT, temp_repo_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
