#!/usr/bin/env python3
"""Run all in-scope unit suites with coverage and aggregate the results."""

from __future__ import annotations

import argparse
import json
import shlex
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

from coverage_common import (
    CONFIG_PATH,
    REPO_ROOT,
    current_platform,
    ensure_clean_dir,
    find_latest_file,
    load_config,
    platform_supported,
    resolve_python_executable,
    run_command,
    suite_by_name,
    suite_results_dir,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=CONFIG_PATH)
    parser.add_argument("--results-dir", type=Path, default=REPO_ROOT / "TestResults" / "UnitCoverage")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--threshold", type=float, default=None)
    parser.add_argument("--suite", action="append", default=[])
    parser.add_argument("--open-report", action="store_true")
    parser.add_argument("--skip-aggregate", action="store_true")
    return parser.parse_args()


def write_suite_result(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def local_name(tag: str) -> str:
    """Return an XML element name without its optional namespace."""
    return tag.rsplit("}", 1)[-1]


def child_text(element: ET.Element, name: str) -> str | None:
    """Return the trimmed text for the first descendant with the supplied local name."""
    for candidate in element.iter():
        if local_name(candidate.tag) == name and candidate.text:
            return candidate.text.strip()
    return None


def parse_trx_failures(results_dir: Path) -> list[dict[str, str]]:
    """Extract failed test names and messages from dotnet's TRX results."""
    failures: list[dict[str, str]] = []
    for trx_path in sorted(results_dir.rglob("*.trx")):
        try:
            root = ET.parse(trx_path).getroot()
        except ET.ParseError:
            continue

        for result in root.iter():
            if local_name(result.tag) != "UnitTestResult" or result.attrib.get("outcome") != "Failed":
                continue
            failure = {"name": result.attrib.get("testName", "Unnamed test")}
            message = child_text(result, "Message")
            if message:
                failure["message"] = " ".join(message.split())
            failures.append(failure)
    return failures


def emit_test_execution_summary(summaries: list[dict[str, Any]]) -> None:
    """Print test-suite failures before the independent coverage-policy result."""
    failed_summaries = [summary for summary in summaries if summary["exitCode"] != 0]
    if not failed_summaries:
        return

    print()
    print("=" * 80)
    print("UNIT TEST EXECUTION")
    print("Result: FAIL — one or more unit test suites returned a non-zero exit code.")
    for summary in failed_summaries:
        print(f"- {summary['name']}: exit code {summary['exitCode']}.")
        failures = summary.get("testFailures", [])
        for failure in failures:
            print(f"  - Failed test: {failure['name']}")
            if failure.get("message"):
                print(f"    Reason: {failure['message']}")
        print(f"  Results: {summary['resultsDirectory']}")
    print("Coverage is evaluated separately below; a coverage PASS does not override test failures.")
    print("=" * 80)

    first_failure = failed_summaries[0]
    failed_test = next(iter(first_failure.get("testFailures", [])), None)
    detail = failed_test["name"] if failed_test else first_failure["name"]
    print(
        "::error title=Unit test execution failed::"
        f"{first_failure['name']} returned exit code {first_failure['exitCode']}: {detail}"
    )


def run_dotnet_suite(
    *,
    suite: dict[str, Any],
    configuration: str,
    results_dir: Path,
) -> dict[str, Any]:
    suite_dir = suite_results_dir(results_dir, suite)
    ensure_clean_dir(suite_dir)

    # Resolve target: solution-level (.sln / .slnf) or single project
    target = suite.get("solution") or suite["project"]

    args = [
        "dotnet",
        "test",
        target,
        "--configuration",
        configuration,
        "--logger",
        "trx",
        "--results-directory",
        str(suite_dir),
        '--collect:XPlat Code Coverage',
        "--settings",
        "coverlet.runsettings",
    ]

    # Append --filter when targeting a solution with a suite-level filter
    if suite.get("solution") and suite.get("filter"):
        args.extend(["--filter", suite["filter"]])

    exit_code = run_command(args, cwd=REPO_ROOT)

    # Collect all coverage.cobertura.xml files (one per test project)
    coverage_files = sorted(suite_dir.rglob("coverage.cobertura.xml"))
    coverage_paths = [str(p.resolve()) for p in coverage_files]
    first_coverage = coverage_paths[0] if coverage_paths else None

    return {
        "name": suite["name"],
        "kind": suite["kind"],
        "status": "passed" if exit_code == 0 else "failed",
        "exitCode": exit_code,
        "coveragePath": first_coverage,
        "coveragePaths": coverage_paths,
        "resultsDirectory": str(suite_dir.resolve()),
        "command": args,
        "testFailures": parse_trx_failures(suite_dir),
    }


def run_python_suite(
    *,
    suite: dict[str, Any],
    results_dir: Path,
) -> dict[str, Any]:
    suite_dir = suite_results_dir(results_dir, suite)
    ensure_clean_dir(suite_dir)
    workdir = REPO_ROOT / suite["workingDirectory"]
    coverage_path = suite_dir / "coverage.cobertura.xml"
    python_command = resolve_python_executable(workdir)
    args = [
        *python_command,
        "-m",
        "pytest",
        "--ignore=../../5-Test/local-processing-service.Tests/integration",
        "-m",
        "not slow",
        "--cov=src",
        f"--cov-report=xml:{coverage_path}",
        "--cov-report=term-missing:skip-covered",
    ]
    exit_code = run_command(args, cwd=workdir)
    return {
        "name": suite["name"],
        "kind": suite["kind"],
        "status": "passed" if exit_code == 0 else "failed",
        "exitCode": exit_code,
        "coveragePath": str(coverage_path.resolve()) if coverage_path.exists() else None,
        "resultsDirectory": str(suite_dir.resolve()),
        "command": args,
        "testFailures": [],
    }


def run_node_suite(
    *,
    suite: dict[str, Any],
    results_dir: Path,
) -> dict[str, Any]:
    suite_dir = suite_results_dir(results_dir, suite)
    ensure_clean_dir(suite_dir)
    workdir = REPO_ROOT / suite["workingDirectory"]
    args = [
        "npm",
        "run",
        "test",
        "--",
        "--coverage",
        "--coverage.provider=v8",
        "--coverage.reporter=cobertura",
        f"--coverage.reportsDirectory={suite_dir}",
    ]
    exit_code = run_command(args, cwd=workdir)
    coverage_file = find_latest_file(suite_dir, "cobertura-coverage.xml")
    if coverage_file is None:
        coverage_file = find_latest_file(suite_dir, "coverage.cobertura.xml")
    return {
        "name": suite["name"],
        "kind": suite["kind"],
        "status": "passed" if exit_code == 0 else "failed",
        "exitCode": exit_code,
        "coveragePath": None if coverage_file is None else str(coverage_file.resolve()),
        "resultsDirectory": str(suite_dir.resolve()),
        "command": args,
        "testFailures": [],
    }


def open_report(report_path: Path) -> None:
    if sys.platform == "darwin":
        subprocess.run(["open", str(report_path)], check=False)
    elif sys.platform.startswith("linux"):
        subprocess.run(["xdg-open", str(report_path)], check=False)
    elif sys.platform == "win32":
        subprocess.run(["cmd", "/c", "start", "", str(report_path)], check=False)


def main() -> int:
    args = parse_args()
    config = load_config(args.config)
    results_dir = args.results_dir.resolve()
    ensure_clean_dir(results_dir)
    selected_suite_names = args.suite or [suite["name"] for suite in config["suites"]]
    platform_name = current_platform()
    failures: list[str] = []
    suite_summaries: list[dict[str, Any]] = []

    for suite_name in selected_suite_names:
        suite = suite_by_name(config, suite_name)
        suite_summary_path = suite_results_dir(results_dir, suite) / "suite-result.json"
        if not platform_supported(suite, platform_name):
            summary = {
                "name": suite["name"],
                "kind": suite["kind"],
                "status": "skipped",
                "reason": f"unsupported platform: {platform_name}",
                "exitCode": 0,
                "coveragePath": None,
            }
            write_suite_result(suite_summary_path, summary)
            suite_summaries.append(summary)
            continue

        if suite["kind"] == "dotnet":
            summary = run_dotnet_suite(suite=suite, configuration=args.configuration, results_dir=results_dir)
        elif suite["kind"] == "python":
            summary = run_python_suite(suite=suite, results_dir=results_dir)
        elif suite["kind"] == "node":
            summary = run_node_suite(suite=suite, results_dir=results_dir)
        else:
            raise ValueError(f"Unsupported suite kind: {suite['kind']}")

        write_suite_result(suite_summary_path, summary)
        suite_summaries.append(summary)
        if summary["exitCode"] != 0:
            failures.append(suite["name"])

    aggregate_exit_code = 0
    emit_test_execution_summary(suite_summaries)
    if not args.skip_aggregate:
        aggregate_args = [
            sys.executable,
            str((Path(__file__).resolve().parent / "aggregate_coverage.py")),
            "--config",
            str(args.config.resolve()),
            "--results-dir",
            str(results_dir),
            "--output-dir",
            str(results_dir / "CoverageReport"),
        ]
        if args.threshold is not None:
            aggregate_args.extend(["--threshold", str(args.threshold)])
        for suite_name in args.suite:
            aggregate_args.extend(["--suite", suite_name])
        aggregate_exit_code = run_command(aggregate_args, cwd=REPO_ROOT)
        if args.open_report:
            open_report(results_dir / "CoverageReport" / "coverage-summary.html")

    return 1 if failures or aggregate_exit_code != 0 else 0


if __name__ == "__main__":
    raise SystemExit(main())
