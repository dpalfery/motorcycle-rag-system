# Admin Desktop — Local Processor

All communication with the Python processor goes through Rust commands, not browser `fetch`. The Tauri webview cannot call `http://127.0.0.1` directly (CORS + mixed-content).

## Rust Commands

| Command | What it does |
|---|---|
| `processor_start(config)` | Spawns `python3 -m uvicorn main:app --host 127.0.0.1 --port {port}` in `{workingDir}/src` with env vars |
| `processor_stop()` | POST `/control/shutdown` then kills the child process |
| `processor_running()` | Returns whether a child handle exists |
| `processor_request(method, path, body, port)` | HTTP proxy: GET/POST/PUT/DELETE to `127.0.0.1:{port}{path}` |

## Env Vars Passed to the Processor on Start

`PORT`, `PYTHONUNBUFFERED=1`, `EMBEDDING_PROVIDER_ENDPOINT`, `EMBEDDING_MODEL`, `MCR_API_BASE_URL`, `PYTHON_UPLOAD_JOB_SECRET` (if provided).

## Dev Mode vs Production

Currently spawns `python3 -m uvicorn` from source (dev mode). The plan is to replace this with a PyInstaller sidecar registered as Tauri `externalBin` — same Rust command surface, different program invocation. Do not change the TypeScript `processor.*` API surface when making this switch.
