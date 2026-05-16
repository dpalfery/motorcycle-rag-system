# Feature Specification: Motorcycle RAG System Baseline

**Feature Branch**: `001-system-spec`  
**Created**: 2025-12-25  
**Status**: Draft  
**Input**: User description: "Reverse engineer Motorcycle RAG System feature specification from existing docs and constitution"

## Clarifications

### Session 2025-12-25

- Q: What is the canonical identity provider strategy for user sign-in (the OIDC `Authority` used by the BFF)? → A: Microsoft Entra External ID / B2C with social sign-in (Google, GitHub, Microsoft, Facebook).

- Q: What OWASP ASVS v5.0.0 verification level should this system “match and pass”? → A: ASVS Level 2.

- Q: How should administrator authentication work (for ingestion, web sources, MCP config, plan/SKU management)? → A: Admins use Microsoft Entra ID (work accounts); customers use Entra External ID / B2C.

- Q: How should admin authorization be represented in tokens/claims? → A: Entra app roles (e.g., `Admin`, `Operator`, `Viewer`).

- The system MUST be designed to match and pass OWASP ASVS v5.0.0 security verification at ASVS Level 2.

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Ask a motorcycle question (Priority: P1)

As a motorcycle enthusiast (or mechanic), I want to ask a motorcycle-related question and receive a single, clear answer backed by sources, so I can make confident decisions without searching multiple sites and documents myself.

**Why this priority**: This is the core product value: fast, trustworthy answers with traceability.

**Independent Test**: Can be fully tested by submitting a set of representative questions and verifying (a) an answer is returned and (b) at least one supporting source is included when available.

**Acceptance Scenarios**:

1. **Given** the system has indexed motorcycle reference content, **When** a user submits a specification question (e.g., make/model/year specs), **Then** the system returns an answer and includes the sources used.
2. **Given** the system has multiple source types available, **When** a user asks a question that requires combining information from more than one source, **Then** the system returns one unified response that merges the information and preserves source attribution.
3. **Given** no relevant information exists across available sources, **When** the user submits a query, **Then** the system returns a clear "no results" response and suggests how to refine the query.
4. **Given** the system returns an answer, **When** the user views the response, **Then** every factual claim is supported by at least one citation and the user can locate the cited material in the original source.
5. **Given** the system proposes an answer from retrieved evidence, **When** the verification step detects insufficient support or contradictions, **Then** the system withholds or qualifies the claim and explains the limitation.

---

### User Story 1a - Sign in and manage profile (Priority: P1)

As a user, I want to authenticate (via Microsoft Entra External ID / B2C, including social sign-in) and have a profile, so that the system can recognize me, apply the correct usage limits for my plan (SKU), and provide a consistent personalized experience.

**Why this priority**: Usage limits and plan enforcement require reliable user identity; profile data supports future personalization and account management.

**Independent Test**: Can be tested by creating a user, signing in, retrieving/updating profile data, and confirming the system uses the authenticated identity for request tracking.

**Acceptance Scenarios**:

1. **Given** a user has an account, **When** they sign in successfully, **Then** the system establishes an authenticated session/identity for subsequent requests.
2. **Given** a signed-in user, **When** they view their profile, **Then** the system returns their profile details including their current plan/SKU.
3. **Given** a signed-in user, **When** they update allowed profile fields, **Then** the system persists the changes and the updated values are returned on subsequent reads.
4. **Given** an authenticated request, **When** the user submits a motorcycle query, **Then** the system associates the request to that user for usage tracking.

---

### User Story 2 - Ingest structured specifications (Priority: P2)

As a system administrator, I want to upload and process structured motorcycle specification datasets (single or batch) and manage pipeline runs (status, metrics, cancel, scheduling), so that the searchable corpus stays current and operators can control ingestion safely.

**Why this priority**: High-quality structured data improves accuracy and reduces reliance on external sources; operational controls reduce ingestion risk.

**Independent Test**: Can be tested by uploading a sample dataset, running processing (including batch), verifying status/metrics are available, and confirming that targeted spec queries return results derived from that dataset.

**Acceptance Scenarios**:

