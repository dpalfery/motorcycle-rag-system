---
id: webui/requirements
title: MotorcycleRAG Web UI Requirements
doc-type: requirements
status: current
component: MotorcycleRAG Web UI
owner: Web UI maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Web UI Requirements

## Introduction

MotorcycleRAG Web UI provides a browser-based, authenticated conversation experience for motorcycle questions. It uses a Backend for Frontend to keep authentication and API token handling outside the SPA.

## Requirements

### Requirement 1: BFF-backed authentication

**User Story:** As an end user, I want to sign in through the Web UI, so that I can access protected motorcycle knowledge without the browser managing API bearer tokens.

#### Acceptance Criteria

1.1. WHEN an unauthenticated user opens a protected route THEN the application SHALL present the login path rather than protected content.
1.2. WHEN a user starts login or logout THEN the SPA SHALL use the same-origin BFF authentication endpoints.
1.3. WHEN the SPA checks authentication state THEN the BFF SHALL provide session state through `/auth/me`.
1.4. WHEN an authenticated browser session is established THEN the system SHALL keep API token handling in the BFF rather than browser storage.

### Requirement 2: Motorcycle chat

**User Story:** As an authenticated user, I want to ask a motorcycle question and read the result, so that I can explore the knowledge base in a conversational interface.

#### Acceptance Criteria

2.1. WHEN a user submits non-empty chat input THEN the SPA SHALL post the query and recent context to the same-origin motorcycle query endpoint.
2.2. WHEN a query is in progress THEN the SPA SHALL show processing feedback and SHALL prevent a duplicate submission.
2.3. WHEN the API returns an answer, suggestions, or sources THEN the SPA SHALL render the available response content in the conversation.
2.4. IF a query exceeds the client timeout or returns an HTTP error THEN the SPA SHALL display a retryable failure message and SHALL restore input availability.

### Requirement 3: Secure API proxying

**User Story:** As a security-conscious operator, I want browser API traffic to pass through the BFF, so that routing, session handling, and web security controls are centralized.

#### Acceptance Criteria

3.1. WHEN the SPA calls an API route THEN the BFF SHALL proxy the `/api/*` request to the configured MotorcycleRAG API destination.
3.2. WHEN the BFF receives a browser request THEN the BFF SHALL apply its configured host, CORS, authentication, data-protection, telemetry, and security-header policies before processing it.
3.3. IF the API destination is unavailable THEN the BFF SHALL return the proxy failure through the same-origin route and SHALL not cause the SPA to call the API cross-origin.

### Requirement 4: Quality verification

**User Story:** As a developer, I want repeatable UI and BFF validation, so that changes preserve authenticated browser behavior.

#### Acceptance Criteria

4.1. WHEN frontend code changes THEN the project SHALL support TypeScript build, lint, and component-test validation.
4.2. WHEN browser behavior changes THEN the project SHALL support Playwright smoke validation against the built UI.
4.3. WHEN authentication or proxy configuration changes THEN the BFF SHALL be tested through its served origin rather than only the Vite development server.
