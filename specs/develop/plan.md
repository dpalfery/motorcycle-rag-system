# Implementation Plan: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

**Branch**: `develop` | **Date**: 2026-03-16 | **Spec**: `specs/develop/spec.md`

---

## Summary

Replace the hard-coded sequential retrieval pipeline with four agents hosted in Azure AI Foundry Agent Service. An OrchestratorAgent (`o4-mini`) decides dynamically which search sub-agents to invoke. Three sub-agents (VectorSearch, WebSearch, PDFSearch — all `Phi-4-mini-flash-reasoning`) run in Foundry and call back to our .NET code for I/O-only tool execution (Azure Search queries, HTTP scraping, DB reads). No LLM calls are made from our .NET code. All agents are provisioned via the GitHub Actions deploy pipeline. Agent IDs are stored in Key Vault and read at runtime.

---

## Technical Context

**Language/Version**: .NET 10 / C# 13
**Primary Dependencies**: `Azure.AI.Agents.Persistent` 1.2.0-beta.2, `Microsoft.Agents.AI.AzureAI.Persistent` 1.0.0-preview, `Azure.AI.Projects` 1.1.0, `Azure.Identity` (existing)
**Removed from agent path**: `Azure.AI.OpenAI` chat completions — no `GetChatCompletionAsync` in orchestration
**Storage**: SQL Server — `WebSources` table (existing); Azure AI Search (existing); Azure Key Vault for agent IDs
**Testing**: xUnit, Moq — existing test projects
**Target Platform**: ASP.NET Core 10, GitHub Actions CI/CD
**Project Type**: Multi-project Clean Architecture solution
**Constraints**: All LLM calls server-side in Foundry; .NET tool handlers are pure I/O; 4 max run rounds (Foundry-enforced); agent provisioning is pipeline-only

---

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| Security (I) | PASS | Agent IDs from Key Vault. Managed Identity for Foundry auth. Tool inputs validated before I/O. No hardcoded secrets or model names. |
| Clean Architecture (II) | PASS | `IFoundryAgentRunner` in Contracts. `FoundryAgentRunner` + `AgentProvisioningService` in Persistence. `FoundryToolDispatcher` + `AgentOrchestrator` in Application. No Foundry SDK types in Domain. |
| Code Quality (III) | PASS | Sequential policy removed. `GetChatCompletionAsync` removed from agent path. One class per file. Full async. |
| Testing (IV) | PASS | Unit tests for dispatcher, orchestrator run coordination, tool handlers. Integration test for provisioning + end-to-end run. |
| Observability (V) | PASS | Structured logs with Foundry run ID, thread ID, tool name, iteration. Thread ID correlated to request correlation ID. |
| Resilience (VI) | PASS | Max rounds enforced by Foundry. Tool exceptions become error tool outputs (no crash). DB failure on trusted sources degrades gracefully. |
| Process (VII) | PASS | New NuGet packages listed and approved below. No manual infra. Provisioning via pipeline only. |

### Approved New NuGet Packages

| Package | Version | Project | Justification |
|---------|---------|---------|---------------|
| `Azure.AI.Agents.Persistent` | 1.2.0-beta.2 | Persistence | Foundry Agent Service SDK — thread/run/tool output lifecycle |
| `Microsoft.Agents.AI.AzureAI.Persistent` | 1.0.0-preview | Application | Agent Framework wrapper for Foundry persistent agents |
| `Azure.AI.Projects` | 1.1.0 | Persistence | Agent definition provisioning (create/update agents) |

---

## Project Structure

### Documentation (this feature)

```text
specs/develop/
├── plan.md              ← this file
├── research.md          ← complete
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/           ← Phase 1 output
└── tasks.md             ← /speckit.tasks output
```

### Source Code — Affected Files

