---
id: local-processor/reference
title: Local Processing Service — Admin Desktop Integration
doc-type: reference
status: current
component: Local Processing Service
owner: Ingestion maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Local Processing Service — Admin Desktop Integration

This document defines the Admin Desktop integration boundary for the Local Processing Service. For service ownership, lifecycle, and processing behavior, also read the [service architecture](architecture.md). System-wide trust rules are in [security directives](../system/security.md).

All communication with the Python processor goes through Rust commands, not browser `fetch`. The Tauri webview cannot call the loopback processor directly (CORS + mixed-content). The host uses authenticated HTTPS only.

## Rust Commands

| Command | What it does |
| --- | --- |
| `processor_start(config)` | Creates `ProcessorTransport` (ephemeral CA/leaf + bearer token), spawns Uvicorn on `127.0.0.1` with `--ssl-keyfile` / `--ssl-certfile`, sets `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`, pipes child stderr for diagnostics, and waits until authenticated `GET /health` responds with HTTP 200 or 503 |
| `processor_stop()` | Authenticated HTTPS `POST /control/shutdown`, kills the child if needed, then removes private certificate material |
| `processor_running()` | Returns whether a child handle exists |
| `processor_request(method, path, body, port)` | Authenticated HTTPS proxy: GET/POST/PUT/DELETE to `https://127.0.0.1:{port}{path}` with the session bearer token; rejects HTTP and authority-changing paths |

## Listen vs healthy

Admin Desktop start success means the control plane is reachable, not that the processor is ready to accept ingestion work.

| Concept | Meaning |
| --- | --- |
| Listening (Rust start gate) | `processor_start` succeeds when authenticated `GET /health` returns **HTTP 200 or 503** within the listen timeout. A 503 from a live process (for example embedding provider disconnected) still counts as listening. |
| Healthy / accepting work | The `/health` JSON body reports `status` (`healthy`, `degraded`, or `unhealthy`) and `accepting_work`. Operators must inspect that body (or the Processor screen) before queueing jobs. |

At import, the processor constructs a `LazyEmbedder` via `get_embedder()` (no network I/O); discovery and concrete provider construction run on first `check_status` / embed. Admin Desktop only spawns Uvicorn; it does not build the embedder. Init or discovery failure leaves the process up and reports `services.embedding_provider` as `disconnected` with overall `status: unhealthy` / `accepting_work: false` (typically HTTP 503). Jobs that call embed raise a clear runtime error instead of crashing the process.

TypeScript preflight helpers that interpret “ready” for UI workflows are a separate concern from this Rust listen gate. Do not assume start success alone means the UI will treat the processor as ready for every ingestion path.

## Start-failure diagnostics

When the child exits early or does not answer `/health` in time, `processor_start` returns the listen/exit reason plus a **bounded, redacted** stderr tail (or a pointer to the log file when stderr was empty). Redaction covers the per-launch control token, the upload-job secret when known, bearer tokens, and secret-shaped values. Operators should read the stderr text in the error before assuming a generic “exit status 1” is unexplained.

## Log path (Admin Desktop)

The Python process writes a daily-rotated `local-processor.log` under `LOCAL_PROCESSOR_LOG_DIR` (default `./logs` relative to the process working directory).

Admin Desktop sets the child working directory when it launches Uvicorn:

| Launch mode | Working directory (`cwd`) | Default log file |
| --- | --- | --- |
| Project venv or system `python3` | `<processor-root>/src` | `<processor-root>/src/logs/local-processor.log` |
| Poetry (`poetry run uvicorn …`) | `<processor-root>` | `<processor-root>/logs/local-processor.log` |

Repository development normally uses the project venv path, so the common Admin Desktop log location is `2-Application/local-processing-service/src/logs/local-processor.log`.

## Trust material (per launch)

| Item | Behavior |
| --- | --- |
| Ephemeral CA + leaf | Leaf SANs include `localhost` and `127.0.0.1`; written to a private temp directory |
| Reqwest client | Trusts only the generated CA; built-in roots disabled; HTTPS-only; redirects disabled |
| Bearer token | URL-safe high-entropy value in `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`; attached as `Authorization: Bearer …` |
| OS trust store | The CA is never installed in the operating-system trust store |
| Cleanup | Certificate directory is removed on stop and failure paths, including poisoned-lock recovery |

## Env Vars Passed to the Processor on Start

`PORT`, `PYTHONUNBUFFERED=1`, `WATCH_FOLDER`, `LOCAL_PROCESSOR_INPUT_DIR`, `EMBEDDING_PROVIDER_ENDPOINT`, `EMBEDDING_MODEL`, `TOKENIZER_MODEL_PATH` (if provided), `MCR_API_BASE_URL`, `AZURE_STORAGE_ACCOUNT_URL`, `PYTHON_UPLOAD_JOB_SECRET` (if provided), `GRAPH_EXTRACTION_ENDPOINT`, `GRAPH_EXTRACTION_MODEL`, `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`.

`WATCH_FOLDER` and `LOCAL_PROCESSOR_INPUT_DIR` are both set to the Admin Desktop managed local ingestion folder so direct local-file processing uses the same path boundary that the app controls.

## Dev Mode vs Production

Currently spawns Uvicorn from source (project venv, Poetry, or system `python3`) with TLS arguments supplied by `ProcessorTransport`. The plan is to replace this with a PyInstaller sidecar registered as Tauri `externalBin` — same Rust command surface and the same authenticated HTTPS control plane, different program invocation. Do not change the TypeScript `processor.*` API surface when making this switch.
