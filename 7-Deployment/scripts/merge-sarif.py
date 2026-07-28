#!/usr/bin/env python3
"""Merge multiple SARIF 2.1.0 files into one report for GitHub upload-sarif.

Usage: merge-sarif.py <input-dir> <output.sarif>
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: merge-sarif.py <input-dir> <output.sarif>", file=sys.stderr)
        return 2

    input_dir = Path(sys.argv[1])
    output_path = Path(sys.argv[2])

    runs: list[dict] = []
    for path in sorted(input_dir.glob("*.sarif")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            print(f"skip {path}: {exc}", file=sys.stderr)
            continue
        for run in data.get("runs") or []:
            runs.append(run)

    merged = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": runs,
    }
    output_path.write_text(json.dumps(merged, indent=2) + "\n", encoding="utf-8")
    print(f"merged {len(runs)} run(s) -> {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
