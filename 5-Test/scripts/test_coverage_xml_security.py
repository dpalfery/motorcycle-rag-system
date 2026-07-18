from __future__ import annotations

from pathlib import Path

import pytest
from defusedxml.common import DefusedXmlException

import aggregate_coverage
import run_unit_coverage


def test_parse_cobertura_file_rejects_entity_expansion(tmp_path: Path) -> None:
    coverage_path = tmp_path / "coverage.cobertura.xml"
    coverage_path.write_text(
        """<?xml version="1.0"?>
<!DOCTYPE coverage [
  <!ENTITY payload "expanded-value">
]>
<coverage><sources><source>.</source></sources><packages><package><classes>
  <class name="&payload;" filename="0-Base/MotorcycleRAG.Core/Utilities/LogSanitizer.cs"><lines /></class>
</classes></package></packages></coverage>
""",
        encoding="utf-8",
    )

    with pytest.raises(DefusedXmlException):
        aggregate_coverage.parse_cobertura_file(
            suite={"name": "unit", "sourceRoots": ["0-Base"]},
            coverage_path=coverage_path,
            file_metrics={},
            class_metrics={},
            exclusions={},
        )


def test_parse_trx_failures_rejects_entity_expansion_without_reporting_injected_text(tmp_path: Path) -> None:
    trx_path = tmp_path / "malicious.trx"
    trx_path.write_text(
        """<?xml version="1.0"?>
<!DOCTYPE TestRun [
  <!ENTITY payload "expanded-value">
]>
<TestRun><Results>
  <UnitTestResult outcome="Failed" testName="&payload;">
    <Output><ErrorInfo><Message>&payload;</Message></ErrorInfo></Output>
  </UnitTestResult>
</Results></TestRun>
""",
        encoding="utf-8",
    )

    failures = run_unit_coverage.parse_trx_failures(tmp_path)

    assert failures == []


def test_parse_trx_failures_ignores_invalid_files_and_preserves_valid_failure(tmp_path: Path) -> None:
    (tmp_path / "valid.trx").write_text(
        """<?xml version="1.0"?>
<TestRun><Results>
  <UnitTestResult outcome="Failed" testName="Valid.Failing.Test">
    <Output><ErrorInfo><Message>Expected true but found false.</Message></ErrorInfo></Output>
  </UnitTestResult>
</Results></TestRun>
""",
        encoding="utf-8",
    )
    (tmp_path / "malformed.trx").write_text(
        "<TestRun><Results><UnitTestResult outcome=\"Failed\">",
        encoding="utf-8",
    )
    (tmp_path / "entity-bearing.trx").write_text(
        """<?xml version="1.0"?>
<!DOCTYPE TestRun [<!ENTITY payload "injected">]>
<TestRun><Results>
  <UnitTestResult outcome="Failed" testName="&payload;" />
</Results></TestRun>
""",
        encoding="utf-8",
    )

    failures = run_unit_coverage.parse_trx_failures(tmp_path)

    assert failures == [
        {
            "name": "Valid.Failing.Test",
            "message": "Expected true but found false.",
        }
    ]
