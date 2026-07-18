# MotorcycleRAG Admin Desktop Requirements

## Introduction

MotorcycleRAG Admin Desktop enables authorized operators to run the local knowledge-ingestion processor and administer supporting RAG-system resources. Its core purpose is a reliable local-first path for PDF manuals and CSV specifications, with the MotorcycleRAG API retaining the authoritative cloud ingestion record.

## Requirements

### Requirement 1: Secure operator access

**User Story:** As an authorized operator, I want to sign in to the desktop application, so that administrative and ingestion actions are protected.

#### Acceptance Criteria

1.1. WHEN an unauthenticated user opens the application THEN the system SHALL present the sign-in experience before administrative screens are available.
1.2. WHEN an operator signs in successfully THEN the system SHALL use the configured admin scope for API calls.
1.3. WHEN an operator signs out or authentication expires THEN the system SHALL clear local session state and prevent further authenticated actions.
1.4. WHEN the system persists an access token THEN the system SHALL use the operating system keychain rather than the source-controlled configuration.

### Requirement 2: Local processor lifecycle and readiness

**User Story:** As an operator, I want the application to start and monitor the local processor, so that I can safely process source files on my machine.

#### Acceptance Criteria

2.1. WHEN an authenticated operator enters the application shell THEN the system SHALL resolve and start the configured local processor when it is not already ready.
2.2. WHEN the processor working directory is unset and automatic resolution is enabled THEN the system SHALL search for a valid repository or packaged processor layout.
2.3. WHEN the processor is unavailable, misconfigured, or not accepting work THEN the system SHALL display its readiness state and prevent local ingestion submission.
2.4. WHEN an operator changes settings passed at processor startup THEN the system SHALL require the processor to be restarted before relying on the changed values.
2.5. WHEN the host starts the local processor THEN the system SHALL generate a per-launch ephemeral CA and leaf certificate for `localhost`/`127.0.0.1`, SHALL launch Uvicorn with TLS on `127.0.0.1`, and SHALL NOT install the CA in the operating-system trust store.
2.6. WHEN the host checks readiness, proxies a processor request, or requests shutdown THEN the system SHALL use HTTPS with hostname validation against the generated CA and SHALL attach the per-launch bearer token.
2.7. WHEN the processor stops or a start/stop path fails THEN the system SHALL remove private certificate and key material from the temporary directory, including poisoned-lock recovery paths that still clean TLS material.

### Requirement 3: Local-first PDF and CSV ingestion

**User Story:** As an operator, I want to submit a local PDF manual or CSV specification for ingestion, so that knowledge can be processed without moving the source-file workflow into a browser.

#### Acceptance Criteria

3.1. WHEN an operator selects a source file THEN the system SHALL accept only supported PDF and CSV file types and SHALL validate applicable API upload constraints.
3.2. WHEN the upload-job secret is missing or the running processor lacks it THEN the system SHALL reject local submission with corrective guidance.
3.3. WHEN a valid local ingestion job is created THEN the system SHALL copy the source file into the local watch folder and SHALL atomically publish its manifest only after the copy succeeds.
3.4. WHEN publishing a local ingestion manifest THEN the system SHALL include the API job identifiers, processor-run identifier, document type, paired file name, size, and creation timestamp.
3.5. IF the selected source file becomes unreadable or its size changes before publication THEN the system SHALL reject the work item and SHALL not publish its manifest.

### Requirement 4: Job visibility and recovery

**User Story:** As an operator, I want local and cloud ingestion status in one place, so that I can diagnose failures and take appropriate recovery actions.

#### Acceptance Criteria

4.1. WHEN the Processor screen is open THEN the system SHALL show local processor health, local jobs, and cloud ingestion jobs using periodic refreshes.
4.2. WHEN a job requires manual metadata THEN the system SHALL allow the operator to submit metadata and SHALL resume the processor flow when the API indicates the job is resuming.
4.3. WHEN an ingestion job fails THEN the system SHALL show sanitized failure information and SHALL expose the supported retry or cleanup actions.
4.4. IF a cloud job remains active after its correlated local processor job is missing beyond the configured grace period THEN the system SHALL mark the cloud job as failed with a stale-job reason.

### Requirement 5: Administration and configuration

**User Story:** As an operator, I want to configure processor integrations and access administration screens, so that I can operate the RAG system from the desktop app.

#### Acceptance Criteria

5.1. WHEN an operator opens Settings THEN the system SHALL allow authorized configuration of API, authentication, processor, model, tokenizer, storage, and browser-profile settings.
5.2. WHEN configuration is saved THEN the system SHALL persist it in the local Tauri store and SHALL use the saved values in subsequent sessions.
5.3. WHEN an authorized operator opens the navigation menu THEN the system SHALL provide access to Processor, Web sources, MCP tools, Users, and Settings screens.