1. **Given** a valid structured dataset is provided, **When** an administrator uploads and processes it, **Then** the system makes its contents searchable and available to question-answering.
2. **Given** a dataset has missing or malformed rows, **When** processing runs, **Then** the system reports warnings/errors without corrupting previously indexed content.
3. **Given** multiple files are provided, **When** an administrator uploads and processes them as a batch, **Then** the system provides a batch result that indicates per-file outcomes.
4. **Given** an ingestion run is executing, **When** an administrator checks status, **Then** the system returns the current execution status and high-level progress signals.
5. **Given** an ingestion run is executing, **When** an administrator requests cancellation, **Then** the system attempts to stop processing and reports whether cancellation succeeded.
6. **Given** scheduled ingestion is enabled, **When** an administrator triggers an immediate scheduled run or views scheduled statistics, **Then** the system provides execution results and scheduling stats.

---

### User Story 3 - Search maintenance manuals (Priority: P2)

As a mechanic, I want to search maintenance and service manuals and receive answers with exact references (e.g., section/page), so I can quickly locate the authoritative procedure.

**Why this priority**: Manuals are authoritative for maintenance procedures; referencing improves trust and usability.

**Independent Test**: Can be tested by ingesting a known manual and verifying that queries return relevant excerpts and precise references.

**Acceptance Scenarios**:

1. **Given** manuals have been ingested, **When** a user asks for a maintenance procedure, **Then** the system returns steps and includes references that allow locating the original content.
2. **Given** a manual contains structured elements (tables/sections), **When** results are returned, **Then** the structure is preserved well enough to interpret the content correctly.

---

### User Story 3a - Admin UI for data ingress (Priority: P2)

As a system administrator, I want a .NET MAUI admin application (Windows-first) to upload documents (PDFs and datasets), run or trigger chunking/vectorization, and monitor ingestion jobs, so I can populate and maintain the system’s searchable corpus without needing to call APIs directly.

**Why this priority**: A practical ingestion workflow is required to keep the system accurate and up to date; a dedicated admin UI reduces operational friction and mistakes.

**Independent Test**: Can be tested by using the MAUI admin application to upload a PDF, run local chunking/vectorization, submit the processed artifacts to the system, and verify that the content becomes searchable and traceable.

**Acceptance Scenarios**:

1. **Given** an administrator has a PDF or CSV dataset, **When** they use the admin application to upload it, **Then** the application validates basic constraints (type/size) and provides a clear success/failure outcome.
2. **Given** a PDF is selected for ingestion, **When** the administrator chooses local processing, **Then** the admin application performs chunking and vectorization locally on the Windows machine and does not require a cloud-hosted model call for that processing.
3. **Given** an ingestion job is started from the admin application, **When** the administrator monitors it, **Then** the application shows job status and actionable errors/warnings.
4. **Given** an ingestion job is running, **When** the administrator requests cancellation from the admin application, **Then** the system attempts to cancel the job and the application reflects the result.

---

### User Story 6 - Add websites for indexing (Priority: P2)

As a system administrator, I want to add and manage a list of websites that the system can scrape and index for motorcycle information, so that the system can incorporate trusted web content into search and answers.

**Why this priority**: Keeping curated, high-quality web sources indexed reduces repeated live web calls and improves answer quality.

**Independent Test**: Can be tested by adding an allowed website, running a scrape/index job, and verifying that content from that website becomes searchable with correct source attribution.

**Acceptance Scenarios**:

1. **Given** an administrator provides a website URL, **When** they add it as an allowed source, **Then** the system validates the URL and stores it as an approved web source.
2. **Given** an approved web source exists, **When** the system performs a scrape/index run, **Then** the system indexes content from that source and makes it available for retrieval with attribution to that website.
3. **Given** a web source is removed or disabled, **When** the administrator updates the web sources list, **Then** the system stops scraping that source and prevents new indexing from it.
4. **Given** a web source is temporarily unavailable or rate-limits access, **When** a scrape/index run occurs, **Then** the system records the failure and continues processing other sources.
5. **Given** multiple sources contain similar content, **When** results are returned, **Then** the system avoids excessive duplication in user-visible results and preserves distinct source attribution.

### User Story 7 - Configure MCP tools in the MAUI admin application (Priority: P3)

