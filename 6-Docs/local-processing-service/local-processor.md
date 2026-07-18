# Local Processing Service — Admin Desktop Integration

This document defines the Admin Desktop integration boundary for the Local Processing Service. For service ownership, lifecycle, and processing behavior, also read the [service architecture](architecture.md). System-wide trust rules are in [security directives](../system/security.md).

All communication with the Python processor goes through Rust commands, not browser `fetch`. The Tauri webview cannot call the loopback processor directly (CORS + mixed-content). The host uses authenticated HTTPS only.

## Rust Commands

| Command | What it does |
| --- | --- |
| `processor_start(config)` | Creates `ProcessorTransport` (ephemeral CA/leaf + bearer token), spawns Uvicorn on `127.0.0.1` with `--ssl-keyfile` / `--ssl-certfile`, sets `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`, and waits for authenticated HTTPS readiness |
| `processor_stop()` | Authenticated HTTPS `POST /control/shutdown`, kills the child if needed, then removes private certificate material |
| `processor_running()` | Returns whether a child handle exists |
| `processor_request(method, path, body, port)` | Authenticated HTTPS proxy: GET/POST/PUT/DELETE to `https://127.0.0.1:{port}{path}` with the session bearer token; rejects HTTP and authority-changing paths |

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
