#!/usr/bin/env python3
"""Merge and summarize coverage artifacts across repo unit suites."""

from __future__ import annotations

import argparse
import json
import xml.etree.ElementTree as ET
from defusedxml import ElementTree as DefusedElementTree
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from coverage_common import REPO_ROOT, load_config, normalize_repo_path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=None)
    parser.add_argument("--results-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--summary-path", type=Path, default=None)
    parser.add_argument("--threshold", type=float, default=None)
    return parser.parse_args()


@dataclass
class FileMetrics:
    path: str
    suite_names: set[str] = field(default_factory=set)
    line_hits: int = 0
    line_total: int = 0
    branch_hits: int = 0
    branch_total: int = 0
    class_names: set[str] = field(default_factory=set)
    lines: dict[int, dict[str, int]] = field(default_factory=dict)

    def line_percent(self) -> float:
        if self.line_total == 0:
            return 100.0
        return (self.line_hits / self.line_total) * 100.0

    def branch_percent(self) -> float | None:
        if self.branch_total == 0:
            return None
        return (self.branch_hits / self.branch_total) * 100.0

    def uncovered_lines(self) -> int:
        return max(self.line_total - self.line_hits, 0)


@dataclass
class ClassMetrics:
    suite_name: str
    file_path: str
    class_name: str
    line_hits: int
    line_total: int
    branch_hits: int
    branch_total: int

    def line_percent(self) -> float:
        if self.line_total == 0:
            return 100.0
        return (self.line_hits / self.line_total) * 100.0

    def branch_percent(self) -> float | None:
        if self.branch_total == 0:
            return None
        return (self.branch_hits / self.branch_total) * 100.0

    def uncovered_lines(self) -> int:
        return max(self.line_total - self.line_hits, 0)


def should_exclude(path_text: str, exclusions: dict[str, Any]) -> bool:
    normalized = path_text.replace("\\", "/")
    for prefix in exclusions.get("pathPrefixes", []):
        if normalized.startswith(prefix.replace("\\", "/")):
            return True
    for fragment in exclusions.get("pathContains", []):
        if fragment.replace("\\", "/") in normalized:
            return True
    return False


def branch_counts(line_element: ET.Element) -> tuple[int, int]:
    if line_element.attrib.get("branch") != "true":
        return 0, 0
    condition_coverage = line_element.attrib.get("condition-coverage", "")
    if "(" not in condition_coverage or "/" not in condition_coverage:
        return 0, 0
    counts = condition_coverage.split("(", 1)[1].rstrip(")")
    covered_text, total_text = counts.split("/", 1)
    try:
        return int(covered_text.strip()), int(total_text.strip())
    except ValueError:
        return 0, 0


def parse_cobertura_file(
    *,
    suite_name: str,
    coverage_path: Path,
    file_metrics: dict[str, FileMetrics],
    class_metrics: list[ClassMetrics],
    exclusions: dict[str, Any],
) -> None:
    root = DefusedElementTree.parse(coverage_path).getroot()
    for class_element in root.findall(".//class"):
        raw_filename = class_element.attrib.get("filename")
        if not raw_filename:
            continue

        normalized_path = normalize_repo_path(raw_filename)
        if should_exclude(normalized_path, exclusions):
            continue

        line_hits = 0
        line_total = 0
        branch_hits_total = 0
        branch_total = 0
        for line_element in class_element.findall("./lines/line"):
            hits = int(line_element.attrib.get("hits", "0"))
            line_number = int(line_element.attrib.get("number", "0"))
            line_total += 1
            if hits > 0:
                line_hits += 1
            branch_hits, branch_valid = branch_counts(line_element)
            branch_hits_total += branch_hits
            branch_total += branch_valid

        class_name = class_element.attrib.get("name", normalized_path)
        class_metrics.append(
            ClassMetrics(
                suite_name=suite_name,
                file_path=normalized_path,
                class_name=class_name,
                line_hits=line_hits,
                line_total=line_total,
                branch_hits=branch_hits_total,
                branch_total=branch_total,
            )
        )

        aggregate = file_metrics.setdefault(normalized_path, FileMetrics(path=normalized_path))
        aggregate.suite_names.add(suite_name)
        aggregate.class_names.add(class_name)
        aggregate.line_hits += line_hits
        aggregate.line_total += line_total
        aggregate.branch_hits += branch_hits_total
        aggregate.branch_total += branch_total
        for line_element in class_element.findall("./lines/line"):
            line_number = int(line_element.attrib.get("number", "0"))
            hits = int(line_element.attrib.get("hits", "0"))
            branch_hits, branch_valid = branch_counts(line_element)
            current = aggregate.lines.setdefault(
                line_number,
                {"hits": 0, "branchHits": 0, "branchTotal": 0},
            )
            current["hits"] = max(current["hits"], hits)
            current["branchHits"] = max(current["branchHits"], branch_hits)
            current["branchTotal"] = max(current["branchTotal"], branch_valid)


