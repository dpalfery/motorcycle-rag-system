## Project Overview
This project is a sophisticated **multi-agent RAG (Retrieval-Augmented Generation) system** for motorcycle information retrieval. It is designed to pass **OWASP ASVS Level 2** security standards and follows strict **Clean Architecture** principles.

The system orchestrates specialized agents to search heterogeneous data sources (CSV specs, PDF manuals, Trusted Web) and provides a unified, cited response. It includes a **React WebUI** for users and a **.NET MAUI Admin App** (Windows-first) for data ingestion and management.

## Authoritative Documentation
*   **`AGENTS.md`**: Primary source for Architectural Rules (Folder Structure) and Security Directives.
*   **`specs/001-system-spec/`**: Detailed functional requirements (`spec.md`), remediation plans (`plan.md`), and task tracking (`tasks.md`).

## Technology Stack

### Backend
*   **Framework**: .NET 10.0 (ASP.NET Core Web API)
*   **Language**: C# 13
*   **Architecture**: Clean Architecture + DDD (8 Layers)
*   **AI/RAG**: Semantic Kernel, Azure OpenAI (GPT-4o, text-embedding-3-large), Azure AI Search.
*   **Data**: SQL Server (Dapper), Azure Blob Storage.
*   **Identity**: Microsoft Entra External ID / B2C (OIDC).

### Frontend
*   **User UI**: React 19 + Vite + Tailwind CSS (`1-Presentation/MotorcycleRag.WebUI`).
*   **Admin UI**: .NET MAUI for Windows (`1-Presentation/MotorcycleRAG.Admin`).
*   **BFF**: Backend-for-Frontend pattern using YARP/ASP.NET Core (`1-Presentation/MotorcycleRag.WebUI.BFF`).

## Architecture & Folder Rules (`AGENTS.md`)

The project strictly follows these layers. Dependencies must point **inward** (Presentation -> Application -> Domain <- Persistence).

1.  **0-Base** (`MotorcycleRAG.Shared`): Cross-cutting concerns, shared config, utilities.
2.  **1-Presentation**: API Controllers, WebUI, Admin App. Entry points only.
3.  **2-Application**: Use cases, orchestration, agents, interfaces for infrastructure.
4.  **3-Domain**: Pure business logic, entities, and repository interfaces. **No infrastructure dependencies.**
5.  **4-Persistence**: Implementation of interfaces, Azure SDKs, DB context.
6.  **5-Test**: Unit (`.UnitTests`), Integration (`.IntegrationTests`), and UI tests.
7.  **6-Docs**: Documentation.
8.  **7-Deployment**: IaC (Pulumi), Dockerfiles.

## Security Rules (Mandatory)

*   **Secrets**: NEVER store secrets in code/config files. Use Environment Variables or Key Vault.
*   **Validation**: Sanitize all inputs. Use parameterized SQL queries ONLY.
*   **Authorization**: Explicitly authorize every action (e.g., `[Authorize(Policy = "DataAdmin")]`).
*   **Logging**: Redact sensitive data (PII, query text) from logs.

## Key Features & Requirements (`spec.md`)

*   **Multi-Agent Search**: Query Planner -> Vector Search -> Web Search (Trusted Sources only) -> PDF Search (with page/section citations).
*   **Trust Tiers**: Tier A (OEM Manuals) > Tier B (Reputable Media) > Tier C (Community).
*   **Ingestion Pipeline**: Batch processing of CSVs and PDFs with status tracking.
*   **MCP Support**: Model Context Protocol integration for extensible tools (managed via Admin App).
*   **User Plans**: Free (10 req/day), Plus (100 req/day), Pro (Unlimited).

## Development Status (`tasks.md`)

*   **Done**: Core Auth (Entra ID), SQL Persistence, User Story 1 (Ask Question), User Story 1a (Profiles/Limits).
*   **In Progress/Pending**: PDF Manual Search (US3), MAUI Admin App (US3a), Web Source Management (US6), MCP Config (US7).

## Usage Commands

### Backend & API
```powershell
# Run API
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Run Tests (Unit + Integration)
dotnet test
```

### Frontend (User)
```powershell
cd 1-Presentation/MotorcycleRag.WebUI
npm run dev
```

### Data Ingestion
*   **API**: `POST /api/DataPipeline/upload` (Files), `POST /api/DataPipeline/process` (Trigger).
*   **Admin App**: Launch `1-Presentation/MotorcycleRAG.Admin` (pending implementation).

# Clean Architecture + DDD Folder Structure (C#)

## **0-Base Layer**

**Purpose:** Cross-cutting or shared concerns used across all layers.

**Project:** `HotshotLogistics.Core`

**Contents:**

* **Dependency Injection / Config Extensions**
* **Logging, Email, Caching Adapters**
* **External API Integrations**
* **Constants / Enums / Utilities**
* **Factories / Contracts Shared Across Layers**
* **Base Exceptions:** Custom exception types used across the solution
  *Folder:* `Exceptions`
* **Base Repositories:** Shared repository interfaces and base implementations
  *Folder:* `Repositories`

> This is the foundational layer that other layers may reference for shared utilities and abstractions.

---

## **1-Presentation Layer**

**Purpose:** Entry point for all user interactions (HTTP, gRPC, SignalR, etc.)

**Projects:** `HotshotLogistics.Api`, `HotshotLogistics.UI`, or `HotshotLogistics.Web`

**Contents:**

* **Controllers / Endpoints:** ASP.NET Core API controllers or minimal APIs
  *Folder:* `Controllers`
