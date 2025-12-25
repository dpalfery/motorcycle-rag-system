<!--
================================================================================
SYNC IMPACT REPORT
================================================================================
Version change: N/A → 1.0.0 (initial ratification)

Modified principles: N/A (initial version)

Added sections:
  - Core Principles (7 principles)
  - Additional Constraints (Technology Stack, Azure Services, Cost Optimization)
  - Development Workflow (Code Review, Task Management)
  - Governance

Removed sections: N/A (initial version)

Templates requiring updates:
  ✅ .specify/templates/plan-template.md - Constitution Check section compatible
  ✅ .specify/templates/spec-template.md - Requirements align with principles
  ✅ .specify/templates/tasks-template.md - Task organization supports principles

Follow-up TODOs: None
================================================================================
-->

# Motorcycle RAG System Constitution

## Core Principles

### I. Security (NON-NEGOTIABLE)

Security is the foundational principle. All other work MUST comply with security requirements.

- **Secrets Management**: NEVER hardcode secrets, passwords, tokens, connection strings, or API keys in source code or configuration files. Retrieve secrets from:
  - Local development: Environment variables via `Environment.GetEnvironmentVariable()`
  - Production: Azure Key Vault + Azure App Configuration
- **Input Validation**: Validate ALL user inputs on both client and server. Use parameterized queries ONLY for any SQL operations. Sanitize inputs before logging (replace newlines, use structured logging with placeholders).
- **Secure Communication**: Enforce HTTPS/TLS 1.2+ for all communications. Set HSTS headers. Redirect HTTP to HTTPS.
- **Authorization**: Apply principle of least privilege. Default to NO access; permissions explicitly granted. Authorize EVERY action after authentication.
- **Compliance Marker**: All task outputs MUST include `[Security Rule: Active]` if security rules are loaded, or `[Security Rule: Missing]` to HALT work.

**Rationale**: Security breaches cause irreversible damage. It is better for the application to fail than for a secret to be exposed.

### II. Clean Architecture

Strict separation of concerns across numbered layers. Dependencies flow DOWNWARD only.

- **Layer Structure**:
  - `0-Base/`: Cross-cutting concerns (if needed)
  - `1-Presentation/`: API entry points, controllers, minimal APIs, UI
  - `2-Application/`: Business logic, agents, services, use cases
  - `3-Domain/`: Core models, entities, interfaces/contracts
  - `4-Persistence/`: Azure service wrappers, data processors, infrastructure
  - `5-Test/`: All test projects (unit, integration, E2E, performance)
  - `6-Docs/`: Documentation, specifications, architecture decisions
  - `7-Deployment/`: Infrastructure as Code, Docker, CI/CD scripts
  - `8-Agent-Instructions/`: AI agent guidance files
- **Dependency Direction**: Presentation → Application → Domain ← Persistence. Domain has NO outward dependencies.
- **File Placement**: NEVER place project files or code at repository root. Identify the correct layer before creating files.
- **Data Access**: Use Azure service wrappers with interfaces for AI services (OpenAI, AI Search, Document Intelligence). For future SQL needs, use ADO.NET for data access (NO Entity Framework for DAL), but EF Core migrations MAY be used for schema management.

**Rationale**: Clean Architecture enables testability, maintainability, and independent deployment of layers.

### III. Code Quality

Zero tolerance for build errors and warnings. Code MUST be production-ready.

- **Build Quality**: Fix ALL build errors and warnings immediately. Treat warnings as errors in CI/CD. No code merges with warnings.
- **No Placeholder Code**: Enterprise-grade code only. Never create "not implemented" stubs, placeholder returns, or partial implementations. All code MUST be fully working, tested, and deployment-ready.
- **Single Responsibility**: One class, interface, or enum per file. No multi-object files.
- **Async Patterns**: Use async/await for ALL I/O operations. Use `CancellationToken` where appropriate.
- **Naming & Style**: Follow C# conventions. Use meaningful names. Configure IDE to surface violations. Enable auto-format on save.
- **Error Handling**: Return consistent error responses (RFC 7807 ProblemDetails). Log sanitized diagnostic information. Never expose stack traces to users.

**Rationale**: Technical debt compounds. Consistent quality reduces maintenance burden and prevents production incidents.

### IV. Testing

Test coverage MUST be meaningful—testing for correctness, not coverage metrics alone.

- **Coverage Target**: 80% code coverage as a guideline, but prioritize testing critical paths, edge cases, and business logic over achieving arbitrary metrics.
- **Test Organization**:
  - Unit tests: Isolated component testing with mocked dependencies
  - Integration tests: Component interactions with real/mocked Azure services
  - E2E tests: Complete request-response cycles
  - Performance tests: Load and response time validation
- **Test-First Mindset**: Write tests before or alongside implementation. Tests MUST fail before passing (Red-Green-Refactor).
- **Test Data**: Use factory methods and builders. NEVER hard-code test data in production code. Test data belongs only in test projects.
- **Performance Targets**: API endpoints < 500ms (95th percentile), Database queries < 100ms average, Real-time operations < 100ms.

**Rationale**: Tests validate requirements and prevent regressions. Meaningful coverage ensures confidence without busywork.

### V. Observability

Systems MUST be debuggable, monitorable, and traceable in production.