```text
# NEW — Contracts (3-Domain)
3-Domain/MotorcycleRAG.Contracts/Interfaces/
├── IFoundryAgentRunner.cs          ← thread + run lifecycle abstraction
└── ITrustedSourcesLoader.cs        ← load trusted sources from DB

# NEW — Application (2-Application)
2-Application/MotorcycleRAG.Application/
├── Agents/Orchestration/
│   ├── FoundryToolDispatcher.cs    ← maps tool names → async handler delegates
│   ├── OrchestratorToolHandlers.cs ← handles vector_search, web_search, pdf_search (starts sub-runs)
│   └── SubAgentToolHandlers.cs     ← handles execute_azure_search, fetch_web_content, get_trusted_sources, etc.
├── Services/TrustedSources/
│   └── DatabaseTrustedSourcesLoader.cs  ← IWebSourceRepository → TrustedSourceOptions[]
└── Services/
    └── AgentOrchestrator.cs        ← REWRITTEN: Foundry run coordinator (replaces sequential policy)

# NEW — Persistence (4-Persistence)
4-Persistence/MotorcycleRAG.Persistence/Azure/
├── FoundryAgentRunner.cs           ← IFoundryAgentRunner impl via Azure.AI.Agents.Persistent
└── AgentProvisioningService.cs     ← creates/upserts agent definitions via Azure.AI.Projects

# MODIFIED — Contracts
3-Domain/MotorcycleRAG.Contracts/Interfaces/
└── IAzureFoundryClient.cs          ← REMOVE GetChatCompletionAsync; retain GetEmbeddingsAsync only

# MODIFIED — Persistence
4-Persistence/MotorcycleRAG.Persistence/Azure/
└── AzureFoundryClientWrapper.cs    ← REMOVE GetChatCompletionAsync implementation

# MODIFIED — Application
2-Application/MotorcycleRAG.Application/
├── Agents/
│   ├── AgentFrameworkAdapter.cs    ← REMOVED (replaced by FoundryToolDispatcher)
│   └── ToolDefinitions.cs          ← REMOVED (tool schemas now live in Foundry agent definitions)
└── Services/
    └── MotorcycleRAGService.cs     ← MINOR: GenerateResponseAsync removed from hot path

# MODIFIED — API Configuration
1-Presentation/MotorcycleRAG.API/Configuration/Services/
├── SearchAgentsConfiguration.cs   ← register FoundryToolDispatcher, DatabaseTrustedSourcesLoader
└── AzureAIServiceConfiguration.cs ← register IFoundryAgentRunner → FoundryAgentRunner

# MODIFIED — Options
0-Base/MotorcycleRAG.Core/Options/
└── AzureFoundryOptions.cs          ← add OrchestratorAgentId, VectorSearchAgentId,
                                       WebSearchAgentId, PDFSearchAgentId (read from env/Key Vault)

# NEW — Deploy bootstrap (called from GitHub Actions)
7-Deployment/ (or a new CLI project)
└── AgentProvisioning/
    └── FoundryAgentProvisioningRunner.cs  ← CLI entry point: reads agent specs, calls AgentProvisioningService

# NEW — Tests
5-Test/tests/MotorcycleRAG.UnitTests/
├── Agents/FoundryToolDispatcherTests.cs
├── Agents/AgentOrchestratorRunTests.cs
└── Services/TrustedSources/DatabaseTrustedSourcesLoaderTests.cs

5-Test/tests/MotorcycleRAG.IntegrationTests/
└── Agents/FoundryAgentRunnerIntegrationTests.cs
```

---

## Phase 0: Research — Complete

See `specs/develop/research.md`. All decisions resolved.

---

## Phase 1: Design & Contracts

See `specs/develop/data-model.md` and `specs/develop/contracts/`.

### Agent Architecture

```text
User query
    ↓
MotorcycleRAGService.QueryAsync()
    ↓
AgentOrchestrator.ExecuteAsync()
    ↓ CreateThread + CreateRun(OrchestratorAgentId)
Azure AI Foundry — OrchestratorAgent (o4-mini)
    ↓ RequiresAction: vector_search | web_search | pdf_search
FoundryToolDispatcher.DispatchAsync(toolCall)
    ├── vector_search  → OrchestratorToolHandlers.HandleVectorSearchAsync()
    │       ↓ CreateThread + CreateRun(VectorSearchAgentId)
    │   Azure AI Foundry — VectorSearchAgent (Phi-4)
    │       ↓ RequiresAction: execute_azure_search
    │   SubAgentToolHandlers.HandleExecuteAzureSearchAsync()
    │       ↓ Azure AI Search query (existing AzureSearchClientWrapper)
    │   ← returns search results as tool output
    │   VectorSearchAgent synthesises → returns message
    │   ← returns synthesised result as tool output to Orchestrator
    │
    ├── web_search     → OrchestratorToolHandlers.HandleWebSearchAsync()
    │       ↓ CreateThread + CreateRun(WebSearchAgentId)
    │   Azure AI Foundry — WebSearchAgent (Phi-4)
    │       ↓ RequiresAction: get_trusted_sources
    │   SubAgentToolHandlers.HandleGetTrustedSourcesAsync()
    │       ↓ IWebSourceRepository (DB read)
    │       ↓ RequiresAction: fetch_web_content(url, term)
    │   SubAgentToolHandlers.HandleFetchWebContentAsync()
    │       ↓ HTTP scraping (existing WebContentExtractor)
    │       ↓ RequiresAction: score_content(content, trust_tier)
    │   SubAgentToolHandlers.HandleScoreContentAsync()
    │       ↓ trust tier multiplier (no LLM)
    │   WebSearchAgent synthesises → returns message
    │   ← returns synthesised result as tool output to Orchestrator
    │
    └── pdf_search     → OrchestratorToolHandlers.HandlePDFSearchAsync()
            ↓ CreateThread + CreateRun(PDFSearchAgentId)
        Azure AI Foundry — PDFSearchAgent (Phi-4)
            ↓ RequiresAction: search_pdf_index
        SubAgentToolHandlers.HandleSearchPdfIndexAsync()
            ↓ Azure AI Search PDF index query
        PDFSearchAgent synthesises → returns message
        ← returns synthesised result as tool output to Orchestrator

OrchestratorAgent synthesises all tool results → final answer
AgentOrchestrator extracts answer from thread messages
    ↓
MotorcycleRAGService returns MotorcycleQueryResponse
```

