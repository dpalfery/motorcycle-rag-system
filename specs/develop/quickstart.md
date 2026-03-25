# Quickstart: LLM-Driven Agentic Orchestrator via Azure AI Foundry Agent Service

## Prerequisites

- Azure AI Foundry project provisioned (hub + project resource)
- `o4-mini` and `Phi-4-mini-flash-reasoning` deployed as model endpoints in the Foundry project
- Azure Key Vault accessible to the API (Managed Identity)

## What Changed

All agent reasoning now runs in Azure AI Foundry Agent Service. Our .NET code creates threads,
starts runs, and handles `RequiresAction` states by executing I/O-only tool handlers.
No LLM calls are made from .NET code.

## Environment Variables Required

```
# Azure AI Foundry
AZURE_FOUNDRY_ENDPOINT=https://<your-project>.openai.azure.com/
AZURE_AI_PROJECT_CONNECTION_STRING=<from Foundry portal — Overview > Connection string>

# Agent IDs (written by deploy pipeline, read from Key Vault)
MCR_ORCHESTRATOR_AGENT_ID=asst_<id>
MCR_VECTORSEARCH_AGENT_ID=asst_<id>
MCR_WEBSEARCH_AGENT_ID=asst_<id>
MCR_PDFSEARCH_AGENT_ID=asst_<id>
```

> Agent IDs are provisioned by the deploy pipeline and written to Key Vault automatically.
> You do not set these manually in production.

## First-Time Agent Provisioning (local dev only)

In production, agents are provisioned by the GitHub Actions pipeline. For local dev:

```bash
dotnet run --project 7-Deployment/AgentProvisioning/MotorcycleRAG.AgentProvisioning
```

This creates/updates all four agents in your Foundry project and prints the agent IDs.
Add them to your local `appsettings.Development.json` (values only — no secrets in config files).

## Seeding Trusted Sources

WebSearchAgent returns nothing until trusted sources exist in the DB. Add at least one:

```
POST /api/admin/web-sources
{
  "name": "Cycle World",
  "url": "https://www.cycleworld.com",
  "trustTier": 2,
  "isEnabled": true,
  "includeInSearch": true
}
```

## Observability

Each query produces structured logs with:
```
[Orchestrator] Thread created: thread_abc123
[Orchestrator] Run created: run_xyz789 on agent: asst_orchestrator
[Orchestrator] RequiresAction — tool: vector_search, callId: call_001
[VectorSearch] Sub-run created: run_aaa111 on agent: asst_vectorsearch
[VectorSearch] RequiresAction — tool: execute_azure_search
[VectorSearch] Tool output submitted — 8 results returned
[VectorSearch] Run completed
[Orchestrator] Tool output submitted for call_001
[Orchestrator] Run completed — extracting answer
```

Check the Azure AI Foundry portal > Threads to see full run history for all agents.

## Running Tests

```bash
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests
```

Integration tests require `AZURE_AI_PROJECT_CONNECTION_STRING` set locally and will create
real Foundry threads/runs (tagged for cleanup).