def sort_rank_key(metric: FileMetrics | ClassMetrics) -> tuple[float, int, float, str]:
    branch_percent = metric.branch_percent()
    branch_value = branch_percent if branch_percent is not None else 100.0
    name = metric.path if isinstance(metric, FileMetrics) else f"{metric.file_path}:{metric.class_name}"
    return (
        round(metric.line_percent(), 4),
        -metric.uncovered_lines(),
        round(branch_value, 4),
        name,
    )


def emit_merged_cobertura(
    output_path: Path,
    file_metrics: dict[str, FileMetrics],
    class_metrics: list[ClassMetrics],
) -> None:
    coverage = ET.Element("coverage")
    packages = ET.SubElement(coverage, "packages")
    package = ET.SubElement(packages, "package", {"name": "repo"})
    classes = ET.SubElement(package, "classes")

    total_line_hits = sum(item.line_hits for item in file_metrics.values())
    total_line_count = sum(item.line_total for item in file_metrics.values())
    total_branch_hits = sum(item.branch_hits for item in file_metrics.values())
    total_branch_count = sum(item.branch_total for item in file_metrics.values())
    line_rate = 1.0 if total_line_count == 0 else total_line_hits / total_line_count
    branch_rate = 1.0 if total_branch_count == 0 else total_branch_hits / total_branch_count

    coverage.attrib.update(
        {
            "line-rate": f"{line_rate:.4f}",
            "branch-rate": f"{branch_rate:.4f}",
            "lines-covered": str(total_line_hits),
            "lines-valid": str(total_line_count),
            "branches-covered": str(total_branch_hits),
            "branches-valid": str(total_branch_count),
            "complexity": "0",
            "version": "merged",
        }
    )

    for file_metric in sorted(file_metrics.values(), key=lambda item: item.path):
        class_node = ET.SubElement(
            classes,
            "class",
            {
                "name": file_metric.path,
                "filename": file_metric.path,
                "line-rate": f"{(file_metric.line_hits / file_metric.line_total) if file_metric.line_total else 1.0:.4f}",
                "branch-rate": f"{(file_metric.branch_hits / file_metric.branch_total) if file_metric.branch_total else 1.0:.4f}",
                "complexity": "0",
            },
        )
        ET.SubElement(class_node, "methods")
        lines_node = ET.SubElement(class_node, "lines")
        for line_number in sorted(file_metric.lines):
            line_info = file_metric.lines[line_number]
            attrib = {"number": str(line_number), "hits": str(line_info["hits"])}
            if line_info["branchTotal"] > 0:
                attrib["branch"] = "true"
                attrib["condition-coverage"] = (
                    f"{(line_info['branchHits'] / line_info['branchTotal']) * 100:.0f}% "
                    f"({line_info['branchHits']}/{line_info['branchTotal']})"
                )
            ET.SubElement(lines_node, "line", attrib)

    tree = ET.ElementTree(coverage)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(output_path, encoding="utf-8", xml_declaration=True)


