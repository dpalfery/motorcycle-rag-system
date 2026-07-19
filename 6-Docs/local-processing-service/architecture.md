# Local Processing Service Architecture

## Overview

The Local Processing Service is a FastAPI process that owns local file processing and exposes job-oriented HTTP controls. It starts a watch-folder worker with the app lifecycle, chooses an embedding provider, validates source paths, dispatches work to PDF/CSV/graph processors, stores generated artifacts, and reports processing stages to MotorcycleRAG API.

## Architecture

```mermaid
flowchart LR
    Admin["Admin Desktop"] -->|"manifest + paired file"| Watch["Watch folder"]
    Watch --> Worker["WatchFolderWorker"]
    Client["Admin Desktop HTTPS client"] -->|"Bearer token"| HTTP["FastAPI control plane"]
    Worker --> Processors["PDF, CSV, bike graph processors"]
    HTTP --> Processors
    Processors --> Chunking["Docling + tokenizer"]
    Processors --> Embeddings["LM Studio / compatible embedder"]
    Processors --> Graph["Metadata and graph extraction"]
    Processors --> Storage["Blob artifacts"]
    Processors --> API["MotorcycleRAG API status/artifact calls"]
```

Admin Desktop is the trusted peer: it starts Uvicorn with TLS on `127.0.0.1`, supplies `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`, and calls the control plane over authenticated HTTPS. The direct Python entry point also binds `127.0.0.1` only. See [local processor integration](local-processor.md).

## Components and Interfaces

| Component | Responsibility | Interfaces |
| --- | --- | --- |
| FastAPI app | Lifespan, CORS, health, job, processing, model-discovery, and shutdown endpoints; app-wide bearer dependency | `/health`, `/embedding/models`, `/process/*`, `/jobs`, `/control/shutdown` |
| `security.local_control_auth` | Constant-time bearer validation for every control route; missing runtime token fails closed | `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN` |
| `security.path_validation` | Strict canonical containment under `LOCAL_PROCESSOR_INPUT_DIR` | Local PDF/CSV path resolution |
| `security.log_sanitizer` | Reversible control-character encoding for log sinks | Shared with processor/API-client logging |
| `security.url_validation` / `security.safe_http` | Structural outbound URL validation and policy-bound HTTP transports with multi-address connect fallback | Model-provider (OpenAI-compatible), API HTTPS, loopback HTTP for local models; see [Outbound HTTP policy](#outbound-http-policy) |
| `WatchFolderWorker` | Polls and validates manifest/file pairs, then dispatches PDF or CSV work | `files/` and `manifests/` contract |
| Processors | PDF extraction/chunking, CSV processing, and deterministic bike-graph import | Async job state and processor methods |
| Embedder factory (`LazyEmbedder`) | Defers discovery and concrete embedder construction until first use; wraps LM Studio/OpenAI-compatible or Ollama-compatible embeddings with truncation | Environment-driven provider configuration; no network I/O at import |
| Extractors | Metadata and graph extraction through configured compatible inference endpoints | Structured extraction models |
| `BlobWriter` and `ApiClient` | Persist output artifacts and report stages to the central API | Blob storage and policy-bound authenticated API HTTP |

### Outbound HTTP policy

Model-provider and API outbound calls share endpoint validation in `security.url_validation` and policy-bound transports in `security.safe_http`, but coverage differs by caller:

| Surface | Transport | Notes |
| --- | --- | --- |
| Embedding discovery | `create_model_discovery_*_client` | Same factory as model-provider clients; multi-address connect fallback |
| OpenAI-compatible embed, metadata, graph | `validate_model_provider_endpoint` + `create_model_provider_async_client` injected into `openai.AsyncOpenAI` | Discovery, embed create, embed health, metadata probe/chat, graph chat |
| MotorcycleRAG API | `create_api_https_async_client` | `API_HTTPS`; globally routable DNS answers only |
| Ollama embed | Construct-time `validate_model_provider_endpoint` only | SDK handles embed/list/health; no mid-flight DNS pin or redirect control (documented residual) |

Allowed model-provider endpoints: public HTTPS or literal-loopback HTTP (`localhost`, `127.0.0.1`, `::1`). Remote private or link-local HTTP, credentials, fragments, unsafe resolved addresses, and redirects are rejected.

After policy validation, transports dial every validated DNS answer in order. Connection-establishment failures (`ConnectError`) trigger fallback to the next target while preserving the configured hostname for Host and TLS SNI — so `http://localhost:1234/v1` works when only IPv4 loopback accepts connections. System-wide rules: [Security directives — Communication](../system/security.md#communication).

## Data Models

| Model | Purpose |
| --- | --- |
| `ProcessPDFRequest` | Upload/document identifiers, blob or local source, resume job ID, and metadata. |
| `ProcessCSVRequest` / `ProcessBikeGraphRequest` | CSV/graph source and import identifiers. |
| `Metadata`, `Chunk`, and `GraphEntity` | Extracted document attributes, chunk payloads/embeddings, and graph data. |
| Processing and job status | Job ID, state, progress, chunk/page counts, artifact locations, and sanitized failure data. |
| Watch-folder manifest | API job identifiers, source file metadata, local paired name, and `processorRunId`. |

## Error Handling

- Missing or invalid bearer tokens return 401; a missing runtime control token returns 503 (fail closed).
- `/health` reports unhealthy or degraded dependencies and whether new work can be accepted; PDF readiness includes tokenizer state. Admin Desktop treats HTTP 200 or 503 as “listening” for start; only `status` / `accepting_work` indicate readiness for ingestion. Embedding discovery runs on first embedder use, not at import — see [local processor integration](local-processor.md#listen-vs-healthy).
- Processing endpoints validate required identifiers and require either a validated local source or required blob-source fields before starting background work.
- Path validation rejects traversal, symlink escape, prefix collisions, missing/non-file paths, and wrong suffixes. The watcher rejects malformed manifests, traversal-like names, missing paired files, and size mismatches.
- Outbound URL/TLS policy rejects credentials, fragments, malformed authorities, non-loopback HTTP, unsafe resolved addresses, and redirects. Policy-bound transports try validated dial targets in order on connect failure only; HTTP 4xx/5xx from an established connection do not trigger address fallback.
- Untrusted diagnostic strings are encoded through `sanitize_log_value` before log sinks; unexpected exceptions are logged server-side without returning tracebacks.
- HTTP failures use explicit 400, 404, 409, 502, or 500 responses as appropriate.
- Shutdown stops acceptance of new work and waits up to 30 seconds for active jobs before exit.

## Testing Strategy

- **Unit:** pytest coverage for processors, embeddings, extractors, path validation, log sanitizer, URL/TLS policy, stage runner, API client, storage, and watch-folder behavior.
- **Endpoint:** FastAPI tests for bearer auth, health, model discovery, processing validation, jobs, cleanup, and shutdown.
- **Integration:** real-PDF processing with an explicitly configured tokenizer and provider, plus API/blob integration in a controlled environment.
- **End-to-end:** Admin Desktop queues a real source through the watch folder; verify worker consumption, local status, API status reporting, and stored artifacts.
