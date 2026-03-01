# Issues — local-docling-ingestion

## [2026-03-01T09:07:46Z] Known Issues / Gotchas

### Python Package Structure
- `src/processors/`, `src/embeddings/`, `src/extraction/`, `src/storage/` dirs exist but are EMPTY
- Each needs `__init__.py` created alongside the implementation file
- The `main.py` already has correct imports — implementations must match those exact import paths

### Dockerfile
- Current Dockerfile uses `requirements.txt` but project uses `pyproject.toml`/Poetry
- Must fix to use `poetry install` or `poetry export`

### .env.example
- Contains `AZURE_STORAGE_CONNECTION_STRING` — must be replaced with `AZURE_STORAGE_ACCOUNT_URL`
- Must add `ALLOWED_ORIGINS` for CORS config

### Azure Blob Auth
- Python service must use DefaultAzureCredential as primary
- Connection string only as LOCAL DEV FALLBACK (when AZURE_STORAGE_ACCOUNT_URL not set)

### Embedding Dimensions
- MUST be exactly 1536 dims
- Qwen3-Embedding-4B supports MRL (Matryoshka Representation Learning) — can truncate to 1536
- Existing index uses 1536 (confirmed in MotorcycleIndexingService.cs:225)