### New Interface: `IFoundryAgentRunner`

```csharp
// 3-Domain/MotorcycleRAG.Contracts/Interfaces/IFoundryAgentRunner.cs
public interface IFoundryAgentRunner
{
    Task<string> CreateThreadAsync(CancellationToken ct = default);
    Task AddUserMessageAsync(string threadId, string content, CancellationToken ct = default);
    Task<AgentRunStatus> CreateRunAsync(string threadId, string agentId, CancellationToken ct = default);
    Task<AgentRunStatus> GetRunStatusAsync(string threadId, string runId, CancellationToken ct = default);
    Task<AgentRunStatus> SubmitToolOutputsAsync(string threadId, string runId, IEnumerable<AgentToolOutput> outputs, CancellationToken ct = default);
    Task<string> GetLastAssistantMessageAsync(string threadId, CancellationToken ct = default);
    Task DeleteThreadAsync(string threadId, CancellationToken ct = default);
}

// Supporting DTOs (in Contracts.Models)
public record AgentRunStatus(string RunId, AgentRunState State, IReadOnlyList<AgentToolCall>? RequiredToolCalls);
public record AgentToolCall(string CallId, string FunctionName, string ArgumentsJson);
public record AgentToolOutput(string CallId, string Output);
public enum AgentRunState { Queued, InProgress, RequiresAction, Completed, Failed, Cancelled, Expired }
```

### `AgentProvisioningService` — Agent Definition Shape

Each agent definition provisioned to Foundry contains:

```text
OrchestratorAgent:
  model: o4-mini
  instructions: [OrchestratorSystemPrompt — see contracts/orchestrator-system-prompt.md]
  tools:
    - vector_search(query: string, max_results: int)
    - web_search(query: string, max_results: int)
    - pdf_search(query: string, max_results: int)

WebSearchAgent:
  model: Phi-4-mini-flash-reasoning
  instructions: [WebSearch system prompt]
  tools:
    - get_trusted_sources()
    - fetch_web_content(url: string, search_term: string)
    - score_content(content: string, source_url: string, trust_tier: int)

VectorSearchAgent:
  model: Phi-4-mini-flash-reasoning
  instructions: [VectorSearch system prompt]
  tools:
    - execute_azure_search(query: string, max_results: int)

PDFSearchAgent:
  model: Phi-4-mini-flash-reasoning
  instructions: [PDFSearch system prompt]
  tools:
    - search_pdf_index(query: string, max_results: int)
```

---

## Complexity Tracking

No constitution violations.

| Change | Why |
|--------|-----|
| Remove `GetChatCompletionAsync` from agent path | No direct LLM calls from .NET; Foundry owns all inference |
| Remove `AgentFrameworkAdapter` + `ToolDefinitions` | Replaced by `FoundryToolDispatcher`; tool schemas live in Foundry agent definitions |
| Remove `ExecuteSequentialRetrievalPolicyAsync` | Replaced entirely by Foundry run coordination |
| New `IFoundryAgentRunner` interface | Dependency Rule: Application must not reference Persistence/Foundry SDK directly |
| Agent provisioning in deploy pipeline | No-manual-infra working agreement; reproducible environments |
