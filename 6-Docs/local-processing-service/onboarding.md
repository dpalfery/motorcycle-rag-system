# Local Processing Service Developer Onboarding

## Prerequisites

- Python 3.10 or later (except Python 3.14.1), compatible with the Poetry project, and a local virtual environment. Use the service virtual environment for tests when it exists.
- Poetry or an equivalent installation process for `pyproject.toml` dependencies, including FastAPI, Uvicorn, Docling, tokenizers, and the configured storage/identity clients.
- A reachable embedding provider. LM Studio's OpenAI-compatible endpoint is the default local provider; Ollama and other supported OpenAI-compatible endpoints are configuration options.
- A configured tokenizer model path or discoverable local model for PDF chunking.
- Blob-storage and MotorcycleRAG API M2M configuration for artifact upload and status reporting when executing the full ingestion flow.

## Run locally

1. In `2-Application/local-processing-service`, create/activate the project environment and install the Poetry dependencies.
2. Copy `.env.example` to `.env` outside source control and supply the required endpoint, model, tokenizer, storage, API, and client-credential values for the selected environment.
3. Start with `python src/main.py`, or use the environment's `python -m uvicorn main:app --host 127.0.0.1 --port 8100` from `src/`.
4. Call `GET /health` before submitting work. For PDF ingestion, confirm tokenizer readiness and `accepting_work: true`.
5. Run `pytest` for the service test suite; use focused tests while diagnosing a processor, watcher, or provider failure.

## Debugging

- Logs go to stdout and a daily-rotated `local-processor.log` under `LOCAL_PROCESSOR_LOG_DIR` (default `./logs`).
- Use `/health`, `/jobs`, and `/jobs/{job_id}` to distinguish provider readiness from job-specific failures.
- Set `WATCH_FOLDER_DISABLED=true` only for an intentional HTTP-only test. Normal Admin Desktop runs rely on the watcher.
- Test PDF failures with a real readable PDF and tokenizer configured. An available LM Studio endpoint does not make PDF chunking ready by itself.

## Non-standard procedures

- The watch-folder contract is `files/` plus `manifests/`. A manifest is consumed only after its paired file is available; create normal work through Admin Desktop rather than editing manifests by hand.
- Embeddings are normalized to a 1536-dimension target by client-side truncation where the provider cannot supply dimensions directly.
- The service's API credentials are for the `Python-Upload-Job` machine-to-machine path. Never add the client secret to the repository or desktop UI logs.
