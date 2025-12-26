# Research Notes — 001-system-spec

## Decisions

### 1) Web UI stack: React 19 (chosen)
**Decision**: Use the existing React 19 + Vite UI at `1-Presentation/motorcycle-rag-ui/` as the primary web application.

**Rationale**:
- The repository already contains a React 19 app (`react`/`react-dom` 19.x) and modern tooling.
- The user explicitly requested “C# + React 19”.

**Alternatives considered**:
- SvelteKit + Open WebUI base (documented in `6-Docs/ui-technology-stack.md`).
  - Rejected for this plan due to conflict with current repo implementation and stated direction; treat that document as legacy/out-of-date unless you want to revive it.

### 2) Admin Ingestion UI: .NET MAUI app (chosen)
**Decision**: Implement a dedicated .NET MAUI admin application as a separate project, targeting .NET 10 (Windows-first).

**Rationale**:
- Requirement calls out a Windows admin app.
- MAUI keeps the option open for future cross-platform while still supporting local model execution (chunking/vectorization) and local file system access.

**Alternatives considered**:
- Electron/Tauri desktop wrapper around the web UI.
  - Rejected because the requirement calls for local-model processing and a “Windows app of some sort” but does not require web tech; staying native keeps footprint and deployment simpler.
- WPF / WinUI 3.
  - Rejected because you prefer MAUI and we want one codebase that can grow beyond Windows if needed.

### 3) Local chunking + vectorization in MAUI app
**Decision**: Do chunking and embedding generation locally in the MAUI admin app via an on-device embedding model and ship only chunks+metadata (and optionally vectors) to the API.

**Rationale**:
- Matches the requirement that local processing does not require a cloud-hosted model call.
- Reduces cloud cost and avoids sending raw documents off-machine during preprocessing.

**Alternatives considered**:
- Chunk in-app, vectorize in cloud.
  - Rejected: violates the explicit “local chunking and vectorizing” requirement.

**NEEDS CLARIFICATION resolved (provisional)**:
- Exact local model packaging: use a local embedding model packaged for Windows deployment (e.g., ONNX-based) and keep the model choice configurable.

### 4) “Beyond a shadow of a doubt” correctness
**Decision**: Implement two complementary correctness strategies:
1) Agentic verification: retrieval/claim-generation step + independent verification step before final answer.
2) Structured citations: each claim must have at least one citation with precise location.

**Rationale**:
- Verification reduces hallucination risk.
- Citations provide user-verifiable provenance.

**Alternatives considered**:
- “Single pass RAG with citations only”.
  - Rejected: citations alone don’t guarantee claims match evidence.

### 5) Website scrape + index capability
**Decision**: Maintain an admin-managed allow-list of websites and run scrape/index jobs into the same retrieval corpus with clear attribution.

**Rationale**:
- Curated sources reduce risk and improve consistency.
- Indexing reduces repeated live browsing.

**Alternatives considered**:
- Unrestricted web browsing per query.
  - Rejected: higher risk and less controllable.

### 6) MCP tool configuration in MAUI admin app
**Decision**: Add MCP server/tool configuration management to the MAUI admin app (create/update/enable/disable/validate), plus an audit trail. The MAUI app ships configuration changes to the API layer.

**Rationale**:
- Centralizes operational/admin workflows (ingestion + MCP configuration) in one privileged desktop app.
- Enables safe operational control without redeploy.

**Alternatives considered**:
- Web application UI.
  - Rejected: you prefer MCP configuration to live in the MAUI admin app.
- Config file only.
  - Rejected: operational friction and no UI governance.

**Live update approach (provisional)**:
- Persist MCP configuration in a shared store (DB or Azure App Configuration) with versioning.
- Agents/orchestrator consume the active version via a provider that refreshes on change (poll/version check or pub/sub), applying updates to new runs rather than mutating in-flight runs.

### 7) Auth + user management + SKU plans
**Decision**: Add authentication, user profile, and plan enforcement:
- Free (10 requests/day), Plus (100 requests/day), Pro (unlimited)
- Track usage per user per day; enforce at query entry point.

**Rationale**:
- Required for productization and abuse control.

**Alternatives considered**:
- Anonymous-only.
  - Rejected: cannot support per-user plans/limits.

## Open Items (Implementation choices to finalize during build)
- Identity provider choice (custom auth vs external IdP). Keep contracts flexible.
- Persistence store(s) for users/plans/usage/audit (SQL vs document store). Keep in Application/Domain contracts.
- Exact schema for citations and claim verification results in API response.
