# Tasks: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

**Input**: `specs/develop/plan.md`, `specs/develop/spec.md`, `specs/develop/data-model.md`, `specs/develop/contracts/`
**Branch**: `develop`

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

---

## Phase 1: Setup (Packages, DTOs, Options)

**Purpose**: Add approved NuGet packages, create new DTO types, extend options config. No logic, no interfaces — pure structural setup. Everything in this phase can be done in parallel once packages are added.

- [X] T001 Add `Azure.AI.Agents.Persistent` 1.2.0-beta.2 to `4-Persistence/MotorcycleRAG.Persistence/MotorcycleRAG.Persistence.csproj`
- [X] T002 Add `Azure.AI.Projects` 1.1.0 to `4-Persistence/MotorcycleRAG.Persistence/MotorcycleRAG.Persistence.csproj`
- [X] T003 Add `Microsoft.Agents.AI.AzureAI.Persistent` 1.0.0-preview to `2-Application/MotorcycleRAG.Application/MotorcycleRAG.Application.csproj`
- [X] T004 [P] Create `AgentRunState` enum in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentRunState.cs` — values: Queued, InProgress, RequiresAction, Completed, Failed, Cancelled, Expired
- [X] T005 [P] Create `AgentToolCall` record in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentToolCall.cs` — properties: `string CallId`, `string FunctionName`, `string ArgumentsJson`
- [X] T006 [P] Create `AgentRunStatus` record in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentRunStatus.cs` — properties: `string RunId`, `AgentRunState State`, `IReadOnlyList<AgentToolCall>? RequiredToolCalls`
- [X] T007 [P] Create `AgentToolOutput` record in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentToolOutput.cs` — properties: `string CallId`, `string Output`
- [X] T008 [P] Add four agent ID properties to `0-Base/MotorcycleRAG.Core/Options/AzureFoundryOptions.cs`: `OrchestratorAgentId`, `VectorSearchAgentId`, `WebSearchAgentId`, `PDFSearchAgentId` — all `string`, no default values, env-var backed

**Checkpoint**: Packages referenced, DTOs exist, options extended — project builds.

---

## Phase 2: Foundational (Interfaces + Remove Old Code)

**Purpose**: Define new interfaces that all user story work depends on. Remove the old sequential pipeline, `AgentFrameworkAdapter`, and `GetChatCompletionAsync` from the agent path. This phase unblocks all P1 and P2 user story work.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T009 Create `IFoundryAgentRunner` interface in `3-Domain/MotorcycleRAG.Contracts/Interfaces/IFoundryAgentRunner.cs` — methods: `CreateThreadAsync`, `AddUserMessageAsync`, `CreateRunAsync`, `GetRunStatusAsync`, `SubmitToolOutputsAsync`, `GetLastAssistantMessageAsync`, `DeleteThreadAsync` — all returning Task with CancellationToken, using DTOs from T004–T007
- [X] T010 [P] Create `ITrustedSourcesLoader` interface in `3-Domain/MotorcycleRAG.Contracts/Interfaces/ITrustedSourcesLoader.cs` — single method: `Task<TrustedSourceOptions[]> LoadAsync(CancellationToken ct = default)`
- [X] T011 Remove `GetChatCompletionAsync` from `3-Domain/MotorcycleRAG.Contracts/Interfaces/IAzureFoundryClient.cs` — retain all `GetEmbeddingsAsync` and `ProcessMultimodalContentAsync` overloads only
- [X] T012 Remove `GetChatCompletionAsync` implementation from `4-Persistence/MotorcycleRAG.Persistence/Azure/AzureFoundryClientWrapper.cs` — remove the method body, the convenience overload, and all HTTP request/response parsing code for chat completions; retain embeddings and multimodal
- [X] T013 [P] Delete `2-Application/MotorcycleRAG.Application/Agents/AgentFrameworkAdapter.cs` — this file is replaced by `FoundryToolDispatcher` in Phase 3
- [X] T014 [P] Delete `2-Application/MotorcycleRAG.Application/Agents/ToolDefinitions.cs` — tool schemas now live in Foundry agent definitions provisioned by the pipeline
- [X] T015 Remove `ExecuteSequentialRetrievalPolicyAsync`, `ExecuteAgentSearchWithMetricsAsync`, `UpdateQueryContextMetrics`, and `BuildSearchOptions` from `2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs` — leave the class shell and constructor intact; mark `ExecuteSequentialSearchAsync` as `throw new NotImplementedException()` temporarily
- [X] T016 [P] Update `1-Presentation/MotorcycleRAG.API/Configuration/Services/SearchAgentsConfiguration.cs` — remove any registration of `AgentFrameworkAdapter`, `AgentFrameworkAdapterFactory`, or `ToolDefinitions`; update using directives accordingly

