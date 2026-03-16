# Feature Specification: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

**Feature Branch**: `develop`
**Created**: 2026-03-16
**Status**: Draft

## User Scenarios & Testing *(mandatory)*

### User Story 1 — LLM Orchestrator Decides Which Agents to Call (Priority: P1)

A user asks a motorcycle question. An OrchestratorAgent hosted in Azure AI Foundry Agent Service receives the query, reasons about what information is needed, and calls sub-agents (VectorSearch, WebSearch, PDFSearch) as tools — in any order, any combination, any number of times up to 4 rounds. Our .NET code only executes the I/O each sub-agent requests; all LLM reasoning runs in Foundry.

**Why this priority**: Core capability. Nothing else works without this.

**Independent Test**: Submit a query via `/api/motorcycles/query`. Inspect structured logs to confirm a Foundry run was created, tool calls were dispatched, and the final answer came from the thread messages — not from a local LLM call.

**Acceptance Scenarios**:

1. **Given** a query with strong RAG coverage, **When** the orchestrator runs, **Then** only `vector_search` is called; `web_search` is not called; answer is returned.
2. **Given** a query with weak or no RAG coverage (e.g., "best beginner bike 2025"), **When** the orchestrator runs, **Then** `vector_search` is called first, followed by `web_search` to augment.
3. **Given** a technical spec query (torque values, service intervals), **When** the orchestrator runs, **Then** `pdf_search` is called; answer cites the manual.
4. **Given** any query, **When** the Foundry run reaches the configured max rounds, **Then** the run completes with the best available answer from accumulated context; no error is returned to the user.
5. **Given** a Foundry run status of `RequiresAction`, **When** our .NET code receives it, **Then** the correct local tool handler is dispatched and outputs are submitted back within the request timeout.

---

### User Story 2 — Sub-Agents Run in Foundry, .NET Executes I/O Only (Priority: P1)

VectorSearchAgent, WebSearchAgent, and PDFSearchAgent are hosted in Foundry using `Phi-4-mini-flash-reasoning`. When the orchestrator calls one of them, our .NET code starts a sub-run on that agent, handles its tool calls (Azure AI Search queries, HTTP scraping, DB reads), and returns the agent's synthesised result to the orchestrator. No local LLM calls exist anywhere in the system.

**Why this priority**: Without this, the system has a split LLM surface and inconsistent observability. Tied to P1.

**Independent Test**: Trigger a query that uses web search. Confirm in Azure AI Foundry portal that two runs appear: one for OrchestratorAgent and one for WebSearchAgent. Confirm no LLM calls appear in local .NET logs.

**Acceptance Scenarios**:

1. **Given** the orchestrator calls `web_search`, **When** our .NET code handles the tool call, **Then** a new Foundry run is created on WebSearchAgent; the orchestrator run is paused pending the result.
2. **Given** WebSearchAgent calls `get_trusted_sources`, **When** our .NET code handles it, **Then** enabled web sources are loaded from the database and returned as tool output.
3. **Given** WebSearchAgent calls `fetch_web_content(url, term)`, **When** our .NET code handles it, **Then** the URL is fetched via HTTP and extracted content is returned; no LLM call is made locally.
4. **Given** VectorSearchAgent calls `execute_azure_search`, **When** our .NET code handles it, **Then** Azure AI Search is queried and results are returned as tool output.
5. **Given** a DB connection failure on `get_trusted_sources`, **When** our .NET code handles it, **Then** an empty result set is returned with a warning log; the WebSearchAgent run continues and returns a graceful result.

---

### User Story 3 — Agent Provisioning via Deploy Pipeline (Priority: P2)

All four Foundry agent definitions (system prompts, models, tool schemas) are created and updated exclusively via the GitHub Actions deploy pipeline. Agent IDs are written to Key Vault and read at runtime. No manual portal steps are required to stand up or update agents.

**Why this priority**: Required for reproducibility. Without it, a fresh deploy leaves agents undefined.

**Independent Test**: Delete all four agents from Foundry. Trigger the deploy pipeline. Confirm all four agents are re-created and the API starts successfully reading agent IDs from config.

**Acceptance Scenarios**:

1. **Given** no agents exist in Foundry, **When** the deploy pipeline runs, **Then** all four agents are created with correct system prompts, models, and tool schemas.
2. **Given** agents already exist, **When** the deploy pipeline runs, **Then** agent definitions are updated (upserted) — not duplicated.
3. **Given** agent IDs are written to Key Vault, **When** the API starts, **Then** it reads agent IDs from environment config without errors.

---

### User Story 4 — TrustedSources from Database (Priority: P2)

Trusted web sources configured via the Admin UI are used by WebSearchAgent at query time. Sources added or toggled in the admin UI are picked up on the next query with no API restart.

**Why this priority**: Without real trusted sources, web_search always returns empty.

**Independent Test**: Add a trusted source via Admin UI. Submit a query that triggers web search. Confirm the new source URL appears in the `fetch_web_content` tool call log.

**Acceptance Scenarios**:

1. **Given** sources exist in DB with `IsEnabled = true, IncludeInSearch = true`, **When** `get_trusted_sources` is called, **Then** those sources are returned.
2. **Given** a source is disabled, **When** `get_trusted_sources` is called, **Then** that source is excluded.
3. **Given** no sources in DB, **When** `get_trusted_sources` is called, **Then** empty array is returned; WebSearchAgent returns a graceful empty result.

---

### Edge Cases