As a system administrator, I want to configure available tools (via Model Context Protocol) from within the MAUI admin application, so that the system can safely enable, disable, and update tool integrations without redeploying.

**Why this priority**: Tooling integrations change frequently; managing them through the MAUI admin application improves operational agility and governance.

**Independent Test**: Can be tested by adding a tool configuration in the MAUI admin application, validating it, enabling it, and verifying that the tool becomes available for authorized system use.

**Acceptance Scenarios**:

1. **Given** an administrator is signed in with appropriate privileges, **When** they open tool configuration in the MAUI admin application, **Then** they can view the current list of configured tools and their enabled/disabled state.
2. **Given** a tool configuration is created or edited, **When** it is saved, **Then** the system validates required fields and rejects invalid configurations with actionable messages.
3. **Given** a tool configuration is enabled, **When** the system needs to use tools to complete a task, **Then** the enabled tool is available to the system according to its configuration and permissions.
4. **Given** a tool configuration is disabled, **When** the system executes workflows, **Then** the disabled tool is not used.
5. **Given** tool configuration changes occur, **When** administrators review system activity, **Then** the system can provide an audit trail of changes (who/when/what changed).

### User Story 4 - Operate reliably under failures (Priority: P3)

As a system operator, I want the system to continue to provide partial results when some sources are unavailable, so that users still get value during outages and degradation events.

**Why this priority**: Reliability is essential for production use and reduces incident impact.

**Independent Test**: Can be tested by simulating the unavailability of individual source types and verifying that the system still returns a best-effort answer with clear messaging.

**Acceptance Scenarios**:

1. **Given** one source is temporarily unavailable, **When** a user submits a query, **Then** the system returns results from remaining sources and clearly indicates limitations.
2. **Given** transient failures occur, **When** the user repeats a query after recovery, **Then** results return to normal without manual intervention.

### User Story 5 - Keep user data secure (Priority: P3)

As a security-conscious stakeholder, I want the system to protect secrets and user query data, so that operational security and privacy risks are minimized.

**Why this priority**: The system depends on sensitive credentials and handles user questions that may be private.

**Independent Test**: Can be tested by verifying that secrets are not stored in code/config files, and by confirming that logs do not contain sensitive data.

**Acceptance Scenarios**:

1. **Given** the system is configured for an environment, **When** it starts, **Then** it loads secrets from secure sources and does not require secrets to exist in source-controlled files.
2. **Given** user-provided query text, **When** the system logs operational events, **Then** logs avoid leaking sensitive content and include correlation identifiers.

### Edge Cases

- Empty/whitespace query submitted.
- Query is extremely long or contains unusual characters.
- Queries that look malicious (injection-like patterns) are submitted.
- Ingested documents are very large or partially corrupted.
- Multiple sources disagree; response must avoid presenting speculation as fact.
- External sources rate-limit the system; the user should still get best-effort results.
- Duplicate or near-duplicate content appears across sources.
- A user reaches their daily request limit and submits an additional request.
- Daily usage limit boundary behavior (e.g., reset timing) is reached while a user is active.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST accept a motorcycle-related natural-language query and return a response.
- **FR-002**: System MUST search available sources in a prioritized sequence (indexed data first, then external augmentation, then deep manual content as fallback) when prior steps are insufficient.
- **FR-003**: System MUST return a unified response that attributes information to its sources.
- **FR-004**: When no relevant information is found, the system MUST return a clear "no results" response and guidance for query refinement.

- **FR-004a**: The system MUST honor user query preferences that enable/disable source categories (e.g., web sources, manual/PDF sources) when those preferences are provided.
- **FR-004b**: The system MUST include a unique query identifier and query metrics in each successful query response.

- **FR-004c**: The system MUST apply an agentic verification strategy for factual correctness: one step produces candidate claims from evidence, and a separate verification step validates those claims against the cited evidence.
- **FR-004d**: When verification cannot confirm a claim to a high standard of support, the system MUST not present the claim as certain; it MUST either (a) omit the claim or (b) present it with a clear uncertainty qualification.

- **FR-004e**: Every response containing factual claims MUST include citations that allow the user to locate the supporting evidence in the original source.

### Citation Requirements

