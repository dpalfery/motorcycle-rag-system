<!-- Copilot / AI agent instructions for the Motorcycle RAG System -->
# Motorcycle RAG System — AI Coding Assistant Guidance

This file contains concise, actionable guidance for automated coding agents (Copilot-style) working in this repository. Keep edits focused, minimal, and consistent with existing patterns in `README.md`, `CLAUDE.md`, and `6-Docs/` specifications. The system is an AI-powered multi-agent RAG (Retrieval-Augmented Generation) platform on Azure AI Foundry, using Semantic Kernel for agent orchestration, Azure OpenAI (GPT-4o/GPT-4o-mini/text-embedding-3-large), Azure AI Search for hybrid vector/keyword search, and Azure Document Intelligence for PDF processing. It handles motorcycle data from CSV specs, PDF manuals, and web sources with sequential search (vector → web → PDF fallback).

Core facts (browse before changing):
- Solution root: `MotorcycleRAG.sln` (multi-project .NET 9 solution targeting .NET 9.0).
- Presentation layer (API): `1-Presentation/MotorcycleRAG.API/` (minimal APIs in Program.cs, controllers if needed, Azure App Configuration, Telemetry, health checks at `/health`).
- Application/Agents: `2-Application/MotorcycleRAG.Application/` (Semantic Kernel agents: QueryPlannerAgent for intent analysis/subquery planning with GPT-4o; VectorSearchAgent for hybrid search; WebSearchAgent for augmentation; services like AgentOrchestrator for coordination, ModelValidationService, MotorcycleRAGService).
- Domain: `3-Domain/` (entities like MotorcycleSpecification, SearchResult, QueryModels; contracts/interfaces e.g., IMotorcycleRAGService, ISearchAgent, IDataProcessor<T>).
- Persistence & infra: `4-Persistence/` (ADO.NET not applicable; Azure wrappers in Azure/ for OpenAI/Search/Document Intelligence; data processors in DataProcessing/ for CSV/PDF; resilience in Resilience/ with Polly; telemetry in Telemetry/; shared in 4-Persistence/MotorcycleRAG.Shared).
- Tests: `5-Test/tests/` (xUnit unit/integration; mock Azure with interfaces; cover agents, processors, resilience).
- Docs: `6-Docs/` (specs in 6-Docs/specs/motorcycle-rag-system/ for requirements/design/tasks; azure-ai-foundry-setup.md, azure-naming-standards.md, deployment.md).
- Deployment: `7-Deployment/` (Pulumi IaC in infrastructure/, Docker in Dockerfile; GitHub Actions CI/CD).

What to do first (quick checklist):
1. Read `README.md` and `CLAUDE.md` for overview/commands; review `6-Docs/specs/motorcycle-rag-system/` (requirements.md for user stories/acceptance criteria; design.md for architecture/models/resilience; tasks.md for implementation status).
2. Inspect `1-Presentation/MotorcycleRAG.API/Program.cs` for startup (DI via ServiceConfiguration.cs extensions: AddAzureAIServices, AddCoreServices, AddSearchAgents, AddDataProcessors; middleware order: HTTPS → CORS → RateLimiting → Auth → endpoints; OpenAPI/Swashbuckle).
3. Review `3-Domain/MotorcycleRAG.Domain/Models/` for entities (e.g., MotorcycleSpecification, SearchResult with relevance scores/sources); `2-Application/MotorcycleRAG.Application/Agents/` for Semantic Kernel implementations.
4. Check Azure integrations in `4-Persistence/Azure/` (wrappers like AzureOpenAIClientWrapper with DefaultAzureCredential/Managed Identity; resilience via ResilienceService with Polly circuit breakers/retries).

Concrete coding rules & conventions (project-specific):
- Clean Architecture enforced: Layers separate (Presentation refs Application/Domain contracts only; no direct Persistence). Use dependency injection with IServiceCollection extensions (e.g., AddAzureAIServices for OpenAI/Search/Document Intelligence with IHttpClientFactory, async/await, CancellationToken).
- .NET 9 / C# 12: Minimal APIs by default (controllers for filters/conventions); async I/O everywhere; Options pattern for config (IOptions<AzureAIConfiguration>); strongly-typed from appsettings.{Environment}.json or Azure App Configuration/Key Vault (no hardcoding secrets).
- Semantic Kernel: Use for agent orchestration (KernelBuilder; plugins for search agents); GPT-4o for planning, GPT-4o-mini for completion (cost-optimized), text-embedding-3-large for vectors.
- Azure integrations: Wrap SDKs in interfaces (e.g., IAzureOpenAIClient); use DefaultAzureCredential/Managed Identity; endpoints from config (e.g., AzureAI:OpenAIEndpoint, SearchServiceEndpoint).
- Resilience: Polly for all external calls (retries with exponential backoff, circuit breakers for OpenAI/Search; timeouts); graceful degradation (fallback to cached/partial results).
- Validation/Errors: FluentValidation for inputs; return ProblemDetails (RFC 7807); structured logging with ILogger<T> + correlation IDs via CorrelationService.
- Observability: Application Insights (TelemetryService; track queries/metrics/errors with custom events); health checks (AddHealthChecks for DB/services; /health endpoint).
- Data Processing: Implement IDataProcessor<T> for CSV (row-chunking, header detection) and PDF (Document Intelligence Layout model, semantic chunking, multimodal with GPT-4 Vision); index to Azure AI Search (hybrid vector/keyword, metadata with sources/pages).
- Performance: Async/await; IHttpClientFactory reuse; caching for queries/embeddings (e.g., Redis if scaled); rate limiting per IP/user.
- Security: HTTPS/HSTS enforced; CORS for clients; authZ via policies (least-privilege); validate all inputs; no secrets in code (User Secrets/Key Vault).

