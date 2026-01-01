<!-- Copilot / AI agent instructions for the Motorcycle RAG System -->
# Motorcycle RAG System — AI Coding Assistant Guidance

This file provides instructions for GitHub Copilot and other AI coding agents working on this repository. Follow GitHub's best practices for Copilot coding agent (see [gh.io/copilot-coding-agent-tips](https://gh.io/copilot-coding-agent-tips)).

## 🚀 Quick Start

**Project Type:** .NET 9 multi-agent RAG system on Azure AI Foundry  
**Architecture:** Clean Architecture with 7 layers (1-Presentation → 7-Deployment)  
**Primary Tech:** ASP.NET Core, Semantic Kernel, Azure OpenAI, Azure AI Search, Pulumi IaC

**Essential Commands:**
```bash
dotnet build                           # Build solution
dotnet test                           # Run all tests
dotnet run --project 1-Presentation/MotorcycleRAG.API  # Start API
```

**First Steps:** Read `README.md`, `CLAUDE.md`, and `6-Docs/specs/motorcycle-rag-system/requirements.md` before making changes.

---

## 📋 System Overview

This is an AI-powered multi-agent RAG (Retrieval-Augmented Generation) platform on Azure AI Foundry, using Semantic Kernel for agent orchestration, Azure OpenAI (GPT-4o/GPT-4o-mini/text-embedding-3-large), Azure AI Search for hybrid vector/keyword search, and Azure Document Intelligence for PDF processing. It handles motorcycle data from CSV specs, PDF manuals, and web sources with sequential search (vector → web → PDF fallback).

---

## 🎯 When to Use Copilot on This Repository

**✅ GOOD Tasks for Copilot:**
- Bug fixes in existing features
- Adding unit/integration tests
- Refactoring isolated components (agents, services, processors)
- Updating documentation (README, CLAUDE.md, 6-Docs/)
- UI/API endpoint tweaks
- Performance optimizations with clear scope
- Adding telemetry/logging
- Implementing well-defined user stories from `6-Docs/specs/motorcycle-rag-system/requirements.md`

**❌ AVOID These Tasks:**
- Complex architectural changes spanning multiple layers
- Security-critical authentication/authorization logic (review with human required)
- Ambiguous or poorly-defined requirements
- Changes requiring deep domain knowledge not in repository
- Modifications to Azure infrastructure without clear requirements
- Tasks requiring access to live Azure resources/secrets

**📝 Task Quality:** Ensure issues are well-scoped with:
- Clear problem statement
- Acceptance criteria
- Affected files/components
- Test requirements

---

## 🏗️ Architecture Overview

### Core Facts (browse before changing):
- Solution root: `MotorcycleRAG.sln` (multi-project .NET 9 solution targeting .NET 9.0).
- Presentation layer (API): `1-Presentation/MotorcycleRAG.API/` (minimal APIs in Program.cs, controllers if needed, Azure App Configuration, Telemetry, health checks at `/health`).
- Application/Agents: `2-Application/MotorcycleRAG.Application/` (Semantic Kernel agents: QueryPlannerAgent for intent analysis/subquery planning with GPT-4o; VectorSearchAgent for hybrid search; WebSearchAgent for augmentation; services like AgentOrchestrator for coordination, ModelValidationService, MotorcycleRAGService).
- Domain: `3-Domain/` (entities like MotorcycleSpecification, SearchResult, QueryModels; contracts/interfaces e.g., IMotorcycleRAGService, ISearchAgent, IDataProcessor<T>).
- Persistence & infra: `4-Persistence/` (ADO.NET not applicable; Azure wrappers in Azure/ for OpenAI/Search/Document Intelligence; data processors in DataProcessing/ for CSV/PDF; resilience in Resilience/ with Polly; telemetry in Telemetry/; shared in 4-Persistence/MotorcycleRAG.Shared).
- Tests: `5-Test/tests/` (xUnit unit/integration; mock Azure with interfaces; cover agents, processors, resilience).
- Docs: `6-Docs/` (specs in 6-Docs/specs/motorcycle-rag-system/ for requirements/design/tasks; azure-ai-foundry-setup.md, azure-naming-standards.md, deployment.md).
- Deployment: `7-Deployment/` (Pulumi IaC in infrastructure/, Docker in Dockerfile; GitHub Actions CI/CD).
- **Detailed Rules:** See `8-Agent-instructions/` for comprehensive rules (base-rule.md, security-general-rule.md, code-quality-general-rule.md, testing-general-rule.md, architecture-general.md, process-general-rule.md, scripting-rules.md).