**Checkpoint**: Solution builds cleanly (zero warnings, NotImplementedException is acceptable at this stage). Old pipeline fully removed. New interfaces defined.

---

## Phase 3: User Stories 1, 2 & 4 — Foundry Run Coordination + Sub-agents + TrustedSources (Priority: P1/P2)

**Goal**:
- **US1**: `AgentOrchestrator` creates Foundry threads and runs on the OrchestratorAgent, handles `RequiresAction` by delegating tool calls, and extracts the final answer from thread messages.
- **US2**: Each orchestrator tool call (`vector_search`, `web_search`, `pdf_search`) starts a sub-run on the corresponding Foundry sub-agent. Sub-agents call back with their own tool calls (Azure Search, HTTP scraping, DB reads) which our .NET code executes and submits back.
- **US4**: `get_trusted_sources` tool handler loads enabled sources from the DB via `IWebSourceRepository`.

**Independent Test**:
- Submit `POST /api/motorcycles/query` with `{ "query": "best beginner motorcycle" }`.
- Confirm structured logs show: thread created, run created, `RequiresAction` handled, sub-run created (for web_search), sub-run tool calls dispatched, final answer extracted.
- Confirm Azure AI Foundry portal shows two runs (orchestrator + sub-agent) with tool call history.
- Confirm zero `chat/completions` calls in network traces.

### Tests

- [X] T017 [P] [US1] Write unit tests for `FoundryToolDispatcher` in `5-Test/tests/MotorcycleRAG.UnitTests/Agents/FoundryToolDispatcherTests.cs` — test: registered tool is dispatched; unregistered tool returns error output; exception in handler returns error output not throw; multiple tools dispatched in one call
- [X] T018 [P] [US1] Write unit tests for `AgentOrchestrator` run coordination in `5-Test/tests/MotorcycleRAG.UnitTests/Services/AgentOrchestratorRunTests.cs` — mock `IFoundryAgentRunner`; test: run completes on first round (no tool calls); run completes after one RequiresAction cycle; run completes after four rounds (max); failed run surfaces exception
- [X] T019 [P] [US2] Write unit tests for `SubAgentToolHandlers` in `5-Test/tests/MotorcycleRAG.UnitTests/Agents/SubAgentToolHandlersTests.cs` — test: `execute_azure_search` calls `AzureSearchClientWrapper`; `fetch_web_content` calls `WebContentExtractor`; `score_content` applies correct tier multiplier; `get_trusted_sources` calls loader and returns mapped sources; `search_pdf_index` calls PDF search index
- [X] T020 [P] [US4] Write unit tests for `DatabaseTrustedSourcesLoader` in `5-Test/tests/MotorcycleRAG.UnitTests/Services/TrustedSources/DatabaseTrustedSourcesLoaderTests.cs` — test: returns only enabled + includeInSearch sources; maps TrustTier to CredibilityScore correctly; returns empty array when no sources; DB exception propagates with log

### Implementation

