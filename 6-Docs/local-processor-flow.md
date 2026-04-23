# Local Processor Flow and Admin App Relationship

## Executive Overview

The local processor is a Python-based service that the Admin app can start, monitor, and control locally. It does three main things:

1. Start and expose a local HTTP service for health, job control, and model discovery.
2. For PDF and CSV ingestion jobs, use a configured embedding provider endpoint + model to compute vector embeddings.
3. Upload processed artifacts to the backend API once each job type has built its artifact payload.

The Admin app is not creating embeddings in Azure itself. Instead, it configures and drives the local Python processor, then uploads the generated artifacts to the backend API once processing is complete.

## High-Level Flow

1. Admin app loads configuration from settings.
2. Admin starts the local processor process using the configured working directory and start command.
3. Admin polls the local processor `/health` endpoint until the service is available.
4. Admin may call the local processor `/embedding/models` endpoint to discover available models from the configured provider endpoint.
5. The local processor processes local content according to job type, then sends the resulting artifacts to the Azure-based backend API.
6. Admin displays local processor status, recent output, and job state on the Jobs page.

## Detailed Flow

### 1. Admin configuration and startup

- The Admin app persists local processor settings in secure storage and preferences.
- Key settings include:
  - `LocalProcessorEndpoint` - the local service URL, typically `http://localhost:8100`.
  - `LocalProcessorWorkingDirectory` - the root folder containing `2-Application/local-processing-service`.
  - `LocalProcessorStartCommand` - the shell command used to launch the Python service.
  - `LocalProcessorUploadJobSecret` - the secret passed to Python via `PYTHON_UPLOAD_JOB_SECRET`.
  - `EmbeddingProviderEndpoint` - the OpenAI-compatible or Ollama endpoint used for embeddings.
  - `EmbeddingModel` - the selected model name for the embedding provider.
- These settings are loaded by `ConfigurationStateService` and consumed by `LocalProcessorService`.

### 2. Starting the local processor

- `LocalProcessorService.StartAsync()` builds a `ProcessStartInfo` with:
  - `FileName = cmd.exe`
  - `Arguments = /c {startCommand}`
  - `WorkingDirectory = LocalProcessorWorkingDirectory`
  - `RedirectStandardOutput = true`
  - `RedirectStandardError = true`
- It also forwards environment variables into the Python process:
  - `PYTHONUNBUFFERED=1`
  - `PYTHON_UPLOAD_JOB_SECRET`
  - `MCR_API_BASE_URL`
  - `EMBEDDING_PROVIDER_ENDPOINT`
  - `EMBEDDING_MODEL`
  - `LOCAL_PROCESSOR_LOG_DIR`
- The Python process starts and the Admin app monitors stdout/stderr to capture live diagnostic output.

### 3. Local processor health and readiness

- The Admin app polls `GET /health` on the local processor endpoint.
- The local processor health endpoint performs internal checks such as:
  - embedding provider availability via `embedder.check_status()`
  - blob storage connectivity via `BlobWriter`
  - whether the API client is configured
- If `/health` succeeds, the processor is ready to accept local jobs.

### 4. Embedding model discovery

- The Admin app exposes a `Load Models` action in Settings.
- When the user clicks it, the app calls `LocalProcessorService.GetEmbeddingModelsAsync(providerEndpoint)`.
- That method sends a request to local processor `/embedding/models?endpoint={providerEndpoint}`.
- The Python processor then probes the provider endpoint and returns:
  - provider type (OpenAI-compatible, Ollama, etc.)
  - available embedding model names
- This flow happens before job processing and is separate from the backend API.

### 5. Embedding model selection

- After discovery, the Admin saves the chosen `EmbeddingModel` along with `EmbeddingProviderEndpoint`.
- Those values are forwarded into the local Python process.
- The local processor uses them from the environment to instantiate its embedder.

### 6. Processing a local job

The local processor has different flows depending on job type.

#### Bike-graph import flow

- When the Admin starts a bike-graph import, the local processor:
  - reads the supplied local CSV file
  - builds graph nodes and edges deterministically
  - formats the `graph-entities` artifact payload
  - uploads that payload to the backend API
- This path does **not** call the embedder.
- This is why you can see an Azure API upload attempt before any embedding provider activity for bike-graph jobs.

#### PDF and CSV ingestion flow

- When the Admin starts a PDF or CSV ingestion job, the local processor:
  - downloads or reads the source file
  - extracts chunks or grouped content
  - calls the embedder to compute vector embeddings
  - formats the search artifact payload
  - uploads the resulting artifacts to the backend API
- For these job types, the processor does not upload search artifacts to the Azure-based API until the embedding work is complete.

### 7. Uploading artifacts to the backend API

- Once the local processor has generated the artifact, it uses `ApiClient` to call the backend API upload endpoint.
- This is the Azure-based API call that can time out or fail if there is a network/auth issue.
- The backend API receives the already-created artifact payload from the local processor.

### 8. Admin observability and diagnostics

- The Jobs page shows:
  - local processor running status
  - whether it is accepting work
  - recent process output captured from stdout/stderr
  - local processor job list
- If the local processor does not produce live output, the Admin also can fall back to the processor log file.
- The local processor log file is now written to the Admin app log folder so a single diagnostics location exists.

## Why the Azure API call may appear before embedding activity

- The backend API is the sink for locally generated artifacts, not the source of the embedding model.
- The `/embedding/models` flow is a local processor probe of the provider endpoint, not an Azure API call.
- For bike-graph jobs, there is no embedding stage, so the first significant outbound activity after local graph construction is the backend API upload.
- For PDF and CSV jobs, embedding generation should happen before the search-artifact upload.

## Relationship between Admin app and local processor

- The Admin app is the controller and UI layer.
- The local processor is the worker and embedding runtime.
- The Admin app:
  - saves settings
  - launches/stops the local processor
  - polls health and jobs
  - collects diagnostics
  - initiates local file processing
- The local processor:
  - hosts local HTTP endpoints
  - loads the embedding provider and model
  - processes files
  - uploads the resulting artifacts to the backend API

## Practical troubleshooting tips

- Confirm the local processor is actually running at `LocalProcessorEndpoint`.
- Confirm `EmbeddingProviderEndpoint` and `EmbeddingModel` are set and discovered successfully.
- Confirm `/health` returns healthy before starting jobs.
- If the Azure API call times out, the issue is most likely:
  - network/connectivity between local processor and API
  - wrong `MCR_API_BASE_URL`
  - auth or upload authorization issue
  - backend API not reachable from the local processor environment
- If no embedding model activity is visible, check the local processor log and the Settings page model discovery step.
