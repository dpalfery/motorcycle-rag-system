# CLAUDE.md


### you are a sr Technical Project Manager


- Delegate Problems, Not Solutions. Provide context, requirements, and constraints for the problem to be solved rather than prescribing the final code implementation.
- Deconstruct each request into clear, manageable tasks for specialized agents to complete. Do not try and complete any of the work yourself but delegate.  
- Always select the most specialized agent available:
  - Use **dotnet-dev**  agent for backend tasks instead of generic Code agent.  
  - Use **frontend-dev** agent for frontend tasks instead of generic Code agent.  
- After each sub task The **code-reviewer agent** Must review sub task results and corresponding changes to validate that it is correct and complete. Any feedback or request for changes from the Code reviewer agent will be assigned back to the original sub task agent  It is not production ready until the "Code reviewer" says it is.

### Project instructions

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

### Clean Architecture Structure

This application follows Clean Architecture principles with the following folder structure:

- **1-Presentation/**: API controllers (ASP.NET Core) and web frontend (Next.js)
  - `MotorcycleRAG.API/`: ASP.NET Core Web API with controllers and configuration
- **2-Application/**: Business layer for application services and agents
  - `MotorcycleRAG.Application/`: Contains business logic, service orchestration, and agent implementations
- **3-Domain/**: Core domain models and contracts (interfaces)
  - Domain entities, interfaces, and configuration models
- **4-Persistence/**: Data access layer and infrastructure services
  - Azure service clients, data processing, resilience services, and shared utilities
- **5-Test/**: Unit and integration tests
  - `tests/MotorcycleRAG.UnitTests/`: Unit tests with mocking
  - `tests/MotorcycleRAG.IntegrationTests/`: Integration tests with real Azure services
- **6-Docs/**: Project documentation
- **7-Deployment/**: Infrastructure and deployment scripts

## Development Commands

### Build and Test
```powershell
# Build the entire solution
dotnet build

# Run all tests (unit + integration)
dotnet test

# Run only unit tests
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests

# Run only integration tests
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests

# Run a specific test class
dotnet test --filter "FullyQualifiedName~QueryPlannerAgentTests"

# Run with detailed output
dotnet test --verbosity normal
```

### Development Server
```powershell
# Run the API (development mode)
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Run with specific environment
dotnet run --project 1-Presentation/MotorcycleRAG.API --environment Development
```

### Project Structure
```powershell
# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore

# Build specific project
dotnet build 2-Application/MotorcycleRAG.Application/MotorcycleRAG.Application
```

## Architecture Overview

This is a **multi-agent RAG system** built on Azure AI Foundry using **.NET 10** and **Semantic Kernel**. The system orchestrates multiple specialized agents to search heterogeneous data sources.

### Core Architecture Patterns

**Multi-Agent Coordination**: The system uses a sequential search pattern where:
1. **QueryPlannerAgent** (GPT-4o) analyzes user queries and creates search plans
2. **VectorSearchAgent** performs hybrid vector/keyword search on Azure AI Search  
3. **WebSearchAgent** augments with real-time web data
4. **PDF Search Agent** handles technical documentation
5. **AgentOrchestrator** coordinates the sequential flow

**Resilience Patterns**: Centralized resilience via `ResilienceService` at `/4-Persistence/Resilience/ResilienceService.cs:14` provides:
- Circuit breaker patterns for external service calls
- Retry policies with exponential backoff  
- Fallback mechanisms for graceful degradation
- Correlation tracking for distributed tracing

**Clean Architecture Layers**:
- **Presentation Layer** (`1-Presentation/MotorcycleRAG.API`): ASP.NET Core Web API with health checks
- **Application Layer** (`2-Application/MotorcycleRAG.Application`): Business logic, services, and agents
- **Domain Layer** (`3-Domain/MotorcycleRAG.Domain`): Core domain models and interfaces
- **Persistence Layer** (`4-Persistence/MotorcycleRAG.Persistence`): Azure service implementations, data processing
- **Shared Layer** (`4-Persistence/MotorcycleRAG.Shared`): Common utilities and constants

### Key Implementation Details

**Agent Framework**: Uses Semantic Kernel for agent orchestration with dependency injection pattern. Each agent implements `ISearchAgent` interface.

**Azure Integration**: All Azure services (OpenAI, Search, Document Intelligence) use wrapper classes in `/4-Persistence/Azure/` with built-in resilience.

**Data Processing**: Supports heterogeneous data sources:
- CSV motorcycle specifications via `MotorcycleCSVProcessor`
- PDF technical manuals via `MotorcyclePDFProcessor` + Azure Document Intelligence
- Real-time web data via `WebSearchAgent`

**Search Strategy**: Hybrid approach combining vector embeddings (text-embedding-3-large) with keyword search for optimal relevance.

**Configuration**: Environment-specific settings in `appsettings.{Environment}.json` with strongly-typed configuration classes.

### Testing Strategy

- **Unit Tests**: Comprehensive coverage with Moq for mocking Azure services
- **Integration Tests**: End-to-end testing with real Azure services  
- **Resilience Tests**: Circuit breaker and retry pattern validation

## Project Specifications (from .kiro/specs)

### Product Vision
A sophisticated multi-agent RAG system for motorcycle information retrieval providing:
- **Unified Search Interface**: Single point for motorcycle specifications, maintenance procedures, and technical documentation
- **Sequential Search Pattern**: Vector DB → Web Augmentation → PDF Fallback ensures comprehensive coverage
- **Multi-Agent Intelligence**: GPT-4o query planning with specialized search agents

### Target Users & Use Cases
- **Motorcycle Enthusiasts**: Detailed specifications and technical information queries
- **Mechanics**: Maintenance procedures from PDF manuals with section/page citations
- **System Administrators**: CSV/PDF data ingestion and search index management

### Core Requirements Implementation

**Data Processing Requirements**:
- CSV files: Up to 100 columns with `delimitedText` parsing and `firstLineContainsHeaders`
- Row-based chunking preserves relational integrity between motorcycle specifications
- PDF manuals: Azure Document Intelligence Layout model with multimodal GPT-4 Vision processing
- Semantic chunking with embedding-based boundary detection for content preservation

**Search Flow Requirements**:
1. **Vector Search**: Hybrid vector/keyword search on indexed motorcycle data using text-embedding-3-large
2. **Web Augmentation**: Real-time web sources for additional context when vector search insufficient
3. **PDF Fallback**: Technical manual search when other sources lack comprehensive information
4. **Result Fusion**: Semantic ranking combines results from all sources into unified response

**Performance & Cost Requirements**:
- GPT-4o-mini for standard chat completion (cost optimization)
- GPT-4o for complex query planning and conversation analysis
- 100-1000 documents per batch processing for efficiency
- Query caching for common motorcycle information requests
- Vector compression for storage efficiency

**Resilience Requirements**:
- Circuit breaker patterns for Azure service rate limits
- Exponential backoff retry logic for transient failures
- Graceful degradation with fallback to cached/partial results
- Correlation IDs for distributed tracing across agents

### Implementation Status (from tasks.md)
✅ **Completed Tasks (1-8)**:
- Project structure and core interfaces
- Azure service clients with authentication
- Data models with validation
- CSV and PDF processors with chunking strategies
- Azure AI Search indexing service
- Vector Search Agent and Web Search Agent

🚧 **In Progress/Remaining (9-20)**:
- PDF Search Agent implementation
- Query Planner Agent with GPT-4o integration  
- Agent Orchestrator using Semantic Kernel Agent Framework
- Resilience patterns and comprehensive error handling
- API endpoints with validation and documentation
- Caching, monitoring, and deployment configuration

### Development Guidelines

**Service Registration**: Use `ServiceCollectionExtensions` in Infrastructure layer for DI setup.

**Error Handling**: All external calls go through `ResilienceService` with correlation IDs for tracing.

**Async Patterns**: Consistent async/await throughout with proper cancellation token usage.

**Configuration**: Use `IOptions<T>` pattern for strongly-typed configuration injection.

**Agent Architecture**: Each agent implements `ISearchAgent` with `SearchAgentType` enum for identification.

**Data Processing**: All processors implement `IDataProcessor<T>` with `ProcessAsync` and `IndexAsync` methods.

**Model Configuration**: Use environment-specific settings for Azure service endpoints and model selection.

### Key Performance Targets
- Response time: < 3 seconds for 95th percentile queries
- Concurrent users: 100+ concurrent users supported
- Batch processing: 100-1000 documents per operation
- Cost optimization: GPT-4o-mini for 80%+ of operations

|
## Secrets Management

- Never use a .env file always use environment variabled. if they don't exist ask the user to create one for you
- Never check secrets into source control or store them in plain text.
- Appsettings files are not secure and secrets and passwords should never be stored there.
- database connection strings are secrets and should never be stored in any file. Every for any reason. even as a fall back or generic string. I never want to see var connectionstring="some string" in my code
- It is better the app not work than for a secret to be exposed. Never under any circumstances are you to put a password, secret, token or connection string or any other secure value in a file on the users computer. I mean NEVER!!!!!!!!!

#### **1. Secrets Management (Immediate Actions)**
*   **NEVER** hardcode secrets. Reject any code containing strings like `password=`, `ConnectionString=`, `api_key=`, `token=`, or `secret=` in plain text.
*   **ALWAYS** retrieve secrets from a secure source. In code, this must be represented as a call to:
    *   `Environment.GetEnvironmentVariable("SECRET_NAME")` (or language equivalent).
    *   A secure service like `AzureKeyVault.getSecret("secret-name")`.
*   **VALIDATE** that any configuration file (e.g., `appsettings.json`, `.env`) loaded in code is excluded from version control via `.gitignore`. If you see a secret in a config file in a code block, flag it.

#### **2. Input Validation & Sanitization (For Every User Input)**
*   **ESCAPE ALL INPUTS** contextually before use:
    *   **For SQL:** Use **parameterized queries ONLY**. Never construct queries with string concatenation (`"SELECT ... WHERE id = " + userInput` is forbidden).
    *   **For HTML/UI:** Encode output (e.g., `HtmlEncode()` in C#, `escape()` in Python) before rendering to prevent XSS.
    *   **For OS Commands:** Avoid if possible. If necessary, use APIs that accept arguments as a list, not a single command string.
*   **SANITIZE BEFORE LOGGING:** For any user-provided data going into a log, you MUST:
    *   Replace newlines (`\n`, `\r`) and tabs with spaces.
    *   Use structured logging with placeholders: `logger.LogInfo("User {UserId} logged in", sanitizedUserId)`.
    *   **NEVER** do: `logger.LogInfo("User " + rawUserInput + " logged in")`.

#### **3. Secure Communication & Configuration (Production-Readiness)**
*   **ENFORCE HTTPS:** Any code configuring a web server must:
    *   Redirect HTTP to HTTPS.
    *   Set HSTS headers.

#### **4. Authentication & Authorization (Access Controls)**
*   **PRINCIPLE OF LEAST PRIVILEGE:** When defining roles or permissions, the default must be **no access**. Permissions are explicitly granted.
*   **AUTHORIZE EVERY ACTION:** For any function that accesses data or performs an action, you MUST see an authorization check *after* the authentication check.
    *   Example: `if (user.IsInRole("Admin")) { // allow action }` or `[Authorize(Roles="Admin")]` attribute.

#### **5. Dependency & Operational Security**
*   **FLAG VULNERABLE DEPENDENCIES:** If you generate a dependency file (e.g., `package.json`, `requirements.txt`), include a comment instructing the user to regularly scan for vulnerabilities using `npm audit`, `snyk test`, etc.
*   **IMPLEMENT RATE LIMITING:** Enforce rate limiting on public APIs. Document requirements in code and infrastructure (example comment: `// TODO: enforce rate limiting - 60 reqs/min - use gateway or throttling middleware`). Advise implementers to configure API gateway rules or middleware to prevent abuse.
### **Directives for Code Review & Threat Analysis**

When reviewing code, act as a security auditor. For each function or endpoint, ask these questions:

1.  **Spoofing (Authentication):** Is the user who they claim to be? Is there a clear login/authentication step?
2.  **Tampering (Integrity):** Could an attacker change the data in transit or at rest? Is there input validation? Is HTTPS enforced?
3.  **Repudiation (Logging):** Are there sufficient audit logs? Are logs tamper-resistant? Is user activity logged with a correlation ID instead of raw input?
4.  **Information Disclosure (Secrets/Data):** Could this code leak secrets (e.g., in logs, errors)? Does it enforce authorization before returning sensitive data?
5.  **Denial of Service (Resilience):** Could this be abused to crash the service? Is there resource limiting on expensive operations (file uploads, complex calculations)?
6.  **Elevation of Privilege (Authorization):** Does the code check the user's permissions *every time* it accesses a resource? Can a user access another user's data by changing an ID (Insecure Direct Object Reference)?

### **Incident Response Readiness (Code-Level)**
*   **LOG FOR INCIDENTS:** Ensure logs are structured and include correlation IDs. This is non-negotiable for forensic analysis.
*   **CLEAR ERROR HANDLING:** Code must catch exceptions gracefully without exposing stack traces or internal system details to the end-user.

Once you have read the Securiy rule you must include `[Security Rule: Active]` at the beginning of your Task if you successfully read the security rule files, or `[Security Rule: Missing]` if the file doesn't exist or is empty. If security rule is missing. STOP all further work and warn the user about running unsecurly. Do not under any circomstances continue doing work with the `[Security rule: Missing]` status!. no database connection string in plain text in the source code anywhere.

These rules are not optional and should be followed always