For each cited item, the system provides a structured reference with enough detail for a user to independently verify the claim.

- **Manual / PDF sources** MUST include (when available): motorcycle make/model/year, manual identifier/title, section heading/number, and page number.
- **Web sources** MUST include: full URL and (when available) a page title and retrieval date.
- **Structured/open datasets** MUST include: dataset name/version (or equivalent identifier) and the specific record key/row identifier used.

If required citation fields are not available, the system MUST provide the best available locator information and explicitly state what is missing.

- **FR-005**: System MUST support uploading and validating files intended for ingestion.
- **FR-006**: System MUST expose upload constraints (supported file types/extensions, size and batch limits) so administrators can prepare compliant uploads.
- **FR-007**: System MUST support ingesting structured motorcycle specification datasets and making them searchable.
- **FR-008**: System MUST support ingesting maintenance manual content and making it searchable.
- **FR-009**: Manual-derived results MUST include references sufficient for a user to locate the original content (e.g., section/page/figure identifiers where available).

- **FR-010**: The system MUST support initiating ingestion processing for a previously uploaded file.
- **FR-011**: The system MUST support uploading and processing multiple files as a batch and return per-file outcomes.
- **FR-012**: The system MUST provide ingestion execution status by execution identifier.
- **FR-013**: The system MUST provide ingestion metrics over a selectable time window.
- **FR-014**: The system MUST allow administrators to request cancellation of an in-progress ingestion execution.
- **FR-015**: The system MUST support scheduled ingestion operations, including (a) triggering an immediate scheduled run and (b) retrieving scheduled processing statistics.

- **FR-016**: The system MUST support augmenting answers with information from external web sources when indexed sources are insufficient.
- **FR-017**: The system MUST apply source-trust safeguards by enforcing the Trust Policy (Source Trust Tiers) defined below (e.g., prefer authoritative/trusted sources and avoid low-credibility content).

### Trust Policy (Source Trust Tiers)

The system assigns each source to exactly one trust tier based on its provenance and governance. Trust tier is used for retrieval ordering, scoring, and claim verification.

- **Tier A (Authoritative)**: OEM/manufacturer documentation (service manuals, owner manuals, service bulletins), official manufacturer websites, and other primary sources that are authoritative for technical procedures and specifications.
- **Tier B (Reputable secondary)**: Established, professionally maintained publishers or organizations with clear editorial oversight (non-user-generated), stable content, and a track record of accuracy.
- **Tier C (Community / low assurance)**: User-generated or minimally governed content (forums, Q&A sites, wikis, social posts, personal blogs).

**Enforcement rules**:

1. **Default preference**: When multiple sources are available for a query, retrieval and ranking MUST prefer Tier A over Tier B, and Tier B over Tier C.
2. **Allowlist-first for web**: Web retrieval and indexing MUST be restricted to administrator-approved domains. Unknown domains are treated as ineligible for retrieval/indexing (not merely Tier C).
3. **No Tier C as sole support for high-risk claims**: Claims about safety-critical procedures (e.g., brakes, torque specs, fuel system, electrical safety), or precise numeric specifications (torque values, capacities, service intervals) MUST NOT be presented as certain if supported only by Tier C evidence.
4. **Corroboration requirement**:
  - A claim may be presented as certain if supported by **at least one Tier A** citation; OR
  - If Tier A is unavailable, the claim may be presented as certain if supported by **two independent Tier B** citations that do not share the same originating publisher/host.
  - Otherwise, the system MUST qualify the claim (uncertain) or omit it.
5. **Labeling**: If Tier C evidence is included in results, the system MUST label it as community/low-assurance in the returned source metadata and MUST avoid presenting it as authoritative.
6. **Auditability**: The system SHOULD record, per query, the trust tier(s) used for the final answer and whether corroboration rules were satisfied (suitable for operational review and troubleshooting).

- **FR-018**: The system MUST handle partial outages by returning best-effort results from remaining sources and clearly indicating limitations.
- **FR-019**: The system MUST implement user-friendly error handling that avoids disclosing sensitive internal details.

- **FR-020**: The system MUST not require secrets (keys, tokens, connection strings) to be stored in source-controlled files.
- **FR-021**: The system MUST protect user query data in logs (avoid logging raw sensitive text; use correlation identifiers).

