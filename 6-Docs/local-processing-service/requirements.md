# Local Processing Service Requirements

## Introduction

The Local Processing Service converts locally available motorcycle sources into searchable ingestion artifacts while maintaining a job-level relationship with MotorcycleRAG API. It supports direct local control and the Admin Desktop watch-folder workflow.

## Requirements

### Requirement 1: Processor readiness and lifecycle

**User Story:** As an operator, I want to know whether the local processor can safely accept work, so that I do not begin an ingestion run with unavailable dependencies.

#### Acceptance Criteria

1.1. WHEN the service starts THEN the system SHALL initialize configured processors and SHALL start the watch-folder worker unless it is explicitly disabled.
1.2. WHEN a client requests `/health` THEN the service SHALL report processor health, active-job state, embedding readiness, tokenizer status, storage status, and API-client configuration without exposing secrets.
1.3. IF the processor is shutting down or a required dependency is unavailable THEN the service SHALL report that it is not accepting work.
1.4. WHEN shutdown is requested THEN the service SHALL stop accepting new work and SHALL allow active jobs a bounded grace period to finish.
1.5. WHEN the service is started through the direct Python entry point THEN the system SHALL bind only to `127.0.0.1`.
1.6. WHEN any local control endpoint is invoked THEN the system SHALL require a constant-time-validated bearer token from `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`, and SHALL fail closed with 503 if that token is unset.

### Requirement 2: Local-first watch-folder processing

**User Story:** As an Admin Desktop operator, I want queued local files to be processed from the watch folder, so that the desktop app and processor use a reliable handoff contract.

#### Acceptance Criteria

2.1. WHEN a manifest is published in the watch-folder manifest directory THEN the service SHALL validate the manifest and its paired source file before processing it.
2.2. IF a manifest contains an unsafe path, missing file, malformed data, or size mismatch THEN the service SHALL reject the work item and SHALL not process the source.
2.3. WHEN a valid PDF or CSV manifest is consumed THEN the service SHALL dispatch it to the matching processor using its `processorRunId` as the correlation identifier.

### Requirement 3: Document and graph processing

**User Story:** As an ingestion operator, I want supported source types transformed into consistent artifacts, so that the central system can index and use motorcycle knowledge.

#### Acceptance Criteria

3.1. WHEN a valid PDF request is accepted THEN the service SHALL validate the source, process it asynchronously, and SHALL expose a job status response.
3.2. WHEN a valid CSV request is accepted THEN the service SHALL validate the source and SHALL process it asynchronously.
3.3. WHEN a valid bike-graph request is accepted THEN the service SHALL process the CSV deterministically without requiring LLM or embedding calls.
3.4. WHEN PDF chunking is required THEN the service SHALL require a configured tokenizer before accepting the work as ready.
3.5. WHEN an embedding provider cannot set the target vector dimension server-side THEN the service SHALL apply client-side truncation to the configured 1536-dimension target.

### Requirement 4: Job observation and reporting

**User Story:** As an operator, I want job status and artifacts reported consistently, so that I can monitor local processing from both the processor and the central API.

#### Acceptance Criteria

4.1. WHEN a job is accepted, running, completed, stopped, or failed THEN the service SHALL expose its current state through the job endpoints.
4.2. WHEN a processor produces artifacts or advances a correlated ingestion stage THEN the service SHALL use its configured authenticated API client to report the appropriate result.
4.3. WHEN a client cleans up jobs THEN the service SHALL delete terminal job records and SHALL preserve active work.
4.4. IF an unexpected processing error occurs THEN the service SHALL log diagnostic details locally and SHALL return a sanitized HTTP error response.
4.5. WHEN untrusted strings are written to logs THEN the service SHALL encode control characters with the shared reversible log sanitizer so raw CR/LF/tab/NUL/C0/C1 characters cannot forge log lines, while preserving printable diagnostic content.

### Requirement 5: Outbound endpoint and local path safety

**User Story:** As an operator, I want the processor to refuse unsafe local paths and unsafe outbound endpoints, so that local processing cannot be steered into SSRF or path-escape attacks.

#### Acceptance Criteria

5.1. WHEN a request supplies a local file path THEN the service SHALL resolve it under `LOCAL_PROCESSOR_INPUT_DIR` with strict canonical containment and SHALL reject traversal, symlink escape, prefix collisions, missing/non-file paths, and wrong suffixes.
5.2. WHEN the service calls a remote API or public HTTPS endpoint THEN the system SHALL require HTTPS with certificate and hostname validation enabled.
5.3. WHEN the service calls a local model endpoint over plain HTTP THEN the system SHALL allow HTTP only for literal loopback hosts and SHALL reject credentials, fragments, malformed authorities, unsafe redirects, and non-loopback private or link-local targets.