def emit_markdown(
    output_path: Path,
    *,
    threshold: float,
    summary: dict[str, Any],
) -> None:
    lines: list[str] = []
    lines.append("# Unit Coverage Summary")
    lines.append("")
    lines.append(f"- Threshold: `{threshold:.1f}%`")
    lines.append(f"- Overall file line coverage: `{summary['overall']['fileLinePercent']:.2f}%`")
    lines.append(f"- Overall class line coverage: `{summary['overall']['classLinePercent']:.2f}%`")
    lines.append(f"- Files below threshold: `{len(summary['thresholdFailures']['files'])}`")
    lines.append(f"- Classes below threshold: `{len(summary['thresholdFailures']['classes'])}`")
    if summary["skippedSuites"]:
        lines.append(f"- Skipped suites: `{', '.join(item['name'] for item in summary['skippedSuites'])}`")
    lines.append("")

    lines.append("## Weakest Files")
    lines.append("")
    lines.append("| File | Coverage | Uncovered Lines | Branch Coverage | Suites |")
    lines.append("| --- | ---: | ---: | ---: | --- |")
    for entry in summary["weakestFiles"][:25]:
        branch = "-" if entry["branchPercent"] is None else f"{entry['branchPercent']:.2f}%"
        lines.append(
            f"| `{entry['path']}` | {entry['linePercent']:.2f}% | {entry['uncoveredLines']} | {branch} | {', '.join(entry['suiteNames'])} |"
        )

    lines.append("")
    lines.append("## Weakest Classes")
    lines.append("")
    lines.append("| Class | File | Coverage | Uncovered Lines | Branch Coverage | Suite |")
    lines.append("| --- | --- | ---: | ---: | ---: | --- |")
    for entry in summary["weakestClasses"][:25]:
        branch = "-" if entry["branchPercent"] is None else f"{entry['branchPercent']:.2f}%"
        lines.append(
            f"| `{entry['className']}` | `{entry['filePath']}` | {entry['linePercent']:.2f}% | {entry['uncoveredLines']} | {branch} | {entry['suiteName']} |"
        )

    lines.append("")
    lines.append("## Threshold Failures")
    lines.append("")
    if not summary["thresholdFailures"]["files"] and not summary["thresholdFailures"]["classes"]:
        lines.append("All in-scope files and classes meet the configured threshold.")
    else:
        for entry in summary["thresholdFailures"]["files"]:
            lines.append(
                f"- File `{entry['path']}` is at `{entry['linePercent']:.2f}%` line coverage in `{', '.join(entry['suiteNames'])}`."
            )
        for entry in summary["thresholdFailures"]["classes"]:
            lines.append(
                f"- Class `{entry['className']}` (`{entry['filePath']}`) is at `{entry['linePercent']:.2f}%` line coverage in `{entry['suiteName']}`."
            )

    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def emit_html(output_path: Path, *, threshold: float, summary: dict[str, Any]) -> None:
    def branch_text(value: float | None) -> str:
        return "-" if value is None else f"{value:.2f}%"

    weakest_rows = "\n".join(
        [
            "<tr>"
            f"<td><code>{entry['path']}</code></td>"
            f"<td>{entry['linePercent']:.2f}%</td>"
            f"<td>{entry['uncoveredLines']}</td>"
            f"<td>{branch_text(entry['branchPercent'])}</td>"
            f"<td>{', '.join(entry['suiteNames'])}</td>"
            "</tr>"
            for entry in summary["weakestFiles"][:25]
        ]
    )
    failure_items = "\n".join(
        [
            f"<li>File <code>{entry['path']}</code>: {entry['linePercent']:.2f}%</li>"
            for entry in summary["thresholdFailures"]["files"]
        ]
        + [
            f"<li>Class <code>{entry['className']}</code> in <code>{entry['filePath']}</code>: {entry['linePercent']:.2f}%</li>"
            for entry in summary["thresholdFailures"]["classes"]
        ]
    )
    if not failure_items:
        failure_items = "<li>All files and classes meet the configured threshold.</li>"

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Unit Coverage Summary</title>
  <style>
    body {{ font-family: Arial, sans-serif; margin: 24px; color: #1f2937; }}
    table {{ border-collapse: collapse; width: 100%; margin-top: 16px; }}
    th, td {{ border: 1px solid #d1d5db; padding: 8px; text-align: left; }}
    th {{ background: #f3f4f6; }}
    code {{ background: #f3f4f6; padding: 2px 4px; border-radius: 4px; }}
  </style>
</head>
<body>
  <h1>Unit Coverage Summary</h1>
  <p>Threshold: <strong>{threshold:.1f}%</strong></p>
  <p>Overall file line coverage: <strong>{summary['overall']['fileLinePercent']:.2f}%</strong></p>
  <p>Overall class line coverage: <strong>{summary['overall']['classLinePercent']:.2f}%</strong></p>
  <h2>Weakest Files</h2>
  <table>
    <thead>
      <tr><th>File</th><th>Coverage</th><th>Uncovered Lines</th><th>Branch Coverage</th><th>Suites</th></tr>
    </thead>
    <tbody>
      {weakest_rows}
    </tbody>
  </table>
  <h2>Threshold Failures</h2>
  <ul>
    {failure_items}
  </ul>
</body>
</html>
"""
    output_path.write_text(html, encoding="utf-8")


def emit_console_summary(
    *,
    threshold: float,
    summary: dict[str, Any],
    summary_path: Path,
    failure_limit: int = 25,
) -> None:
    """Print the coverage-policy outcome prominently for CI logs."""
    file_failures = summary["thresholdFailures"]["files"]
    class_failures = summary["thresholdFailures"]["classes"]
    failing = bool(file_failures or class_failures)
    overall = summary["overall"]

    print()
    print("=" * 80)
    print("UNIT COVERAGE POLICY")
    print(
        "Policy: Every in-scope source file and class must meet at least "
        f"{threshold:.1f}% line coverage."
    )
    print(
        "Overall: "
        f"{overall['fileLinePercent']:.2f}% file coverage, "
        f"{overall['classLinePercent']:.2f}% class coverage."
    )

    if not failing:
        print("Result: PASS — all in-scope files and classes satisfy the policy.")
        print("=" * 80)
        return

    print("Result: FAIL — the coverage policy is not satisfied.")
    print(
        f"Violations: {len(file_failures)} files and {len(class_failures)} classes "
        "are below the required threshold."
    )
    print()
    print(f"Worst file violations (first {min(len(file_failures), failure_limit)}):")
    for entry in file_failures[:failure_limit]:
        print(f"- {entry['path']}: {entry['linePercent']:.2f}%")

    print()
    print(f"Worst class violations (first {min(len(class_failures), failure_limit)}):")
    for entry in class_failures[:failure_limit]:
        print(
            f"- {entry['className']} ({entry['filePath']}): "
            f"{entry['linePercent']:.2f}%"
        )

    print()
    print(f"Full coverage report: {summary_path}")
    print("=" * 80)
    print(
        "::error title=Unit coverage policy failed::"
        f"Every in-scope source file and class requires at least {threshold:.1f}% "
        f"line coverage; {len(file_failures)} files and {len(class_failures)} "
        "classes are below the threshold."
    )


def main() -> int:
    args = parse_args()
    config = load_config(args.config)
    threshold = (
        float(config["thresholds"]["fileLinePercent"])
        if args.threshold is None
        else float(args.threshold)
    )
    results_dir = args.results_dir.resolve()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    summary_path = args.summary_path or output_dir / "coverage-summary.json"
    files: dict[str, FileMetrics] = {}
    classes: list[ClassMetrics] = []
    suite_results: list[dict[str, Any]] = []
    skipped_suites: list[dict[str, Any]] = []
    for suite in config["suites"]:
        suite_dir = results_dir / suite["resultsSubdirectory"]
        suite_summary_path = suite_dir / "suite-result.json"
        suite_status = {"name": suite["name"], "kind": suite["kind"], "status": "missing"}
        if suite_summary_path.exists():
            suite_status = json.loads(suite_summary_path.read_text(encoding="utf-8"))

        coverage_path_text = suite_status.get("coveragePath")
        if suite_status.get("status") == "skipped":
            skipped_suites.append(
                {
                    "name": suite["name"],
                    "reason": suite_status.get("reason", "skipped"),
                    "requiredInCi": bool(suite.get("requiredInCi", False)),
                }
            )
            suite_results.append(suite_status)
            continue

        if not coverage_path_text:
            suite_results.append(suite_status)
            continue

        coverage_path = Path(coverage_path_text)
        if not coverage_path.is_absolute():
            coverage_path = (REPO_ROOT / coverage_path).resolve()
        if coverage_path.exists():
            parse_cobertura_file(
                suite_name=suite["name"],
                coverage_path=coverage_path,
                file_metrics=files,
                class_metrics=classes,
                exclusions=config["coverageExclusions"],
            )
            suite_status["status"] = "passed" if suite_status.get("exitCode", 1) == 0 else "failed"
        suite_results.append(suite_status)

    weakest_files = sorted(files.values(), key=sort_rank_key)
    weakest_classes = sorted(classes, key=sort_rank_key)

    file_failures = [item for item in weakest_files if item.line_percent() < threshold]
    class_threshold = (
        float(config["thresholds"].get("classLinePercent", threshold))
        if args.threshold is None
        else float(args.threshold)
    )
    class_failures = [item for item in weakest_classes if item.line_percent() < class_threshold]

    overall_file_hits = sum(item.line_hits for item in files.values())
    overall_file_total = sum(item.line_total for item in files.values())
    overall_class_hits = sum(item.line_hits for item in classes)
    overall_class_total = sum(item.line_total for item in classes)
    summary = {
        "thresholds": {"fileLinePercent": threshold, "classLinePercent": class_threshold},
        "overall": {
            "fileLinePercent": 100.0 if overall_file_total == 0 else (overall_file_hits / overall_file_total) * 100.0,
            "classLinePercent": 100.0 if overall_class_total == 0 else (overall_class_hits / overall_class_total) * 100.0,
            "filesCount": len(files),
            "classesCount": len(classes),
        },
        "suites": suite_results,
        "skippedSuites": skipped_suites,
        "weakestFiles": [
            {
                "path": item.path,
                "suiteNames": sorted(item.suite_names),
                "classNames": sorted(item.class_names),
                "linePercent": round(item.line_percent(), 2),
                "uncoveredLines": item.uncovered_lines(),
                "branchPercent": None if item.branch_percent() is None else round(item.branch_percent(), 2),
            }
            for item in weakest_files
        ],
        "weakestClasses": [
            {
                "suiteName": item.suite_name,
                "filePath": item.file_path,
                "className": item.class_name,
                "linePercent": round(item.line_percent(), 2),
                "uncoveredLines": item.uncovered_lines(),
                "branchPercent": None if item.branch_percent() is None else round(item.branch_percent(), 2),
            }
            for item in weakest_classes
        ],
        "thresholdFailures": {
            "files": [
                {
                    "path": item.path,
                    "suiteNames": sorted(item.suite_names),
                    "linePercent": round(item.line_percent(), 2),
                }
                for item in file_failures
            ],
            "classes": [
                {
                    "suiteName": item.suite_name,
                    "filePath": item.file_path,
                    "className": item.class_name,
                    "linePercent": round(item.line_percent(), 2),
                }
                for item in class_failures
            ],
        },
    }

    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    emit_markdown(output_dir / "coverage-summary.md", threshold=threshold, summary=summary)
    emit_html(output_dir / "coverage-summary.html", threshold=threshold, summary=summary)
    emit_merged_cobertura(output_dir / "coverage-merged.cobertura.xml", files, classes)

    failing = bool(file_failures or class_failures)
    emit_console_summary(
        threshold=threshold,
        summary=summary,
        summary_path=summary_path,
    )
    return 1 if failing else 0


if __name__ == "__main__":
    raise SystemExit(main())
