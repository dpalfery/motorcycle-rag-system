# Research: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

**Date**: 2026-03-16 | **Feature**: Agentic Orchestrator with Dynamic Tool Selection

---

## Resolved Decisions

### 1. Agent Hosting: Azure AI Foundry Agent Service

**Decision**: All agents (Orchestrator, WebSearch, VectorSearch, PDFSearch) are defined and hosted in Azure AI Foundry Agent Service. The LLM reasoning loop runs server-side in Foundry.

**Rationale**: Centralises all LLM work in Foundry — one model management surface, one observability story, no local LLM calls in .NET. Foundry manages thread state, run lifecycle, and model routing. Our .NET code only executes non-LLM I/O.

**Alternatives considered**:
- Local ReAct loop (hand-rolled) — rejected: builds infra Foundry already provides; mixed LLM surfaces
- Local Agent Framework loop — rejected: same concern; workhorses would still need a local LLM path

---

### 2. Client-Side SDK: Microsoft Agent Framework + Azure.AI.Agents.Persistent

**Decision**: Use `Microsoft.Agents.AI.AzureAI.Persistent` (1.0.0-preview) and `Azure.AI.Agents.Persistent` (1.2.0-beta.2) to interact with the Foundry Agent Service. `Azure.AI.Projects` (1.1.0) for project/connection management and agent provisioning.

**Rationale**: These are the Microsoft-blessed SDKs for this exact pattern. `Azure.AI.OpenAI` is removed from the agent orchestration path entirely — no direct chat completions called from our code.

**No `Azure.AI.OpenAI` in the orchestration path**: The package may be retained only if other non-agent features require it (e.g., multimodal processing stub). It is removed from all agent/orchestration code.

---

### 3. Orchestrator Model: `o4-mini` (deployed in Foundry)

**Decision**: The OrchestratorAgent in Foundry is configured with `o4-mini`. It decides which of the three search tool-agents to invoke and when to stop.

**Rationale**: Strong reasoning capability for multi-step tool selection. Deployed as a model in the Azure AI Foundry project — no separate endpoint configuration needed.

---

### 4. Workhorse Model: `Phi-4-mini-flash-reasoning` (deployed in Foundry)

**Decision**: `WebSearchAgent`, `VectorSearchAgent`, and `PDFSearchAgent` in Foundry are each configured with `Phi-4-mini-flash-reasoning`. All LLM reasoning for sub-tasks (query expansion, content assessment, result synthesis) runs in Foundry via this model.

**Rationale**: Low-cost, fast inference for high-frequency sub-tasks. Deployed as a serverless model endpoint in the same Foundry project. Our .NET code never calls this model directly.

---

### 5. Max Iterations: 4 (configured on OrchestratorAgent)

**Decision**: The OrchestratorAgent is configured with `max_completion_tokens` / run options limiting it to 4 tool-call rounds before producing a final answer.

**Rationale**: Prevents runaway cost; sufficient for vector + web + pdf + optional retry. Enforced server-side by Foundry, no custom loop guard needed in .NET.

---

### 6. Agent Provisioning: GitHub Actions Deploy Pipeline (Option A)

**Decision**: Agent definitions (system prompt, model, tool schemas) are provisioned via the GitHub Actions deploy pipeline using `Azure.AI.Projects` SDK calls. Agent IDs returned from provisioning are stored in Azure Key Vault and read at runtime via environment config.

**Rationale**: Reproducible, reviewable, no manual portal steps. Consistent with the working agreement: `pulumi up` is forbidden, `az` writes are forbidden, the pipeline is the only deployment path.

**Agent ID storage**: After provisioning, the pipeline writes agent IDs to Key Vault secrets (`MCR_ORCHESTRATOR_AGENT_ID`, `MCR_WEBSEARCH_AGENT_ID`, etc.). The API reads these at startup via `IConfiguration`.

---

### 7. Tool Architecture: Two-Tier Tool Calls

**Decision**: There are two tiers of tool calls:

**Tier 1 — OrchestratorAgent tools** (implemented by our .NET code):
- `vector_search(query, max_results)` → triggers VectorSearchAgent sub-run in Foundry
- `web_search(query, max_results)` → triggers WebSearchAgent sub-run in Foundry
- `pdf_search(query, max_results)` → triggers PDFSearchAgent sub-run in Foundry

**Tier 2 — Sub-agent tools** (implemented by our .NET code — pure I/O, no LLM):
- VectorSearchAgent tools: `execute_azure_search(query, max_results)` → Azure AI Search query
- WebSearchAgent tools: `get_trusted_sources()` → DB read via `IWebSourceRepository`; `fetch_web_content(url, search_term)` → HTTP scraping; `score_content(content, source_url, trust_tier)` → trust tier multiplier logic
- PDFSearchAgent tools: `search_pdf_index(query, max_results)` → Azure AI Search PDF index query

**Rationale**: All LLM reasoning (query expansion, result assessment, synthesis) happens in Foundry agents. Our .NET code only executes deterministic I/O: database reads, HTTP requests, Azure Search queries. The split is clean — Foundry thinks, .NET fetches.

---

### 8. TrustedSources: Database via `IWebSourceRepository` (unchanged intent, new call site)

**Decision**: `get_trusted_sources()` tool implementation reads from DB via `IWebSourceRepository.GetAllWebSourcesAsync()`, filtered to `IsEnabled = true AND IncludeInSearch = true`. This is called from our .NET tool handler, not from any agent.

**Rationale**: Same as prior design. The call site moves from `WebSearchAgent.SearchAsync` to the `get_trusted_sources` tool handler in the new `FoundryToolDispatcher`.

---

### 9. `IAzureFoundryClient` Disposition

**Decision**: `IAzureFoundryClient` is redesigned. `GetChatCompletionAsync` is removed from the interface. The interface is split:
- `IFoundryAgentRunner` (new, in Contracts) — thread creation, run lifecycle, tool output submission
- `IAzureFoundryClient` retains only `GetEmbeddingsAsync` for the embeddings path (still used by VectorSearchAgent for query embedding before calling Azure AI Search)

**Rationale**: Clear separation. Embeddings use a different endpoint (DeepInfra) and are not part of the agent loop. Agent lifecycle operations are a distinct concern.

---

### 10. `AgentFrameworkAdapter` and `AgentOrchestrator` Redesign

**Decision**: `AgentFrameworkAdapter` is replaced by `FoundryToolDispatcher` — a class that maps Foundry tool call names to local handler methods. `AgentOrchestrator` becomes a Foundry run coordinator: create thread → add message → create run → poll → handle `RequiresAction` → submit outputs → extract answer.

**Rationale**: The existing `AgentFrameworkAdapter` is a hand-rolled dispatcher that the new architecture replaces with a cleaner, Foundry-aware equivalent. The polling/dispatch pattern is well-defined by the `Azure.AI.Agents.Persistent` SDK.

---

### 11. Handling Sub-Agent Runs (Agent-to-Agent)

**Decision**: When the OrchestratorAgent calls `vector_search`, `web_search`, or `pdf_search`, our .NET tool handler starts a **new thread + run** on the corresponding Foundry sub-agent, handles its `RequiresAction` cycles (executing I/O tools), and returns the final message content as the tool output to the orchestrator run.

**Rationale**: This is the standard agent-to-agent composition pattern with Foundry persistent agents. Each sub-run is independent, observable, and uses its own thread. The orchestrator never sees raw search results — it sees the sub-agent's synthesised summary.
