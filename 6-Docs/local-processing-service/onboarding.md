# Local Processing Service Developer Onboarding

## Prerequisites

- Python 3.10 or later (except Python 3.14.1), compatible with the Poetry project, and a local virtual environment. Use the service virtual environment for tests when it exists.
- Poetry or an equivalent installation process for `pyproject.toml` dependencies, including FastAPI, Uvicorn, Docling, tokenizers, and the configured storage/identity clients.
- A reachable embedding provider for ingestion work. LM Studio's OpenAI-compatible endpoint is the default local provider; Ollama and other supported OpenAI-compatible endpoints are configuration options. The process can start and answer `/health` while the provider is down; discovery runs on first embedder use, not at import.
- A configured tokenizer model path or discoverable local model for PDF chunking.
- Blob-storage and MotorcycleRAG API M2M configuration for artifact upload and status reporting when executing the full ingestion flow.

## Run locally

1. In `2-Application/local-processing-service`, create/activate the project environment and install the Poetry dependencies.
2. Copy `.env.example` to `.env` outside source control and supply the required endpoint, model, tokenizer, storage, API, and client-credential values for the selected environment.
3. Start with `python src/main.py`, or use the environment's `python -m uvicorn main:app --host 127.0.0.1 --port 8100` from `src/`.
4. Call `GET /health` before submitting work. Confirm `accepting_work: true` (and tokenizer readiness for PDF). A process that is listening may still report `status: unhealthy` when the embedding provider is disconnected.
5. Run `pytest` for the service test suite; use focused tests while diagnosing a processor, watcher, or provider failure.

## Debugging

- Logs go to stdout and a daily-rotated `local-processor.log` under `LOCAL_PROCESSOR_LOG_DIR` (default `./logs` relative to the process working directory).
- Admin Desktop launches usually set `cwd` to `<processor-root>/src`, so the default file is `<processor-root>/src/logs/local-processor.log`. See [Admin Desktop integration](local-processor.md#log-path-admin-desktop).
- Use `/health`, `/jobs`, and `/jobs/{job_id}` to distinguish provider readiness from job-specific failures. `services.embedding_provider` is `connected` or `disconnected`; overall `status` / `accepting_work` decide whether to queue work.
- Set `WATCH_FOLDER_DISABLED=true` only for an intentional HTTP-only test. Normal Admin Desktop runs rely on the watcher.
- Test PDF failures with a real readable PDF and tokenizer configured. An available LM Studio endpoint does not make PDF chunking ready by itself.

### Embedding lazy init

`get_embedder()` returns a lazy proxy. Module import and factory construction perform no embedding-provider network I/O. Discovery and concrete embedder construction run on the first `check_status` or embed call (for example the first `/health` after start). If discovery or init fails, `check_status` reports `disconnected`, `/health` stays reachable, and the process remains up. Embedding calls used by jobs raise a clear runtime error until the provider is available.

### Local model endpoint URLs

Use literal loopback URLs such as `http://localhost:1234/v1` for LM Studio or other OpenAI-compatible providers. The processor validates the URL and dials all resolved loopback addresses until one connects — you do not need to rewrite `localhost` to `127.0.0.1`. Ollama hosts (`OLLAMA_BASE_URL` / `OLLAMA_HOST`) follow the same endpoint policy at embedder construction; see [architecture — Outbound HTTP policy](architecture.md#outbound-http-policy) for the Ollama SDK residual.

### Admin Desktop exit status 1

If Start fails with a message that the processor exited early (often `exit status: 1`):

1. Read the full error. After the listen/exit reason, Admin Desktop appends a bounded redacted stderr tail when the child printed diagnostics.
2. If the error says no stderr was captured, open `logs/local-processor.log` under the processor working directory (`cwd`) — typically `src/logs/local-processor.log` for venv launches.
3. Treat a successful Start with `/health` returning 503 and `services.embedding_provider: disconnected` as “listening but not ready for work,” not as a start crash. Start the embedding provider (or fix endpoint/model settings) and re-check `/health` before queueing jobs.
4. Remaining import-time failures (for example missing graph-extraction configuration) can still kill the child before listen; those should now surface in stderr or the log file rather than as an opaque exit code alone.

## Non-standard procedures

- The watch-folder contract is `files/` plus `manifests/`. A manifest is consumed only after its paired file is available; create normal work through Admin Desktop rather than editing manifests by hand.
- Embeddings are normalized to a 1536-dimension target by client-side truncation where the provider cannot supply dimensions directly.
- The service's API credentials are for the `Python-Upload-Job` machine-to-machine path. Never add the client secret to the repository or desktop UI logs.