- **Structured Logging**: Use semantic properties with ILogger<T>. Log at appropriate levels (Debug, Information, Warning, Error, Critical). Include correlation IDs via CorrelationService.
- **Telemetry**: Integrate with Application Insights. Track custom events for queries, metrics, and errors. Include QueryId, duration, and user context.
- **Health Checks**: Implement health check endpoints (`/health`) for all services. Monitor dependencies (Azure services, external APIs).
- **Metrics**: Collect KPIs—response times, error rates, throughput, cost per query.
- **Tracing**: Implement distributed tracing for request flows across agents and services.

**Rationale**: You cannot fix what you cannot see. Observability enables rapid incident response and performance optimization.

### VI. Resilience

Systems MUST handle failures gracefully and degrade without catastrophic impact.

- **Retry Policies**: Use Polly for all external calls. Implement exponential backoff (3 retries default).
- **Circuit Breakers**: Protect against cascading failures. Break after 5 failures, wait 1 minute before retry.
- **Timeouts**: Set explicit timeouts for all external operations.
- **Graceful Degradation**: When primary search fails, fallback to cached results or partial functionality. Return available results even if some agents fail.
- **Rate Limiting**: Implement rate limiting per IP/user on public APIs. Handle Azure service rate limits with backoff.
- **Error Messages**: Return user-friendly messages. Log detailed diagnostics internally.

**Rationale**: Distributed systems fail. Resilience patterns ensure the system remains useful during partial outages.

### VII. Process & Workflow

Development follows structured processes for consistency and traceability.

- **Task Tracking**: Create todo lists and status files in `6-Docs/` for complex tasks. Update immediately upon task completion.
- **Code Review Checklist**:
  - [ ] Files in correct numbered folder structure
  - [ ] Dependencies flow downward only
  - [ ] No secrets in source control
  - [ ] Input validation implemented
  - [ ] Unit tests cover new functionality
  - [ ] Build passes with zero warnings
  - [ ] Documentation updated for new features
- **Commit Discipline**: Small, self-contained changes. Run `dotnet build --no-restore` and `dotnet test` locally before commit.
- **Windows Environment**: Scripts MUST use PowerShell syntax. Avoid `&&` operator and bash/Linux syntax.
- **Documentation**: Update README, specs, and relevant docs for behavior changes. Reference acceptance criteria from requirements.

**Rationale**: Consistent processes reduce errors, improve collaboration, and maintain project knowledge across sessions.

## Additional Constraints

### Technology Stack

- **Platform**: Azure AI Foundry
- **Framework**: ASP.NET Core Web API (.NET 10.0 / C# 13)
- **AI Services**: Azure OpenAI (GPT-4o for planning, GPT-4o-mini for completion, text-embedding-3-large for vectors)
- **Search**: Azure AI Search (hybrid vector/keyword with semantic ranking)
- **Document Processing**: Azure Document Intelligence
- **Agent Orchestration**: Semantic Kernel Agent Framework
- **Resilience**: Polly (circuit breakers, retries, fallbacks)
- **Configuration**: Azure App Configuration + Azure Key Vault (production), environment variables (development)
- **Monitoring**: Application Insights
- **Testing**: xUnit, Moq
- **Deployment**: Azure Container Apps, Pulumi (Infrastructure as Code)

### Azure Services Integration

- Wrap all Azure SDKs in interfaces (e.g., `IAzureOpenAIClient`, `ISearchClient`)
- Use `DefaultAzureCredential` / Managed Identity for authentication
- Configure endpoints via Options pattern (`IOptions<AzureAIConfiguration>`)
- All Azure calls MUST go through ResilienceService (Polly policies)

### Cost Optimization

- Use GPT-4o-mini for standard chat completion (cost-optimized)
- Reserve GPT-4o for query planning and complex reasoning
- Implement query caching for common requests
- Batch process documents (100-1000 per batch)
- Monitor costs via Azure Cost Management

## Development Workflow

### Code Review Requirements

All PRs MUST pass:
1. Build with zero warnings
2. All tests pass
3. Code coverage meets 80% guideline (meaningful coverage)
4. Static analysis passes
5. Security checklist verified
6. Architecture compliance verified

### Task Management

- Complex tasks: Create status markdown in `6-Docs/`
- Use checklist format: `[ ]` pending, `[x]` completed, `[-]` in progress
- Break large tasks into smaller, verifiable steps
- Update status immediately after completing each item
- After completion, offer to clean up tracking files

## Governance

This constitution supersedes all other practices in this repository. Amendments require:

1. **Documentation**: Describe the change and rationale
2. **Version Bump**:
   - MAJOR: Backward-incompatible principle changes or removals
   - MINOR: New principles or materially expanded guidance
   - PATCH: Clarifications, wording fixes, non-semantic refinements
3. **Propagation**: Update dependent templates and guidance files
4. **Review**: All stakeholders must acknowledge changes

**Compliance Verification**:
- All PRs/reviews MUST verify compliance with these principles
- Complexity beyond these principles MUST be justified in writing
- Use agent instruction files in `8-Agent-Instructions/` for runtime guidance

**Version**: 1.0.0 | **Ratified**: 2025-12-25 | **Last Amended**: 2025-12-25
