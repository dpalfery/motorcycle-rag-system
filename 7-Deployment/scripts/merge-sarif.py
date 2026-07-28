#!/usr/bin/env python3
"""Merge multiple SARIF 2.1.0 files into one report with a single run.

GitHub Code Scanning rejects uploads that contain multiple runs under the same
category (see https://github.blog/changelog/2025-07-21-code-scanning-will-stop-combining-multiple-sarif-runs-uploaded-in-the-same-sarif-file/).

Usage: merge-sarif.py <input-dir> <output.sarif>
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


def _rule_id(rule: dict[str, Any]) -> str | None:
    return rule.get("id") or rule.get("ruleId")


def _merge_rules(existing: list[dict[str, Any]], incoming: list[dict[str, Any]]) -> list[dict[str, Any]]:
    seen = {_rule_id(r) for r in existing if _rule_id(r)}
    for rule in incoming:
        rid = _rule_id(rule)
        if rid is None or rid in seen:
            continue
        existing.append(rule)
        seen.add(rid)
    return existing


def _merge_into(base: dict[str, Any], other: dict[str, Any]) -> None:
    base_tool = base.setdefault("tool", {})
    base_driver = base_tool.setdefault("driver", {})
    other_tool = other.get("tool") or {}
    other_driver = other_tool.get("driver") or {}

    if not base_driver.get("name") and other_driver.get("name"):
        base_driver["name"] = other_driver["name"]
    if not base_driver.get("informationUri") and other_driver.get("informationUri"):
        base_driver["informationUri"] = other_driver["informationUri"]
    if not base_driver.get("version") and other_driver.get("version"):
        base_driver["version"] = other_driver["version"]
    if not base_driver.get("semanticVersion") and other_driver.get("semanticVersion"):
        base_driver["semanticVersion"] = other_driver["semanticVersion"]

    base_driver["rules"] = _merge_rules(
        list(base_driver.get("rules") or []),
        list(other_driver.get("rules") or []),
    )

    base.setdefault("results", [])
    base["results"].extend(other.get("results") or [])

    if other.get("artifacts"):
        base.setdefault("artifacts", [])
        base["artifacts"].extend(other["artifacts"])


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: merge-sarif.py <input-dir> <output.sarif>", file=sys.stderr)
        return 2

    input_dir = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    merged_run: dict[str, Any] | None = None
    source_runs = 0

    for path in sorted(input_dir.glob("*.sarif")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            print(f"skip {path}: {exc}", file=sys.stderr)
            continue

        for run in data.get("runs") or []:
            source_runs += 1
            if merged_run is None:
                # Deep-ish copy via JSON so later merges do not mutate input objects.
                merged_run = json.loads(json.dumps(run))
                merged_run.setdefault("results", [])
                tool = merged_run.setdefault("tool", {})
                driver = tool.setdefault("driver", {})
                driver.setdefault("rules", list(driver.get("rules") or []))
            else:
                _merge_into(merged_run, run)

    if merged_run is None:
        merged_run = {
            "tool": {
                "driver": {
                    "name": "SkillSpector",
                    "rules": [],
                }
            },
            "results": [],
        }

    # Ensure driver name is present for GitHub upload validation.
    driver = merged_run.setdefault("tool", {}).setdefault("driver", {})
    if not driver.get("name"):
        driver["name"] = "SkillSpector"

    merged = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [merged_run],
    }
    output_path.write_text(json.dumps(merged, indent=2) + "\n", encoding="utf-8")
    result_count = len(merged_run.get("results") or [])
    print(
        f"merged {source_runs} run(s) into 1 run "
        f"({result_count} result(s)) -> {output_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
