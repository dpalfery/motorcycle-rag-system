# Implementation Plan: Motorcycle RAG System Baseline

**Branch**: `001-system-spec` | **Date**: 2025-12-25 | **Spec**: `specs/001-system-spec/spec.md`
**Input**: Feature specification from `specs/001-system-spec/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Deliver a production-ready Motorcycle RAG System baseline with:
- High-confidence answers via agentic verification + structured citations
- Data ingestion pipeline operations (upload/process/status/metrics/cancel/scheduling)
- .NET MAUI admin ingestion app (Windows-first) with local chunking + vectorization
- Admin-managed website scraping/indexing
- MCP server/tool configuration managed in the MAUI admin app (shipped to the API for agent consumption)
- Authentication, user profiles, and plan/SKU enforcement (Free 10 requests/day, Plus 100 requests/day, Pro unlimited)

Tech approach:
- Backend: .NET 10 / C# (Clean Architecture; existing API + Application/Domain/Persistence layers)
- Web UI: React 19 + Vite (existing `1-Presentation/motorcycle-rag-ui/`)
- Desktop Admin UI: .NET MAUI app (Windows-first; supports local models)

Research reference: `specs/001-system-spec/research.md`

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: .NET 10 / C# (server + MAUI admin app), TypeScript + React 19 (web UI)

**Primary Dependencies**:
- Server: ASP.NET Core, Semantic Kernel (agent orchestration), Polly (resilience), Azure SDK wrappers
- Web UI: React 19, Vite, TanStack Query, Tailwind (existing repo)
- Admin app: .NET MAUI (Windows-first)

**Storage**: NEEDS CLARIFICATION
- Existing system uses Azure AI Search for retrieval index.
- User management/plan/usage/tool config/audit need a persistence store (options: SQL, document DB, or Azure-native store). Keep behind interfaces.

**Testing**: xUnit (repo standard), plus integration tests for API contracts; UI tests TBD

**Target Platform**:
- API: containerized on Azure (Container Apps)
- Web UI: browser
- Admin UI: Windows (via .NET MAUI)

**Project Type**: Multi-project solution (Clean Architecture) + React web UI + MAUI admin app

**Performance Goals**: Ensure user-perceived “high confidence” correctness; keep response times reasonable; avoid regressions.

**Constraints**:
- Security: no secrets in code; least privilege; sanitize logs
- Clean Architecture boundaries
- Correctness: verification + citations required
- Plan enforcement: daily request limits per user plan

**Scale/Scope**: MVP for all baseline features (auth, ingestion, web sources, MCP tools, verification/citations)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Security (secrets, authZ, input validation, HTTPS, ProblemDetails): PASS (requirements included; enforce in design)
- Clean Architecture (dependency direction, no EF in DAL): PASS (design will keep stores behind interfaces)
- Code Quality (no placeholders, async I/O, cancellation tokens): PASS (implementation plan expects production-ready code)
- Testing (meaningful coverage): PASS (tests required per feature area)
- Observability (App Insights, correlation IDs, metrics): PASS
- Resilience (Polly, retries, circuit breakers): PASS
- Process & Workflow: PASS

Post-design re-check (after `research.md`, `data-model.md`, `contracts/openapi.yaml`, `quickstart.md`): PASS

Remaining clarifications to resolve during implementation:
- Persistence store for user/plan/usage/tool-audit/web-source registry (must remain behind Domain interfaces; no EF for DAL).
- Persistence + live-update mechanism for MCP server/tool configuration (store choice, versioning, refresh strategy across multiple API instances).
- Exact local embedding model packaging for MAUI admin app (model choice and distribution strategy).

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

Implemented for this feature:

```text
specs/001-system-spec/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── openapi.yaml
└── checklists/
  └── requirements.md
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
MotorcycleRAG.sln

1-Presentation/
  MotorcycleRAG.API/                  # ASP.NET Core API
  MotorcycleRag.WebUI/               # React 19 web app (Vite)
  MotorcycleRag.WebUI.BFF/            # Existing .NET UI project (BFF for web UI)

2-Application/
  MotorcycleRAG.Application/          # Orchestration, agents, pipelines, caching, optimization

3-Domain/
  MotorcycleRAG.Domain/               # Domain models
  MotorcycleRAG.Contracts/            # Interfaces/contracts

4-Persistence/
  MotorcycleRAG.Persistence/          # Azure wrappers, processors, telemetry, resilience

5-Test/tests/                         # Unit/Integration/E2E/Perf/Load

6-Docs/                               # Documentation
7-Deployment/                         # Docker/Pulumi

# Planned addition
1-Presentation/
  MotorcycleRAG.Admin/                # .NET MAUI admin ingestion app (new)
```

**Structure Decision**: Keep Clean Architecture; add a new MAUI admin app under `1-Presentation/` and keep its service interactions via the existing API.

## Phased Plan

### Phase 0 — Outline & Research (COMPLETED)
- Research artifact: `specs/001-system-spec/research.md`
- Resolved key stack conflicts (React 19 vs SvelteKit docs) and captured decisions.

### Phase 1 — Design & Contracts (COMPLETED)
- Data model: `specs/001-system-spec/data-model.md`
- API contract: `specs/001-system-spec/contracts/openapi.yaml`
- Quickstart: `specs/001-system-spec/quickstart.md`

### Phase 1 — Agent Context Update (NEXT)
- Run: `.specify/scripts/powershell/update-agent-context.ps1 -AgentType copilot`

### Phase 1 — Constitution Re-check (NEXT)
- Re-check gates after design artifacts are in place (no violations expected).

### Phase 2 — Implementation Planning (STOP HERE)
- Defer coding tasks to `/speckit.tasks` generation.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
