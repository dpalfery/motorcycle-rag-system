---
id: admin-desktop/onboarding
title: MotorcycleRAG Admin Desktop Developer Onboarding
doc-type: onboarding
status: current
component: MotorcycleRAG Admin Desktop
source-root: 1-Presentation/MotorcycleRAG.AdminDesktop
owner: Admin Desktop maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Admin Desktop Developer Onboarding

## Prerequisites

- A supported Tauri 2 development environment for the target operating system, including the Rust toolchain (`rustup` and `cargo`) and the platform-native build prerequisites described in the [Tauri prerequisites](https://v2.tauri.app/start/prerequisites/).
- Node.js and npm compatible with the checked-in lockfile.
- Python environment for `2-Application/local-processing-service`, including its project dependencies. The desktop host starts that service with its `.venv` interpreter when present, otherwise `python3` or `python`.
- A reachable embedding endpoint and model. The default development endpoint is LM Studio at `http://localhost:1234/v1` with model `qwen3-embedding`.
- A tokenizer model path for PDF ingestion. A running embedding endpoint alone is insufficient for PDF processing.
- MotorcycleRAG API configuration and an Entra ID account authorized for the configured admin scope. Local ingestion also needs the Python upload-job secret configured in the Settings screen.

## Run locally

1. From `1-Presentation/MotorcycleRAG.AdminDesktop`, run `npm install`.
2. Prepare `2-Application/local-processing-service` according to its Python project configuration, including its virtual environment and any required local models.
3. Start the desktop app with `npm run tauri dev`.
4. Sign in. On entering the application shell, it attempts to resolve and start the local processor on port `8100` by default.
5. Open **Settings** and verify the API URL, authentication settings, processor working directory, embedding and graph-extraction settings, tokenizer path, storage URL, and upload-job secret. Restart the processor after changing values that are passed to it at startup.
6. On **Processor**, confirm the processor health reports that it is accepting work before selecting a PDF or CSV file.

The processor working directory can be selected explicitly. When automatic resolution is enabled and the setting is empty, the host searches for a valid repository or packaged processor layout.

## Debugging

- Frontend: run `npm run dev` for the Vite UI alone, or `npm run tauri dev` to debug the complete desktop flow. Use the webview developer tools to inspect UI failures.
- Rust host: use `cargo test` from `src-tauri` for host tests and attach a Rust debugger to the Tauri process when investigating commands such as `processor_start` or `queue_local_ingestion_work_item`.
- Frontend tests: run `npm test`; run `npm run test:coverage` when coverage output is required.
- Local processor: inspect its `/health` endpoint and log output before retrying an ingestion job. The health response must show tokenizer readiness for PDF work and `accepting_work: true` before a job is queued.
- API path: use the Processor screen's cloud-job state together with the local processor job list. The shared identifier is `processorRunId` / `docIngestionRunId`; this is the correct correlation key for a local-first run.

## Non-standard operational details

- The desktop app copies the selected source into its application-data `local-ingestion-watch/files` directory, then atomically publishes a manifest in `local-ingestion-watch/manifests`. The Python worker consumes the manifest only after its paired file is present.
- Do not place files or hand-written manifests into the watch folder to simulate a normal operator flow. Use the desktop queue operation so identifiers, file size, and file names are validated consistently.
- The app stores configuration locally through the Tauri store and keeps sign-in tokens in the operating system keychain. Do not add credentials to source-controlled configuration files.