- [X] T021 [P] [US1] Implement `FoundryAgentRunner` in `4-Persistence/MotorcycleRAG.Persistence/Azure/FoundryAgentRunner.cs` — implements `IFoundryAgentRunner` using `Azure.AI.Agents.Persistent` SDK; `CreateThreadAsync`, `AddUserMessageAsync`, `CreateRunAsync`, `GetRunStatusAsync` (polls until terminal or RequiresAction), `SubmitToolOutputsAsync`, `GetLastAssistantMessageAsync`, `DeleteThreadAsync`; all calls authenticated via `DefaultAzureCredential`; structured logging on every state transition
- [X] T022 [P] [US4] Implement `DatabaseTrustedSourcesLoader` in `2-Application/MotorcycleRAG.Application/Services/TrustedSources/DatabaseTrustedSourcesLoader.cs` — implements `ITrustedSourcesLoader`; calls `IWebSourceRepository.GetAllWebSourcesAsync()`; filters `IsEnabled && IncludeInSearch`; maps to `TrustedSourceOptions` per tier table in data-model.md; constructs `SearchUrlTemplate` as `"{Url}/search?q={query}"`; defaults `ContentSelector` to `"//p|//article|//div[@class='content']"`
- [X] T023 [US1] Implement `FoundryToolDispatcher` in `2-Application/MotorcycleRAG.Application/Agents/Orchestration/FoundryToolDispatcher.cs` — dictionary of `string → Func<AgentToolCall, CancellationToken, Task<AgentToolOutput>>`; `RegisterHandler(toolName, handler)` method; `DispatchAsync(IEnumerable<AgentToolCall>, CancellationToken)` method dispatches all calls, catches per-handler exceptions and returns error output (no throw); structured log per dispatch with tool name and callId (depends on T009, T005, T007)
- [X] T024 [US2] Implement `SubAgentToolHandlers` in `2-Application/MotorcycleRAG.Application/Agents/Orchestration/SubAgentToolHandlers.cs` — five handler methods registered with `FoundryToolDispatcher`: `HandleExecuteAzureSearchAsync` (calls existing `AzureSearchClientWrapper`), `HandleFetchWebContentAsync` (calls existing `WebContentExtractor`), `HandleScoreContentAsync` (applies trust tier multiplier from `WebSourceValidator` logic — no LLM call), `HandleGetTrustedSourcesAsync` (calls `ITrustedSourcesLoader`), `HandleSearchPdfIndexAsync` (calls Azure AI Search PDF index); each handler deserialises `ArgumentsJson`, executes I/O, returns JSON-serialised result as `AgentToolOutput.Output`; structured logging per handler (depends on T022, T023)
- [X] T025 [US1] Implement `OrchestratorToolHandlers` in `2-Application/MotorcycleRAG.Application/Agents/Orchestration/OrchestratorToolHandlers.cs` — three handler methods: `HandleVectorSearchAsync`, `HandleWebSearchAsync`, `HandlePDFSearchAsync`; each creates a Foundry thread, adds the query as a user message, creates a run on the corresponding sub-agent ID (from `AzureFoundryOptions`), drives the sub-agent run loop (poll → dispatch sub-agent tool calls via `FoundryToolDispatcher` → submit outputs → repeat until Completed), extracts final assistant message, deletes thread, returns synthesised result as `AgentToolOutput.Output`; sub-agent tool calls dispatched using the same `FoundryToolDispatcher` instance loaded with sub-agent handlers from T024; structured logging with sub-run ID (depends on T021, T023, T024, T008)
- [X] T026 [US1] Rewrite `AgentOrchestrator.ExecuteSequentialSearchAsync` in `2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs` — replace `NotImplementedException` body: create thread via `IFoundryAgentRunner`, add user message, create run on `OrchestratorAgentId`, enter poll loop — on `RequiresAction` dispatch tool calls via `FoundryToolDispatcher` (loaded with orchestrator handlers from T025) and submit outputs, on `Completed` extract answer via `GetLastAssistantMessageAsync`, delete thread; return `SearchResult[]` empty (orchestrator now returns answer directly — results are embedded in the answer); remove `GenerateResponseAsync` delegation from the main path; structured log per iteration with run ID, thread ID correlated to request correlation ID (depends on T021, T023, T025)
- [X] T027 [US1] Update `MotorcycleRAGService.QueryAsync` in `2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs` — remove `GenerateResponseAsync` call; `ExecuteSearchAsync` now returns `(SearchResult[] results, string answer)` tuple from orchestrator; skip `RefinementService.GenerateNoResultsResponse` if answer is non-empty; retain citation service and limitation analyzer on the answer text (depends on T026)
- [X] T028 [P] [US1] Register `IFoundryAgentRunner → FoundryAgentRunner` in `1-Presentation/MotorcycleRAG.API/Configuration/Services/AzureAIServiceConfiguration.cs` — scoped lifetime; inject `AzureFoundryOptions` for project connection string
- [X] T029 [P] [US4] Register `ITrustedSourcesLoader → DatabaseTrustedSourcesLoader` in `1-Presentation/MotorcycleRAG.API/Configuration/Services/SearchAgentsConfiguration.cs` — scoped lifetime
- [X] T030 [P] [US1] Register `FoundryToolDispatcher` as scoped in `1-Presentation/MotorcycleRAG.API/Configuration/Services/SearchAgentsConfiguration.cs`; register `OrchestratorToolHandlers` and `SubAgentToolHandlers` as scoped
- [X] T031 [US1] Write integration test `FoundryAgentRunnerIntegrationTests` in `5-Test/tests/MotorcycleRAG.IntegrationTests/Agents/FoundryAgentRunnerIntegrationTests.cs` — requires `AZURE_AI_PROJECT_CONNECTION_STRING` env var; creates real thread, adds message, creates run on a real agent, handles one `RequiresAction` cycle, submits dummy tool output, asserts run completes; cleans up thread in `finally` (depends on T021, T026)