### Initial Checklist (what to do first):
1. Read `README.md` and `CLAUDE.md` for overview/commands; review `6-Docs/specs/motorcycle-rag-system/` (requirements.md for user stories/acceptance criteria; design.md for architecture/models/resilience; tasks.md for implementation status).
2. Inspect `1-Presentation/MotorcycleRAG.API/Program.cs` for startup (DI via ServiceConfiguration.cs extensions: AddAzureAIServices, AddCoreServices, AddSearchAgents, AddDataProcessors; middleware order: HTTPS → CORS → RateLimiting → Auth → endpoints; OpenAPI/Swashbuckle).
3. Review `3-Domain/MotorcycleRAG.Domain/Models/` for entities (e.g., MotorcycleSpecification, SearchResult with relevance scores/sources); `2-Application/MotorcycleRAG.Application/Agents/` for Semantic Kernel implementations.
4. Check Azure integrations in `4-Persistence/Azure/` (wrappers like AzureOpenAIClientWrapper with DefaultAzureCredential/Managed Identity; resilience via ResilienceService with Polly circuit breakers/retries).
5. **Read detailed rules in `8-Agent-instructions/base-rule.md`** for must-follow security, architecture, and quality standards.

---

## 🔧 Development Commands

### Build and Test
```bash
# Build entire solution
dotnet build

# Clean and rebuild
dotnet clean && dotnet restore && dotnet build

# Run all tests (unit + integration)
dotnet test

# Run only unit tests
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests

# Run only integration tests
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests

# Run specific test class
dotnet test --filter "FullyQualifiedName~QueryPlannerAgentTests"

# Run with detailed output
dotnet test --verbosity normal
```

### Development Server
```bash
# Run API in development mode
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Run with specific environment
dotnet run --project 1-Presentation/MotorcycleRAG.API --environment Development
```

### Code Quality
```bash
# Check for warnings (treat warnings as errors in CI)
dotnet build -warnaserror

# Format code (if configured)
dotnet format
```

---

## 📐 Coding Rules & Conventions
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

**🔒 Security (CRITICAL - see `8-Agent-instructions/security-general-rule.md`):**
- **Zero tolerance:** Never commit secrets, API keys, or connection strings
- Use environment variables, Azure Key Vault, or User Secrets
- Parameterize all database queries
- Sanitize/escape all user inputs and logs
- Enforce HTTPS and least-privilege authorization

**🏛️ Architecture (see `8-Agent-instructions/architecture-general.md`):**
- Never place project/code files in repository root
- Respect numbered folder layering (1-Presentation → 7-Deployment)
- Dependencies flow downward only (no circular references)
- Use ADO.NET only (no Entity Framework)
- Schema management via FluentMigrator

**✅ Code Quality (see `8-Agent-instructions/code-quality-general-rule.md`):**
- Fix build errors and warnings immediately
- Treat warnings as errors in CI
- Task is not complete until build compiles with zero warnings
- All required tests must pass or be added

---

## 🧪 Testing & Validation
- Run `dotnet test MotorcycleRAG.sln` (xUnit; target coverage >80%; unit for logic/agents, integration for Azure mocks via interfaces/Moq).
- Add unit tests in `MotorcycleRAG.UnitTests` for agents (mock IAzureOpenAIClient/ISearchClient), processors (sample CSV/PDF), resilience (simulate failures).
- Integration tests in `MotorcycleRAG.IntegrationTests` for end-to-end (TestServer for API; real/mocked Azure; appsettings.Test.json); cover query flow, error scenarios, performance (e.g., <3s response).
- When adding features, include tests for requirements (e.g., sequential search fallback, cost optimization via GPT-4o-mini).
- **See `8-Agent-instructions/testing-general-rule.md` for detailed test requirements and coverage standards.**

**Validation Checklist Before PR:**
1. ✅ Build succeeds with zero warnings: `dotnet build -warnaserror`
2. ✅ All tests pass: `dotnet test`
3. ✅ New/changed code has test coverage (target >80%)
4. ✅ No security vulnerabilities introduced
5. ✅ Documentation updated if behavior changed
6. ✅ Follows architecture and coding conventions

---

## 🔗 Integration Patterns
- Azure services: Use wrappers (AzureOpenAIClientWrapper for GPT/embeddings/chat; AzureSearchClientWrapper for hybrid search/indexing; DocumentIntelligenceClientWrapper for PDF/OCR). All calls through ResilienceService (Polly: Handle HttpRequestException/Timeout; 3 retries, 1min circuit break).
- Agents: Implement ISearchAgent (VectorSearchAgent: hybrid search on motorcycle-index; WebSearchAgent: scrape trusted sources with rate limits; PDF fallback via processed index). Orchestrate via AgentOrchestrator (Semantic Kernel: plan with QueryPlannerAgent, parallel execution, fuse results with semantic ranking).
- Data Flow: Query → IMotorcycleRAGService → AgentOrchestrator → Agents → Azure AI Search/OpenAI → Unified SearchResult[] → Response generation.
- Indexing: MotorcycleIndexingService for batch (100-1000 docs); preserve metadata (make/model/year, page/section for PDFs).
- Config: Centralized in Domain/Models (AzureAIConfiguration, SearchConfiguration, ResilienceConfiguration); bind via GetSection("AzureAI").