- What if a Foundry sub-run fails (model error, timeout)? → Tool handler catches the exception, returns an error string as tool output; OrchestratorAgent reasons about the failure and continues or concludes.
- What if the Foundry API is unreachable? → `IFoundryAgentRunner` surfaces an exception; `MotorcycleRAGService` catches it and returns a service-unavailable response.
- What if a trusted source URL returns 4xx/5xx? → Per-source HTTP error is caught; remaining sources still scraped; empty content returned for failed source.
- What if the user sends a typo-heavy query? → WebSearchAgent's Phi-4 model handles normalisation as part of its reasoning before calling `fetch_web_content`.
- What if Key Vault is unavailable at startup? → API fails to start fast (fail-fast); agent IDs are non-optional config.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All four agents (Orchestrator, VectorSearch, WebSearch, PDFSearch) MUST be hosted in Azure AI Foundry Agent Service.
- **FR-002**: OrchestratorAgent MUST use `o4-mini`. Sub-agents (VectorSearch, WebSearch, PDFSearch) MUST use `Phi-4-mini-flash-reasoning`.
- **FR-003**: Our .NET code MUST NOT make direct LLM calls (no `GetChatCompletionAsync`, no `Azure.AI.OpenAI` chat completions in the agent path).
- **FR-004**: The OrchestratorAgent MUST have three callable tools: `vector_search`, `web_search`, `pdf_search`. Each tool triggers a Foundry sub-run.
- **FR-005**: WebSearchAgent MUST have three callable tools: `get_trusted_sources`, `fetch_web_content`, `score_content`. VectorSearchAgent MUST have `execute_azure_search`. PDFSearchAgent MUST have `search_pdf_index`.
- **FR-006**: All tool implementations in .NET MUST be pure I/O — no LLM calls, no business logic beyond data transformation.
- **FR-007**: The OrchestratorAgent MUST be configured with a maximum run round limit of 4.
- **FR-008**: `get_trusted_sources` MUST read from the database via `IWebSourceRepository`, filtered to `IsEnabled = true AND IncludeInSearch = true`.
- **FR-009**: Agent definitions MUST be provisioned via the GitHub Actions deploy pipeline using `Azure.AI.Projects` SDK. Agent IDs MUST be stored in Key Vault.
- **FR-010**: The API MUST read agent IDs from environment configuration (Key Vault-backed) at startup.
- **FR-011**: `IAzureFoundryClient.GetChatCompletionAsync` MUST be removed from the agent orchestration path. A new `IFoundryAgentRunner` interface MUST be introduced for thread/run lifecycle operations.
- **FR-012**: The existing `AgentFrameworkAdapter` MUST be replaced by `FoundryToolDispatcher` — a class that maps Foundry tool call names to local async handler methods.
- **FR-013**: The existing hard-coded sequential policy (`ExecuteSequentialRetrievalPolicyAsync`) MUST be removed.
- **FR-014**: Embeddings (`GetEmbeddingsAsync`) MAY continue to use DeepInfra via the existing path — this is outside the agent loop.

### Key Entities

- **OrchestratorAgent**: Foundry agent definition — o4-mini, system prompt, three tool schemas (vector_search, web_search, pdf_search).
- **WebSearchAgent**: Foundry agent definition — Phi-4-mini-flash-reasoning, three tool schemas.
- **VectorSearchAgent**: Foundry agent definition — Phi-4-mini-flash-reasoning, one tool schema.
- **PDFSearchAgent**: Foundry agent definition — Phi-4-mini-flash-reasoning, one tool schema.
- **IFoundryAgentRunner**: New interface in Contracts — thread/run lifecycle.
- **FoundryAgentRunner**: New implementation in Persistence — wraps `Azure.AI.Agents.Persistent`.
- **FoundryToolDispatcher**: New class in Application — maps tool names to handler delegates.
- **AgentProvisioningService**: New class in Persistence — creates/updates agent definitions in Foundry via `Azure.AI.Projects`. Called from the deploy pipeline bootstrap.

---

## Success Criteria *(mandatory)*

### Constitution Alignment

- **Security (I)**: No model names or agent IDs hardcoded. Agent IDs from Key Vault. Tool implementations validate inputs. No new unauthenticated endpoints. Azure Managed Identity used for Foundry auth.
- **Clean Architecture (II)**: `IFoundryAgentRunner` in Contracts; `FoundryAgentRunner` in Persistence. `FoundryToolDispatcher` in Application. No Foundry SDK types leak into Domain.
- **Code Quality (III)**: Zero warnings. Sequential policy removed cleanly. One class per file. All agent interactions fully async.
- **Testing (IV)**: Unit tests for `FoundryToolDispatcher` (mock tool handlers), `AgentOrchestrator` run coordination. Integration tests for provisioning and end-to-end run.
- **Observability (V)**: Structured logs per tool call (tool name, run ID, iteration). Foundry portal shows all runs. Thread IDs correlated to request IDs.
- **Resilience (VI)**: Max run rounds enforced by Foundry. Tool handler exceptions returned as error outputs (not crashes). DB failure on `get_trusted_sources` degrades gracefully.
- **Process (VII)**: New NuGet packages explicitly listed and approved. No IaC changes outside pipeline. Agent provisioning is pipeline-only.

### Measurable Outcomes

- **SC-001**: Queries with strong RAG coverage use `vector_search` only — confirmed in Foundry portal run history.
- **SC-002**: Queries with weak RAG trigger `web_search` — confirmed via trusted source fetch logs.
- **SC-003**: No run exceeds 4 tool-call rounds.
- **SC-004**: Fresh deploy pipeline run provisions all four agents end-to-end with no manual steps.
- **SC-005**: Trusted sources added via Admin UI are used on the next query without API restart.
- **SC-006**: Zero direct `chat/completions` API calls appear in network traces for a normal query.