Tests and validation:
- Run `dotnet test MotorcycleRAG.sln` (xUnit; target coverage >80%; unit for logic/agents, integration for Azure mocks via interfaces/Moq).
- Add unit tests in `MotorcycleRAG.UnitTests` for agents (mock IAzureOpenAIClient/ISearchClient), processors (sample CSV/PDF), resilience (simulate failures).
- Integration tests in `MotorcycleRAG.IntegrationTests` for end-to-end (TestServer for API; real/mocked Azure; appsettings.Test.json); cover query flow, error scenarios, performance (e.g., <3s response).
- When adding features, include tests for requirements (e.g., sequential search fallback, cost optimization via GPT-4o-mini).

Integration points and patterns to follow:
- Azure services: Use wrappers (AzureOpenAIClientWrapper for GPT/embeddings/chat; AzureSearchClientWrapper for hybrid search/indexing; DocumentIntelligenceClientWrapper for PDF/OCR). All calls through ResilienceService (Polly: Handle HttpRequestException/Timeout; 3 retries, 1min circuit break).
- Agents: Implement ISearchAgent (VectorSearchAgent: hybrid search on motorcycle-index; WebSearchAgent: scrape trusted sources with rate limits; PDF fallback via processed index). Orchestrate via AgentOrchestrator (Semantic Kernel: plan with QueryPlannerAgent, parallel execution, fuse results with semantic ranking).
- Data Flow: Query → IMotorcycleRAGService → AgentOrchestrator → Agents → Azure AI Search/OpenAI → Unified SearchResult[] → Response generation.
- Indexing: MotorcycleIndexingService for batch (100-1000 docs); preserve metadata (make/model/year, page/section for PDFs).
- Config: Centralized in Domain/Models (AzureAIConfiguration, SearchConfiguration, ResilienceConfiguration); bind via GetSection("AzureAI").

Common file examples to reference:
- Startup/DI: `1-Presentation/MotorcycleRAG.API/Program.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/ServiceConfiguration.cs`.
- Agents/Services: `2-Application/MotorcycleRAG.Application/Agents/QueryPlannerAgent.cs`, `2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs`.
- Domain: `3-Domain/MotorcycleRAG.Domain/Models/MotorcycleSpecification.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IMotorcycleRAGService.cs`.
- Persistence: `4-Persistence/Azure/AzureOpenAIClientWrapper.cs`, `4-Persistence/DataProcessing/MotorcycleCSVProcessor.cs`, `4-Persistence/Resilience/ResilienceService.cs`.
- Tests: `5-Test/tests/MotorcycleRAG.UnitTests/Agents/VectorSearchAgentTests.cs`.
- Deployment: `7-Deployment/infrastructure/Program.cs` (Pulumi for Container Apps/Environment; outputs endpoint).

Azure/Deployment Guidance:
- Naming: Follow `6-Docs/azure-naming-standards.md` (CAF: <org>-<workload>-<env>-<loc>-<resType>, e.g., mcr-rag-dev-eus2-capp; abbreviate for limits).
- Setup: See `6-Docs/azure-ai-foundry-setup.md` (create AI Foundry project, deploy OpenAI/Search/Document Intelligence; use Managed Identity; test endpoints).
- Deploy: Pulumi in `7-Deployment/infrastructure/` (provision RG/Container Apps/Env; build/push Docker image; GitHub Actions with secrets: AZURE_CLIENT_ID/SECRET, PULUMI_ACCESS_TOKEN, service keys). No secrets in repo; use env vars/App Config/Key Vault.

Safety and scope rules for agents:
- Maintain layers: No cross-references (e.g., Presentation → Persistence impls); use Domain interfaces.
- No secrets: Expect from Azure App Config/Key Vault/env vars; validate config on startup.
- Resilience: Wrap all Azure calls in Polly; handle rate limits/quota errors with backoff/fallback.
- Naming/Changes: Adhere to azure-naming-standards.md; for infra (Pulumi/Docker), minimal scoped changes with PR rationale.
- Cost: Prefer GPT-4o-mini; cache frequent queries; monitor via App Insights/Cost Management.

PR & commit guidance for automated edits:
- Small, self-contained changes; add/update tests (unit/integration); run `dotnet build --no-restore`, `dotnet test`, `dotnet run` locally.
- Update README/CLAUDE/docs/specs only for behavior changes; reference requirements.md acceptance criteria.
- For DI/config: Update ServiceConfiguration.cs; validate startup (e.g., options validation).
- Azure changes: Preview with `pulumi preview`; test Managed Identity roles.

If unclear, ask for:
- Specific Azure resource names/subscription for tests.
- CI/CD env details (e.g., App Config/Key Vault setup).
- Target requirement from specs/requirements.md.

End of file.