---

## 📁 Key File References
- Startup/DI: `1-Presentation/MotorcycleRAG.API/Program.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/ServiceConfiguration.cs`.
- Agents/Services: `2-Application/MotorcycleRAG.Application/Agents/QueryPlannerAgent.cs`, `2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs`.
- Domain: `3-Domain/MotorcycleRAG.Domain/Models/MotorcycleSpecification.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IMotorcycleRAGService.cs`.
- Persistence: `4-Persistence/Azure/AzureOpenAIClientWrapper.cs`, `4-Persistence/DataProcessing/MotorcycleCSVProcessor.cs`, `4-Persistence/Resilience/ResilienceService.cs`.
- Tests: `5-Test/tests/MotorcycleRAG.UnitTests/Agents/VectorSearchAgentTests.cs`.
- Deployment: `7-Deployment/infrastructure/Program.cs` (Pulumi for Container Apps/Environment; outputs endpoint).

---

## ☁️ Azure & Deployment
- Naming: Follow `6-Docs/azure-naming-standards.md` (CAF: <org>-<workload>-<env>-<loc>-<resType>, e.g., mcr-rag-dev-eus2-capp; abbreviate for limits).
- Setup: See `6-Docs/azure-ai-foundry-setup.md` (create AI Foundry project, deploy OpenAI/Search/Document Intelligence; use Managed Identity; test endpoints).
- Deploy: Pulumi in `7-Deployment/infrastructure/` (provision RG/Container Apps/Env; build/push Docker image; GitHub Actions with secrets: AZURE_CLIENT_ID/SECRET, PULUMI_ACCESS_TOKEN, service keys). No secrets in repo; use env vars/App Config/Key Vault.

---

## 🛡️ Safety & Scope Rules
- Maintain layers: No cross-references (e.g., Presentation → Persistence impls); use Domain interfaces.
- No secrets: Expect from Azure App Config/Key Vault/env vars; validate config on startup.
- Resilience: Wrap all Azure calls in Polly; handle rate limits/quota errors with backoff/fallback.
- Naming/Changes: Adhere to azure-naming-standards.md; for infra (Pulumi/Docker), minimal scoped changes with PR rationale.
- Cost: Prefer GPT-4o-mini; cache frequent queries; monitor via App Insights/Cost Management.

---

## 📝 PR & Commit Guidelines
- Small, self-contained changes; add/update tests (unit/integration); run `dotnet build --no-restore`, `dotnet test`, `dotnet run` locally.
- Update README/CLAUDE/docs/specs only for behavior changes; reference requirements.md acceptance criteria.
- For DI/config: Update ServiceConfiguration.cs; validate startup (e.g., options validation).
- Azure changes: Preview with `pulumi preview`; test Managed Identity roles.

**Iteration & Feedback:**
- Respond to PR comments with `@copilot` mentions
- Address reviewer feedback and iterate until approved
- Keep changes focused and minimal
- Document rationale for architectural decisions

---

## 🆘 When Unclear or Blocked

**Ask for clarification on:**
- Specific Azure resource names/subscription for tests.
- CI/CD env details (e.g., App Config/Key Vault setup).
- Target requirement from specs/requirements.md.
- Business logic or domain-specific requirements not documented.

**Escalate to human review:**
- Security-critical changes
- Complex architectural decisions
- Breaking API changes
- Performance-critical optimizations requiring profiling
- Infrastructure changes affecting production

---

## 📚 Additional Resources

- **Detailed Rules:** `8-Agent-instructions/` directory
  - `base-rule.md` - Must-follow global rules
  - `security-general-rule.md` - Security requirements
  - `code-quality-general-rule.md` - Code quality standards
  - `testing-general-rule.md` - Test requirements
  - `architecture-general.md` - Architecture guidelines
  - `process-general-rule.md` - Process and workflow
  - `scripting-rules.md` - Script writing guidelines

- **Documentation:**
  - `README.md` - Project overview and getting started
  - `CLAUDE.md` - Development commands and architecture
  - `6-Docs/specs/motorcycle-rag-system/` - Detailed specifications
  - `6-Docs/azure-ai-foundry-setup.md` - Azure setup guide
  - `6-Docs/azure-naming-standards.md` - Naming conventions
  - `6-Docs/deployment.md` - Deployment procedures

---

**Note:** This guidance follows [GitHub Copilot coding agent best practices](https://gh.io/copilot-coding-agent-tips). For questions about Copilot usage patterns, refer to the official GitHub documentation.

End of file.