**Checkpoint**: `POST /api/motorcycles/query` reaches Foundry, sub-agents run, answers return. Foundry portal shows runs. Zero local LLM calls. US1, US2, US4 independently verified.

---

## Phase 4: User Story 3 — Agent Provisioning via Deploy Pipeline (Priority: P2)

**Goal**: All four Foundry agent definitions are created/updated by the GitHub Actions pipeline. Agent IDs land in Key Vault. API reads them at startup. No manual portal steps.

**Independent Test**: Delete all four agents from the Foundry portal. Trigger the deploy pipeline. Confirm all four agents are re-created and `MCR_*_AGENT_ID` secrets exist in Key Vault. Start the API and confirm it resolves agent IDs from config without errors.

### Tests

- [X] T032 [P] [US3] Write unit tests for `AgentProvisioningService` in `5-Test/tests/MotorcycleRAG.UnitTests/Services/AgentProvisioningServiceTests.cs` — mock `Azure.AI.Projects.AIProjectClient`; test: creates agent when none exists; updates agent when one already exists (upsert by name); returns agent ID after create; returns agent ID after update; throws on missing system prompt

### Implementation

- [X] T033 [P] [US3] Create `AgentDefinitions` static class in `4-Persistence/MotorcycleRAG.Persistence/Azure/AgentDefinitions.cs` — one static readonly string per system prompt (OrchestratorSystemPrompt, VectorSearchSystemPrompt, WebSearchSystemPrompt, PDFSearchSystemPrompt) — content copied verbatim from `specs/develop/contracts/*-system-prompt.md`; one static `ToolDefinition[]` array per agent matching the tool schemas in `specs/develop/data-model.md`
- [X] T034 [US3] Implement `AgentProvisioningService` in `4-Persistence/MotorcycleRAG.Persistence/Azure/AgentProvisioningService.cs` — uses `Azure.AI.Projects.AIProjectClient`; `ProvisionAllAgentsAsync(CancellationToken)` method creates/upserts all four agents using `AgentDefinitions`; returns `ProvisionedAgentIds` record with four agent ID strings; upsert logic: list existing agents by name, update if found, create if not; structured logging per agent provisioned
- [X] T035 [US3] Create `MotorcycleRAG.AgentProvisioning` CLI project in `7-Deployment/AgentProvisioning/MotorcycleRAG.AgentProvisioning.csproj` — .NET 10 console app; references `MotorcycleRAG.Persistence`; entry point reads `AZURE_AI_PROJECT_CONNECTION_STRING` from env, calls `AgentProvisioningService.ProvisionAllAgentsAsync`, writes agent IDs to stdout as JSON (depends on T034)
- [ ] T036 [US3] Add agent provisioning step to `7-Deployment/infrastructure/deploy.yml` (GitHub Actions) — run `dotnet run --project 7-Deployment/AgentProvisioning` after container deploy step; capture stdout JSON agent IDs
- [ ] T037 [US3] Add Key Vault secret write step to `7-Deployment/infrastructure/deploy.yml` — after T036 step: write `MCR_ORCHESTRATOR_AGENT_ID`, `MCR_VECTORSEARCH_AGENT_ID`, `MCR_WEBSEARCH_AGENT_ID`, `MCR_PDFSEARCH_AGENT_ID` to Key Vault using `az keyvault secret set` with the IDs captured in T036; uses pipeline Managed Identity (depends on T036)
- [X] T038 [P] [US3] Update `AzureAIConfigurationValidator` in `1-Presentation/MotorcycleRAG.API/Configuration/AzureAIConfigurationValidator.cs` — add validation for `AzureFoundryOptions.OrchestratorAgentId`, `VectorSearchAgentId`, `WebSearchAgentId`, `PDFSearchAgentId` — fail-fast at startup if any are missing or empty

**Checkpoint**: Delete agents, run pipeline, confirm four agents recreated in Foundry, four Key Vault secrets written, API starts cleanly reading all four agent IDs. US3 independently verified.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Observability hardening, configuration validation, cleanup, and end-to-end verification against quickstart.md.