- **FR-022**: The system MUST provide health status signals suitable for operational monitoring.
- **FR-023**: The system MUST track basic operational metrics (query success rate, duration, error rate) for continuous improvement.

- **FR-024**: The system MUST provide an administrator-facing user interface for managing data ingress.
- **FR-025**: The administrator-facing user interface MUST run as a Windows desktop application.
- **FR-026**: The administrator-facing user interface MUST support selecting and uploading PDFs and structured datasets for ingestion.
- **FR-027**: The administrator-facing user interface MUST support a local processing mode where chunking and vectorization are executed on the Windows machine using local models.
- **FR-028**: The administrator-facing user interface MUST allow administrators to start ingestion processing (single and batch) and view per-file outcomes.
- **FR-029**: The administrator-facing user interface MUST allow administrators to view ingestion status, view ingestion metrics, and request cancellation of running ingestion executions.

- **FR-030**: The system MUST allow administrators to register, update, disable, and remove approved websites for scraping and indexing.
- **FR-031**: The system MUST validate candidate website entries (e.g., valid URL format and uniqueness) before approving them for scraping.
- **FR-032**: The system MUST support executing scrape/index runs for approved websites and making indexed web content searchable with clear website attribution.
- **FR-033**: The system MUST record scrape/index run outcomes per website (successes, failures, and actionable error details) and continue best-effort processing across multiple sources.

- **FR-034**: The MAUI admin application MUST provide an administrator-facing interface to configure MCP server/tool integrations.
- **FR-035**: MCP tool configurations MUST support enabling/disabling tools and updating tool configuration without requiring a redeploy.
- **FR-036**: MCP tool configuration changes MUST be access-controlled so only authorized administrators can change tool settings.
- **FR-037**: MCP tool configuration changes MUST be auditable (who changed what and when).
- **FR-037a**: The system MUST persist MCP configuration (servers/tools, enabled state, non-secret settings) in a shared store.
- **FR-037b**: The agent orchestration layer MUST consume MCP configuration via a provider that supports live updates (e.g., version/ETag change detection), applying configuration changes to new runs without breaking in-flight requests.

- **FR-038**: The system MUST support user authentication so that users can be uniquely identified.
- **FR-038a**: The system MUST use Microsoft Entra External ID / B2C for user authentication via OIDC.
- **FR-038b**: The system MUST support social identity providers for sign-in (at minimum: Google, GitHub, Microsoft, Facebook).
- **FR-038c**: Administrative access (ingestion operations, web source management, MCP configuration, user/SKU administration) MUST require Microsoft Entra ID (workforce) authentication.
- **FR-038d**: Administrative authorization MUST use Microsoft Entra ID application roles carried in the authenticated token/claims (e.g., `Admin`, `Operator`, `Viewer`).
- **FR-039**: The system MUST support user management capabilities including creating users and disabling/enabling user access.
- **FR-040**: The system MUST support user profiles and allow users to view and update permitted profile fields.
- **FR-041**: The system MUST define subscription SKUs/plans and associate each user to exactly one active plan at a time.
- **FR-042**: The system MUST enforce per-user daily request limits based on the user’s plan.
- **FR-043**: The system MUST track per-user request usage (count per day) and make the current usage and limit visible to the user.
- **FR-044**: When a user exceeds their plan’s daily request limit, the system MUST reject additional requests for that day with a clear message.
- **FR-045**: The system MUST provide administrative capabilities to assign or change a user’s plan/SKU.

- **FR-046**: The system MUST meet OWASP ASVS v5.0.0 security requirements at ASVS Level 2.

### SKU / Plan Definitions

The system defines the following plans and daily request limits:

| Plan (SKU) | Daily Request Limit | Notes |
|-----------:|---------------------:|-------|
| Free       | 10 requests/day      | Intended for evaluation/limited usage |
| Plus       | 100 requests/day     | Intended for regular personal use |
| Pro        | Unlimited            | No daily request limit; still subject to abuse protections |

### External API Contract (Observed Implementation)

This section captures the currently implemented external HTTP interface as a concrete contract.

#### Motorcycle Query API

