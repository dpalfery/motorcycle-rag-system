# Feature Specification: Fabric Ingestion Pipeline

**Feature Branch**: `001-fabric-ingestion-pipeline`  
**Created**: 2026-02-08  
**Status**: Draft  
**Input**: User description: "@6-Docs\\fabric-processor.md"

## Clarifications

### Session 2026-02-08

- Q: What's the maximum acceptable end-to-end time for ingesting an 800-2000 page manual in the standard environment before it's considered a problem? -> A: <= 6 hours
- Q: What completeness threshold is required for an 800-2000 page manual ingestion to be considered successful (no content loss)? -> A: >= 95% pages captured
- Q: For service manuals that cover multiple model years/variants, how should the manual be linked to bike models? -> A: One manual can link to many bike models/years
- Q: If a manual contains scanned/image-only pages (no extractable text), what is required? -> A: Convert all scanned pages into searchable text; store page images for all manuals so they can be shown during retrieval; users can request a specific page
- Q: Who is allowed to request and view a specific manual page (the per-page viewable representation)? -> A: Authenticated end users with a single shared manuals entitlement; manuals are free and do not require per-manual licensing

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ingest Service Manuals (Priority: P1)

As a content administrator, I can ingest a motorcycle service manual so that the
system extracts structured, searchable knowledge while preserving document
hierarchy and source references (e.g., section path and page references).

**Why this priority**: Manuals contain the highest-value maintenance procedures,
torque specs, and capacities; without them the experience lacks fidelity.

**Independent Test**: Ingest a known manual and confirm that key procedures and
torque specs are searchable and always link back to the correct source reference.

**Acceptance Scenarios**:

1. **Given** a valid service manual, **When** I run ingestion, **Then** the system
   produces searchable manual sections with section-path metadata and page-level
   source references.
2. **Given** a manual section containing a torque specification, **When** ingestion
   completes, **Then** the system captures the numeric value and its unit and
   associates it to the correct manual section.

---

### User Story 2 - Ingest Bike Specifications Dataset (Priority: P2)

As a content administrator, I can ingest a structured motorcycle specification
dataset so that bike models are created/updated consistently (including common
alias/format variations).

**Why this priority**: Specs are necessary to anchor manuals and to answer
questions that depend on structured attributes.

**Independent Test**: Ingest a sample dataset with known duplicates/aliases and
verify that canonical bike models are created and duplicates are handled
deterministically.

**Acceptance Scenarios**:

1. **Given** a spec dataset containing multiple naming variants for the same bike
   model, **When** I run ingestion, **Then** the system produces a single canonical
   bike model record with recorded aliases.

---

### User Story 3 - Link Manuals + Specs for Cross-Linked Retrieval (Priority: P3)

As a user of the RAG system, I can ask a maintenance question (e.g., torque spec
or tool requirement) and get an answer that uses the ingested manual knowledge,
is connected to the correct bike model, and always includes source references.

**Why this priority**: The value of ingestion is realized only when the knowledge
is connected across sources and supports higher-accuracy retrieval.

**Independent Test**: Run a benchmark set of questions tied to known sources and
verify that answers point to the correct manual sections and the correct bike
model attributes.

**Acceptance Scenarios**:

1. **Given** a bike model with an ingested manual, **When** I request a torque spec
   for a specific procedure, **Then** the system can return the correct value and
   cite the exact manual source reference.
2. **Given** a bike model with an ingested manual, **When** I ask to view a
   specific manual page, **Then** the system can return that page with the
   correct manual identity and page reference.

---

### Edge Cases

- Manual file is corrupted, password-protected, or unreadable.
- Manual is large (800 to 2000 pages), which is a normal ingestion path.
- Manual text extraction yields empty/low-confidence content (e.g., scanned pages).
- Manual contains scanned/image-only pages; the system must still capture the page content and keep it viewable.
- Manual has some unreadable pages; ingestion reports which pages could not be extracted.
- Manual applies to multiple model years/variants; linkage must not force a single year.
- Spec dataset has missing required identifiers (make/model/year) or inconsistent types.
- Duplicate ingestion runs create conflicting records.
- Partial pipeline failure occurs after some outputs are written.
- Source content contains unexpected characters/formatting that breaks parsing.
- Ingestion is triggered by an unauthorized user.
- A user without the manuals entitlement attempts to view a manual page.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support creating ingestion jobs for service manuals and
  structured spec datasets.
- **FR-002**: System MUST preserve manual hierarchy (chapter/section path) and
  MUST attach source references (at least page-level) to all extracted manual
  content.
- **FR-003**: System MUST split manuals into discrete, searchable sections/chunks
  and MUST assign stable identifiers to each section.
- **FR-004**: System MUST extract key maintenance knowledge from manuals,
  including at minimum torque specifications and fluid capacities when present.
- **FR-004a**: System MUST handle manuals in the 800 to 2000 page range as a
  standard ingestion path and MUST NOT truncate or skip content solely due to
  document length.
- **FR-005**: System MUST store extracted manual content in a searchable index so
  that retrieval can return the original source references.
- **FR-005a**: System MUST store a per-page viewable representation for manuals
  (e.g., page images) and MUST be able to present these pages during retrieval.
- **FR-005b**: Users MUST be able to request a specific manual page and receive
  that page along with its manual identity and page reference.
- **FR-005c**: Viewing manual pages MUST be restricted to authenticated end users
  with a single shared manuals entitlement that applies to all manuals.
- **FR-006**: System MUST ingest structured motorcycle specs and MUST create or
  update canonical bike model records.
- **FR-007**: System MUST normalize common model naming variations and MUST
  retain aliases used during ingestion.
- **FR-008**: System MUST link canonical bike models to relevant manual
  sections/chunks.
