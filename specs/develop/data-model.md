# Data Model: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

**Date**: 2026-03-16

---

## No New Database Tables

This feature introduces no new SQL tables. The existing `WebSources` table is unchanged.

---

## New DTOs (Contracts.Models)

### `AgentRunStatus`

```csharp
// 3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentRunStatus.cs
public record AgentRunStatus(
    string RunId,
    AgentRunState State,
    IReadOnlyList<AgentToolCall>? RequiredToolCalls);
```

### `AgentToolCall`

```csharp
// 3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentToolCall.cs
public record AgentToolCall(
    string CallId,
    string FunctionName,
    string ArgumentsJson);
```

### `AgentToolOutput`

```csharp
// 3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentToolOutput.cs
public record AgentToolOutput(
    string CallId,
    string Output);  // JSON-serialised result string
```

### `AgentRunState` (enum)

```csharp
// 3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AgentRunState.cs
public enum AgentRunState
{
    Queued,
    InProgress,
    RequiresAction,
    Completed,
    Failed,
    Cancelled,
    Expired
}
```

---

## Configuration Keys (AzureFoundryOptions)

Four new string properties added to `AzureFoundryOptions`. Values come from Key Vault via environment config — never hardcoded.

| Property | Environment Variable | Written by | Read by |
|---|---|---|---|
| `OrchestratorAgentId` | `MCR_ORCHESTRATOR_AGENT_ID` | Deploy pipeline → Key Vault | `AgentOrchestrator` |
| `VectorSearchAgentId` | `MCR_VECTORSEARCH_AGENT_ID` | Deploy pipeline → Key Vault | `OrchestratorToolHandlers` |
| `WebSearchAgentId` | `MCR_WEBSEARCH_AGENT_ID` | Deploy pipeline → Key Vault | `OrchestratorToolHandlers` |
| `PDFSearchAgentId` | `MCR_PDFSEARCH_AGENT_ID` | Deploy pipeline → Key Vault | `OrchestratorToolHandlers` |

---

## Agent Definitions (provisioned to Foundry — not persisted in our DB)

### OrchestratorAgent

| Field | Value |
|---|---|
| Model | `o4-mini` |
| Max rounds | 4 |
| Tools | `vector_search`, `web_search`, `pdf_search` |
| System prompt | See `contracts/orchestrator-system-prompt.md` |

### WebSearchAgent

| Field | Value |
|---|---|
| Model | `Phi-4-mini-flash-reasoning` |
| Tools | `get_trusted_sources`, `fetch_web_content`, `score_content` |
| System prompt | See `contracts/websearch-system-prompt.md` |

### VectorSearchAgent

| Field | Value |
|---|---|
| Model | `Phi-4-mini-flash-reasoning` |
| Tools | `execute_azure_search` |
| System prompt | See `contracts/vectorsearch-system-prompt.md` |

### PDFSearchAgent

| Field | Value |
|---|---|
| Model | `Phi-4-mini-flash-reasoning` |
| Tools | `search_pdf_index` |
| System prompt | See `contracts/pdfsearch-system-prompt.md` |

---

## Tool Schema Reference

### OrchestratorAgent tools

```json
{ "name": "vector_search",
  "parameters": { "query": "string (required)", "max_results": "integer (default 10)" } }

{ "name": "web_search",
  "parameters": { "query": "string (required)", "max_results": "integer (default 5)" } }

{ "name": "pdf_search",
  "parameters": { "query": "string (required)", "max_results": "integer (default 5)" } }
```

### WebSearchAgent tools

```json
{ "name": "get_trusted_sources",
  "parameters": {} }

{ "name": "fetch_web_content",
  "parameters": { "url": "string (required)", "search_term": "string (required)" } }

{ "name": "score_content",
  "parameters": { "content": "string (required)", "source_url": "string (required)", "trust_tier": "integer (required, 1-5)" } }
```

### VectorSearchAgent tools

```json
{ "name": "execute_azure_search",
  "parameters": { "query": "string (required)", "max_results": "integer (default 10)" } }
```

### PDFSearchAgent tools

```json
{ "name": "search_pdf_index",
  "parameters": { "query": "string (required)", "max_results": "integer (default 5)" } }
```

---

## Mapping: `WebSource` (DB) → `TrustedSourceOptions` (returned by `get_trusted_sources` tool)

| `WebSource` DB field | → | `TrustedSourceOptions` field | Notes |
|---|---|---|---|
| `Name` | → | `Name` | Direct |
| `Url` | → | `BaseUrl` | Direct |
| `Url` | → | `SearchUrlTemplate` | `"{Url}/search?q={query}"` default |
| `TrustTier` | → | `CredibilityScore` | 1→0.95, 2→0.80, 3→0.65, 4→0.50, 5→0.35 |
| — | → | `ContentSelector` | Default `"//p\|//article\|//div[@class='content']"` |

Filter: `IsEnabled = true AND IncludeInSearch = true`