- `POST /api/motorcycles/query`
  - Request body fields:
    - `query` (string, required)
    - `userId` (string, optional)
    - `preferences` (object, optional)
      - `includeWebSources` (boolean)
      - `includePDFSources` (boolean)
      - `maxResults` (integer)
      - `minRelevanceScore` (number)
      - `preferredSources` (string array)
    - `context` (object, optional)
      - `sessionId` (string)
      - `previousQueries` (string array)
      - `userPreferences` (object)
      - `language` (string)
      - `timestamp` (datetime)
      - `requiresMultiModal` (boolean)
      - `correlationId` (string)
  - Response body fields:
    - `response` (string)
    - `sources` (array of search results)
      - `id` (string)
      - `content` (string)
      - `relevanceScore` (number)
      - `source` (object)
        - `agentType` (enum)
        - `sourceName` (string)
        - `sourceUrl` (string)
        - `documentId` (string)
        - `lastUpdated` (datetime)
      - `metadata` (object)
      - `generatedAt` (datetime)
      - `highlights` (string array)
    - `metrics` (object)
    - `queryId` (string)
    - `generatedAt` (datetime)

  Notes:
  - When authentication is enabled, the system associates queries with the authenticated user for request limits and usage tracking. If a `userId` field is present, the system may ignore or override it in favor of the authenticated identity.

- `GET /api/motorcycles/health`

#### Platform/Service Health

- `GET /health`

#### Data Pipeline API

All routes are under `api/DataPipeline/*`.

- `POST /api/DataPipeline/upload` (single file upload; optional `processImmediately` query flag)
- `POST /api/DataPipeline/upload-batch` (batch file upload; optional `processImmediately` query flag)
- `POST /api/DataPipeline/process` (process a previously uploaded file)
- `POST /api/DataPipeline/process-batch` (batch processing)
- `GET /api/DataPipeline/status/{executionId}`
- `GET /api/DataPipeline/metrics?hours={hours}`
- `POST /api/DataPipeline/cancel/{executionId}`
- `POST /api/DataPipeline/scheduled/execute`
- `GET /api/DataPipeline/scheduled/stats`
- `GET /api/DataPipeline/health`
- `GET /api/DataPipeline/upload/constraints`

### Acceptance Mapping (Requirements → Scenarios)

- **FR-001–FR-004b** are accepted via **User Story 1** scenarios 1–3.
- **FR-004c–FR-004e** are accepted via **User Story 1** scenarios 4–5.
- **FR-038–FR-045** are accepted via **User Story 1a** scenarios 1–4 and operational verification of limit enforcement.
- **FR-046** is accepted via operational security verification (OWASP ASVS v5.0.0 checklist evidence) plus targeted security testing.
- **FR-005–FR-015** are accepted via **User Story 2** scenarios 1–6.
- **FR-007–FR-009** are accepted via **User Story 3** scenarios.
- **FR-024–FR-029** are accepted via **User Story 3a** scenarios 1–4.
- **FR-030–FR-033** are accepted via **User Story 6** scenarios 1–5.
- **FR-034–FR-037** are accepted via **User Story 7** scenarios 1–5.
- **FR-016–FR-017** are accepted via **User Story 1** scenario 2.
- **FR-018–FR-019** are accepted via **User Story 4** scenarios 1–2.
- **FR-020–FR-021** are accepted via **User Story 5** scenarios 1–2.
- **FR-022–FR-023** are accepted via operational verification (health endpoints and metrics).

### Assumptions & Dependencies

- The system has access to at least one authoritative source corpus (structured specs and/or manuals) to provide grounded answers.
- External sources may change, be unavailable, or rate-limit access; the system must remain useful via best-effort behavior.
- Administrators have a supported mechanism to upload files and manage ingestion runs via the system's external interface.
- The Windows admin application has access to local model assets required for chunking/vectorization in local processing mode.
- Administrators provide a curated list of websites that are appropriate to scrape and index for motorcycle information.
- Tool integrations configured via the Windows admin application (MAUI) are intended for administrative control and governance; tool configuration does not grant blanket access beyond the configured permissions.
- Daily request limits are evaluated and reset using a consistent system-defined day boundary (assumed UTC) to avoid ambiguity.
- User profile and chat history are explicitly out of scope for this baseline spec, but may be added later.