- **FR-008a**: System MUST support linking a single manual to multiple canonical
  bike models/years when the source manual covers multiple variants.
- **FR-009**: System MUST support representing components and their relationships
  to manual sections (e.g., a torque spec applying to a component).
- **FR-010**: System MUST provide job status and progress (queued/running/
  succeeded/failed) and MUST provide a reason for failures.
- **FR-010a**: System MUST measure and report manual extraction coverage (at
  minimum: (1) percent of pages captured as viewable pages, (2) percent of pages
  with searchable text, and (3) a list of pages not captured). For 800 to 2000
  page manuals, a job MUST be considered successful only if >= 95% of pages are
  captured as viewable pages.
- **FR-011**: System MUST avoid duplicates: re-ingesting the same source MUST NOT
  create duplicate canonical records.
- **FR-012**: System MUST validate inputs (file type, size limits, and basic
  structural checks) before processing.
- **FR-013**: System MUST enforce authorization for ingestion actions.
- **FR-014**: System MUST provide audit-friendly logging of ingestion activity
  (who/what/when/outcome) without exposing sensitive content.
- **FR-015**: System MUST support reprocessing/retry of failed steps without
  requiring a full restart of the pipeline.
- **FR-016**: System MUST provide a way to measure and report ingestion quality
  (e.g., number of chunks produced, number of extracted attributes, and linkage
  coverage).
- **FR-017**: System MUST support operating within a $50/month cloud spend cap by
  providing budget visibility (cost reporting) and operational safeguards (alerts
  and throttling when appropriate).

- **FR-018**: System MUST support running the bulk of manual/spec ingestion workloads on Microsoft Fabric capacity (e.g., trial capacity) to minimize per-run cloud compute fees.

- **FR-019**: System MUST provide guardrails to prevent unexpected spend when using Fabric or any external paid services, including explicit workload limits (pages/bytes/runtime) and clear operator-facing failure messages when limits are exceeded.

- **FR-020**: If any pipeline step uses a metered external service (e.g., LLM token billing), the system MUST allow disabling that step per job and MUST provide a lower-cost fallback behavior.

### Key Entities *(include if feature involves data)*

- **IngestionJob**: A single run for one or more sources, including status,
  timestamps, and a summary of outputs.
- **ManualDocument**: A service manual source with identity, provenance, and one
  or more applicable BikeModel links (when the manual covers multiple
  model years/variants).
- **ManualSection**: A structured unit of manual content with hierarchy path and
  source references.
- **ManualPageAsset**: A per-page viewable artifact tied to a ManualDocument and
  a page reference.
- **BikeModel**: A canonical bike model record (make/model/year) with aliases.
- **SpecAttribute**: A normalized numeric or categorical spec for a bike model.
- **Component**: A named part or system referenced by manual content.
- **Relationship**: Links among BikeModel, ManualSection, Component, and
  SpecAttribute.

### Assumptions

- Only authorized administrators can ingest or reprocess sources.
- Manuals are free to view and do not require per-manual licensing.
- The system has access to the source manuals and spec datasets and can store
  derived artifacts and metadata.
- A benchmark set for validating torque-spec accuracy will be defined and
  maintained by the team.

## Out of Scope

- Implementing a local-laptop ingestion worker (RTX 5090) execution path is out of scope for this feature; the architecture should keep clean extension points to add it later.

## Implementation Notes (Non-Requirements)

### Authorization Notes

- Manual-page viewing should be enforced via a dedicated API authorization policy (recommended name: `mcr-api-manuals-view`).
- The policy should require authentication and a single entitlement signal that applies to all manuals.
- Recommended default signal (TBD at implementation time): an app role or claim that clearly maps to "manuals:view" (for example: role `User` OR `Viewer` plus claim `mcr_entitlements` contains `manuals`).

## Success Criteria *(mandatory)*

## Constitution Alignment *(mandatory)*

Describe how this feature complies with each principle in
`.specify/memory/constitution.md`.

- Security (I): Restrict ingestion to authorized roles; validate inputs; redact
  sensitive content in logs; ensure outputs preserve source references.
- Clean Architecture (II): Ingestion logic is implemented as application use
  cases with storage/processors behind abstractions; domain stays framework-free.
- Code Quality (III): Builds remain warning-free; one type per C# file; clear
  error handling and async-friendly I/O.
- Testing (IV): Automated tests cover manual parsing, spec normalization,
  linkage rules, and idempotency.
- Observability (V): Job telemetry includes progress, counts of outputs, and
  traceable job IDs across pipeline steps.
- Resilience (VI): Processing uses timeouts, retries where safe, and supports
  resuming failed steps without duplicating data.
- Process & Workflow (VII): Work is tracked in `specs/001-fabric-ingestion-pipeline/`
  and reviewed against the constitution gates before implementation.

### Measurable Outcomes

- **SC-001**: For a defined benchmark set of torque-spec questions, at least 95%
  of answers reference the correct source and provide the correct value.
- **SC-002**: For cross-linked maintenance questions, 95% of questions return an
  answer in under 3 seconds under the agreed test load.
- **SC-003**: Operators can ingest a 800 to 2000 page manual end-to-end in <= 6
  hours in the standard environment.
- **SC-004**: Monthly cloud operating cost attributable to ingestion and storage
  remains <= $50/month under the planned usage profile.
- **SC-005**: At least 90% of ingested manual sections include a valid hierarchy
  path and page-level source references.
- **SC-006**: For 800 to 2000 page manuals, ingestion achieves >= 95% page
  capture coverage (viewable pages), and any non-captured pages are reported.
- **SC-007**: Users can request a specific manual page and receive the correct
  page with its manual identity and page reference.
