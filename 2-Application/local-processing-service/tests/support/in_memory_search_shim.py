"""In-memory Azure Search replacement for manual-processing system tests."""

from __future__ import annotations

import json
import math
from typing import Any

from fastapi import FastAPI, HTTPException, Query, Request

app = FastAPI(title="MotorcycleRAG In-Memory Search Shim")
_records: list[dict[str, Any]] = []


@app.get("/health")
async def health() -> dict[str, Any]:
    return {"status": "healthy", "record_count": len(_records)}


@app.post("/reset")
async def reset() -> dict[str, Any]:
    _records.clear()
    return {"status": "ok", "record_count": 0}


@app.post("/index-jsonl")
async def index_jsonl(
    request: Request,
    upload_id: str = Query(..., alias="uploadId"),
) -> dict[str, Any]:
    body = await request.body()
    indexed = 0

    for line_number, raw_line in enumerate(body.decode("utf-8").splitlines(), start=1):
        if not raw_line.strip():
            continue

        try:
            record = json.loads(raw_line)
        except json.JSONDecodeError as exc:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid JSONL at line {line_number}: {exc}",
            ) from exc

        _validate_record(record, line_number, upload_id)
        _records[:] = [existing for existing in _records if existing.get("id") != record["id"]]
        _records.append(record)
        indexed += 1

    return {"status": "indexed", "indexed": indexed, "record_count": len(_records)}


@app.get("/search")
async def search(
    q: str = Query(...),
    upload_id: str | None = Query(default=None, alias="uploadId"),
    top: int = Query(default=5, ge=1, le=50),
) -> dict[str, Any]:
    query_terms = [term for term in q.lower().split() if term]
    candidates = [
        record
        for record in _records
        if upload_id is None or str(record.get("sourceFile")) == upload_id
    ]
    ranked = sorted(
        ((score_record(record, query_terms), record) for record in candidates),
        key=lambda item: item[0],
        reverse=True,
    )
    results = [record for score, record in ranked if score > 0][:top]
    return {"results": results, "count": len(results), "totalIndexed": len(_records)}


def score_record(record: dict[str, Any], query_terms: list[str]) -> int:
    searchable = " ".join(
        str(record.get(field, ""))
        for field in ("title", "content", "make", "model", "section", "primarySection")
    ).lower()
    return sum(1 for term in query_terms if term in searchable)


def _validate_record(record: Any, line_number: int, upload_id: str) -> None:
    if not isinstance(record, dict):
        raise HTTPException(status_code=400, detail=f"Line {line_number} is not an object.")

    required = [
        "id",
        "title",
        "content",
        "documentType",
        "sourceFile",
        "chunkIndex",
        "contentVector",
    ]
    missing = [field for field in required if field not in record]
    if missing:
        raise HTTPException(
            status_code=400,
            detail=f"Line {line_number} is missing required fields: {', '.join(missing)}",
        )

    if record["sourceFile"] != upload_id:
        raise HTTPException(
            status_code=400,
            detail=f"Line {line_number} sourceFile does not match uploadId.",
        )

    content = record.get("content")
    if not isinstance(content, str) or not content.strip():
        raise HTTPException(status_code=400, detail=f"Line {line_number} has empty content.")

    vector = record.get("contentVector")
    if (
        not isinstance(vector, list)
        or not vector
        or not all(isinstance(value, (int, float)) and math.isfinite(value) for value in vector)
    ):
        raise HTTPException(
            status_code=400,
            detail=f"Line {line_number} has an invalid contentVector.",
        )