- Transport request/response models (DTOs) are defined in `3-Domain/MotorcycleRAG.Contracts.Models` and MUST remain domain-independent (no references to `MotorcycleRAG.Domain` entities/value objects).

- User authentication is provided by Microsoft Entra External ID / B2C (OIDC), including social identity providers.
- Administrative access is provided by Microsoft Entra ID (workforce) and is separate from customer identity.
- Administrative authorization is enforced using Entra application roles conveyed via token claims.

### Key Entities *(include if feature involves data)*

- **Query**: User-submitted question text plus optional preferences (e.g., source preferences, verbosity).
- **User**: An authenticated identity with a profile and an associated plan/SKU.
- **User Profile**: User-visible account/profile data, including current plan/SKU and usage status.
- **Plan (SKU)**: Subscription tier definition, including daily request limit policy.
- **Usage Record**: A per-user, per-day record tracking request count for limit enforcement.
- **Search Result**: A candidate piece of evidence with content excerpt, relevance score, and source attribution.
- **Source Reference**: Information that allows locating the original source (e.g., document name, section, page number, URL).
- **Claim**: A discrete factual statement proposed for inclusion in an answer.
- **Verification Result**: The outcome of validating a claim against its cited evidence (supported, unsupported, conflicting, insufficient evidence).
- **Document**: A unit of searchable content (structured specification record, manual chunk, web snippet).
- **Ingestion Job**: A run that processes and publishes a dataset/manual into the searchable corpus with status and outcomes.
- **Execution/Correlation ID**: Identifier used to trace a query across the system for observability.
- **Admin Ingestion Session**: An administrator-initiated ingestion workflow instance within the Windows admin application, including selected files, chosen processing mode, and outcomes.
- **Local Processing Profile**: Administrator-selected configuration for local chunking/vectorization (e.g., processing mode, output options), without prescribing specific implementation technologies.
- **Web Source**: An administrator-approved website entry (URL and related metadata) that is eligible for scraping and indexing.
- **Web Scrape/Index Run**: A processing run for a specific web source that produces indexed content and run outcomes.
- **Tool Definition**: A configured tool integration entry, including enablement state, permissions, and connection settings.
- **Tool Configuration Audit Event**: A record of a tool configuration change (who/when/what changed).

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: For every successful query, the response includes a non-empty query identifier and a timestamp.
- **SC-002**: When the system cannot find relevant information, the response clearly indicates no results and provides at least one refinement suggestion.
- **SC-002a**: For responses containing factual claims, every claim includes at least one citation with sufficient location detail for a user to find the cited material.
- **SC-002b**: When retrieved evidence is contradictory or insufficient, the system does not present the disputed claim as certain and clearly communicates the limitation.
- **SC-003**: For every ingestion run started via the pipeline interface, the system returns an execution identifier and exposes status retrievable by that identifier.
- **SC-004**: Ingestion runs produce actionable outcomes (successes, warnings, errors) without silent failures.
- **SC-005**: No secrets appear in source control, build logs, or application logs (validated via automated scanning and review).
- **SC-006**: Administrators can complete an end-to-end ingestion workflow using the Windows admin application: select files, validate, start processing, monitor status, and see completion outcomes.
- **SC-007**: In local processing mode, the Windows admin application can complete chunking and vectorization using local models without requiring a cloud-hosted model call for that processing.
- **SC-008**: Administrators can add an approved website, run scraping/indexing, and subsequently retrieve search results attributed to that website.
- **SC-009**: Administrators can enable/disable an MCP tool integration from the Windows admin application (MAUI) and the enabled/disabled state is reflected in system tool availability.
- **SC-010**: Tool configuration changes are traceable via an audit trail that includes who changed what and when.
- **SC-011**: The system enforces plan-based request limits: Free users cannot exceed 10 queries/day; Plus users cannot exceed 100 queries/day; Pro users are not blocked by a daily limit.
- **SC-012**: Signed-in users can view their current plan/SKU and current day usage status in the product.
- **SC-013**: The system passes OWASP ASVS v5.0.0 at ASVS Level 2, with recorded evidence suitable for audit/review.