- [ ] T039 [P] Add correlation between Foundry thread ID and HTTP request correlation ID in `2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs` — log `ThreadId` and `RunId` using `ICorrelationService.CreateLoggingScope` so Foundry run IDs appear in every log line for that request
- [ ] T040 [P] Add structured logging to `OrchestratorToolHandlers` and `SubAgentToolHandlers` in `2-Application/MotorcycleRAG.Application/Agents/Orchestration/` — log tool name, sub-run ID, sub-run state transitions, and result size on every dispatch; use structured placeholders (no string concatenation)
- [ ] T041 [P] Update `DegradedModeTracker` in `2-Application/MotorcycleRAG.Application/Services/Telemetry/DegradedModeTracker.cs` — adapt to new architecture: track which Foundry sub-runs failed (returned error tool output) rather than which `ISearchAgent` instances failed; preserve existing telemetry schema
- [ ] T042 [P] Remove `VectorSearchAgent`, `WebSearchAgent`, `PDFSearchAgent`, `QueryPlannerAgent` classes from `2-Application/MotorcycleRAG.Application/Agents/` — these are replaced by Foundry-hosted agents; their tool execution logic has moved to `SubAgentToolHandlers`; confirm no remaining references before deletion
- [ ] T043 Validate all scenarios in `specs/develop/quickstart.md` — submit test queries, confirm log output matches expected patterns, confirm Foundry portal shows correct run history, confirm trusted source added via Admin UI is picked up on next query

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup       — No dependencies. Start immediately.
Phase 2: Foundational — Depends on Phase 1 (packages must exist). BLOCKS all stories.
Phase 3: US1+US2+US4  — Depends on Phase 2 completion.
Phase 4: US3          — Depends on Phase 2 completion. Can run in parallel with Phase 3.
Phase 5: Polish       — Depends on Phase 3 and Phase 4 both complete.
```

### Within Phase 3

```
T017–T020 (tests)  — Write first, confirm they fail. All parallelisable.
T021, T022         — Parallelisable (different files, different layers).
T023               — Depends on T009 (IFoundryAgentRunner interface).
T024               — Depends on T022, T023.
T025               — Depends on T021, T023, T024.
T026               — Depends on T021, T023, T025.
T027               — Depends on T026.
T028, T029, T030   — Parallelisable DI wiring, depend on T021/T022/T023.
T031               — Depends on T021, T026.
```

### Within Phase 4

```
T032 (tests)       — Write first, confirm it fails.
T033               — Parallelisable with T032.
T034               — Depends on T033.
T035               — Depends on T034.
T036               — Depends on T035.
T037               — Depends on T036.
T038               — Parallelisable.
```

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories. Starts after Phase 2.
- **US2 (P1)**: No dependency on other stories. Starts after Phase 2. Tightly coupled with US1 (same phase).
- **US4 (P2)**: No dependency on US1/US2 beyond shared infrastructure. Starts after Phase 2.
- **US3 (P2)**: No dependency on US1/US2/US4. Starts after Phase 2. Runs in parallel with Phase 3.

---

## Parallel Execution Examples

### Phase 3 Parallel Start (after Phase 2 complete)

```
Stream A (US1 core):      T017 → T021 → T023 → T025 → T026 → T027 → T031
Stream B (US2/US4 core):  T019, T020 → T022 → T024 → T029, T030
Stream C (US3 pipeline):  T032 → T033 → T034 → T035 → T036 → T037
```

### Phase 1 Parallel (after T001–T003 complete)

```
T004, T005, T006, T007, T008 — all touch different files, run in parallel
```

---

## Implementation Strategy

### MVP (US1 + US2 — end-to-end Foundry query)

1. Complete **Phase 1** (packages + DTOs)
2. Complete **Phase 2** (interfaces + removals)
3. Complete **Phase 3** (T021 → T023 → T024 → T025 → T026 → T027 → T028–T030)
4. **STOP and VALIDATE**: Submit a query, see it reach Foundry, get an answer back
5. Confirm US4 (T022, T024, T029) works end-to-end with a real DB-backed trusted source

### Full Delivery

6. Complete **Phase 4** (US3 provisioning pipeline)
7. Complete **Phase 5** (polish + quickstart validation)

---

## Summary

| Phase | Tasks | Stories |
|---|---|---|
| Phase 1: Setup | T001–T008 (8) | — |
| Phase 2: Foundational | T009–T016 (8) | — |
| Phase 3: US1+US2+US4 | T017–T031 (15) | US1, US2, US4 |
| Phase 4: US3 | T032–T038 (7) | US3 |
| Phase 5: Polish | T039–T043 (5) | — |
| **Total** | **43 tasks** | |

**Parallel opportunities**: 22 tasks marked [P]
**MVP scope**: T001–T031 (Phases 1–3) — end-to-end Foundry query working