* **Hubs:** SignalR hubs for real-time updates
  *Folder:* `Hubs`
* **Filters / Middleware:** Exception handling, logging, request validation
  *Folder:* `Middleware`
* **ViewModels / DTOs:** Request/response payloads specific to presentation
  *Folder:* `DTOs`
* **Static Content / Pages:** Razor pages or SPA static assets
  *Folder:* `wwwroot` or `Pages`
* **Program.cs / Startup.cs:** Composition root, DI setup, and pipeline config

> This layer calls into `2-Application` only. It does not directly reference persistence or domain implementations.

---

## **2-Application Layer**

**Purpose:** Orchestrates use cases and enforces application logic, coordinating between domain and infrastructure.

**Project:** `HotshotLogistics.Application`

**Contents:**

* **Services / Use Cases:** Application service classes implementing workflows
  *Folder:* `Services`
* **Commands / Queries / Handlers:** CQRS pattern logic, mediator handlers
  *Folder:* `Features` or `Handlers`
* **Validators:** Input validation (FluentValidation, custom logic)
  *Folder:* `Validators`
* **Authorization:** Policy providers, role/claim checks
  *Folder:* `Authorization`
* **DTOs:** Input/output models for use cases
  *Folder:* `DTOs`
* **Events / Notifications:** Application-level events or mediators
  *Folder:* `Events`
* **Dependency Injection Extensions:**
  *File:* `ServiceCollectionExtensions.cs`

> This layer depends only on `3-Domain` and `4-Contracts`.
> Contains no UI or infrastructure logic.

---

## **3-Domain Layer**

**Purpose:** Pure business logic, rules, and core models.

**Projects:**

* `HotshotLogistics.Domain` → concrete domain models and logic
* `HotshotLogistics.Contracts` → shared interfaces and abstractions

**Contents:**

* **Entities:** Aggregate roots and domain entities
  *Folder:* `Entities`
  *Example:* `Order.cs`, `Job.cs`
* **ValueObjects:** Immutable types without identity
  *Folder:* `ValueObjects`
* **Domain Services:** Business rules not tied to entities
  *Folder:* `Services`
* **Factories:** Construction logic enforcing invariants
  *Folder:* `Factories` (in `Contracts` if shared)
* **Repositories / Interfaces:** Domain contracts for persistence and messaging
  *Folder:* `Repositories`, `Hubs`, etc. (in `Contracts`)
* **Domain Events:** Core events representing state changes
  *Folder:* `Events`
* **DTOs (Domain-Scoped):** Internal payloads used inside domain boundaries
  *Folder:* `DTOs`
* **Dependencies:** Shared abstractions for dependency registration
* **README.md:** Explain model boundaries and design rules

> The domain is completely persistence-agnostic and unaware of infrastructure.

---

## **4-Persistence Layer**

**Purpose:** Implements data storage and retrieval using EF Core, Dapper, or external stores.

**Project:** `HotshotLogistics.Persistence`

**Contents:**

* **DbContext:** EF Core database context
  *Folder:* `Contexts`
* **Entity Configurations:** Mapping, relationships, and constraints
  *Folder:* `Configurations`
* **Repositories:** Implementation of domain repository interfaces
  *Folder:* `Repositories`
* **Migrations / Seed Data:** Database migrations and seeders
  *Folder:* `Migrations`, `Seed`
* **ReadModels / Projections:** Optimized models for queries
  *Folder:* `ReadModels`
* **README.md:** Connection strings, migration usage, conventions

> References `3-Domain` and `4-Contracts`, but never `1-Presentation`.

---

## **5-Test Layer**

**Purpose:** Centralized location for all test projects covering unit, integration, and UI tests.

**Projects:**

* `HotshotLogistics.Tests` → Unit tests for domain, application, and services
* `HotshotLogistics.IntegrationTests` → Integration tests for API and persistence
* `HotshotLogistics.Playwright-UI.Tests` → End-to-end UI tests using Playwright

**Contents:**

* **Unit Tests:** Test individual classes, methods, and business logic in isolation
  *Folder:* `HotshotLogistics.Tests`
* **Integration Tests:** Test API endpoints, database operations, and cross-layer interactions
  *Folder:* `HotshotLogistics.IntegrationTests`
* **UI Tests:** End-to-end browser-based tests for the admin dashboard
  *Folder:* `HotshotLogistics.Playwright-UI.Tests`
* **Test Data:** Shared fixtures, mocks, and sample data for tests
  *Folder:* `data`

> All test projects should reference their target projects but remain isolated from production code paths.
> Follow naming convention: `{ClassName}Tests.cs` for unit tests, `{Feature}IntegrationTests.cs` for integration tests.

---

## **6-Docs**

**Purpose:** Internal documentation, architectural decisions, and tasks.

* `architecture-general.md`
* `interface-cleanup-tasks.md`
* Diagrams, design notes, checklists

---

## **7-Deployment**

**Purpose:** CI-CD scripts, Docker Files, anything that helps with the deployment of the solution either locally or in production.

* `Docker files`
* `scripts`
* **important rule**: when deciding on which sku to use in Azure you must pick from teh free service skus provided as part of the 1 year azure free account

## **8-Agent-Instructions**
**Purpose:** Instructions for the AI agent to follow when generating code.

* `architecture-general.md`
* `interface-cleanup-tasks.md`
* `agent-instructions.md`
name: "Security-General-Rule"
description: "Enforces fundamental security practices that apply across all development activities in Hotshot Logistics."
when-to-apply: "always" At the start of every task
rule: |

This document is authoritative for security directives; other rule files must reference it for security-related guidance.

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