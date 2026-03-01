---
name: clean-architecture
description: Apply when creating, moving, adding, or searching files to maintain the Dependency Rule and separation of concerns across all development activities. 
---

# Clean Architecture + DDD Folder Structure (C#)

## **Core Architecture Principles**

Based on Uncle Bob Martin's Clean Architecture, this structure enforces:

1. **The Dependency Rule**: Source code dependencies can only point inward. Nothing in an inner circle can know anything about an outer circle.
2. **Independence of Frameworks**: Architecture doesn't depend on frameworks; frameworks are tools.
3. **Testability**: Business rules can be tested without UI, Database, or external elements.
4. **Independence of UI**: The UI can change without changing business rules.
5. **Independence of Database**: You can swap databases without affecting business rules.
6. **Independence of External Agencies**: Business rules don't know about the outside world.

### **Dependency Direction (Critical)**
```
Presentation → Application → Domain ← Persistence
     ↓              ↓            ↑         ↓
   Base ←──────────┴────────────┴─────────┘
```

**Allowed Dependencies:**
- Presentation → Application, Base
- Application → Domain, Base
- Persistence → Domain, Base
- Domain → Base (minimal, only for shared utilities)

**Forbidden Dependencies:**
- Domain → Application, Persistence, or Presentation
- Application → Persistence or Presentation
- Any layer → outer layer

**Key Rules for Code Generation:**

1. **Always respect the Dependency Rule** - inner layers never depend on outer layers
2. **Keep domain pure** - no framework code in domain entities
3. **Use interfaces for boundaries** - Application defines interfaces, Infrastructure implements
4. **Thin controllers** - only call use cases and map responses
5. **Rich domain models** - behavior with data, not anemic models
6. **Test without infrastructure** - domain and application tests need no database
7. **1 class or interface per file** - no multiple classes in one file
8. **Single Responsibility Principle** - each class has one job
9. **Open/Closed Principle** - open for extension, closed for modification
10. **Liskov Substitution Principle** - derived classes must be substitutable for their base classes
11. **Interface Segregation Principle** - many client-specific interfaces instead of one general-purpose interface
12. **Dependency Inversion Principle** - high-level modules shouldn't depend on low-level ones; both should depend on abstractions

---

## **Quick Reference: Layer Dependencies**
```
Layer           | Can Depend On
----------------|------------------
Presentation    | Application, Base
Application     | Domain, Contracts, Base
Domain          | Base (minimal)
Contracts       | Domain, Base
Persistence     | Domain, Contracts, Base
Tests           | Anything (for testing)
```

**Important Note on Contracts → Domain Dependency:**
Contracts referencing Domain is **CORRECT** in this architecture because:
- Repository interfaces need to reference Domain entities (e.g., `IRepository<Document>`)
- Service interfaces need to use Domain value objects and entities in method signatures
- DTOs in Contracts may need to reference Domain types for proper contract definitions
- Both projects are conceptually part of the "Domain Layer" (3-Domain folder)
- This follows the Dependency Inversion Principle: Application depends on Contracts (abstractions), Persistence implements them using Domain entities

## **Key Benefits Achieved**

✓ **Framework Independence:** Can swap ASP.NET for another framework
✓ **Database Independence:** Can swap SQL Server for PostgreSQL, Cosmos DB, etc.
✓ **UI Independence:** Can add mobile app without changing business logic
✓ **Testability:** Can test business rules without UI, database, or frameworks
✓ **Maintainability:** Clear boundaries make changes predictable
✓ **Azure Migration Friendly:** Can modernize infrastructure without touching domain
✓ **Vector Store Agnostic:** Can switch between Azure AI Search, Pinecone, Weaviate, etc.

---

*This architecture follows Uncle Bob Martin's Clean Architecture principles, ensuring that business logic remains independent of frameworks, databases, and delivery mechanisms, resulting in a system that is testable, maintainable, and adaptable to changing requirements.*
---

## **0-Base Layer (Shared Kernel)**

**Purpose:** Cross-cutting concerns and shared abstractions used across layers. Keep minimal to avoid coupling.

**Project:** `MotorcycleRAG.Core`

**Clean Architecture Alignment:** This is infrastructure for all layers but must not contain business logic or create circular dependencies.

**Contents:**

* **Constants / Enums:** Shared value constants
  *Folder:* `Constants`, `Enums`
* **Utilities:** Pure functions with no business logic
  *Folder:* `Utilities`
* **Base Exceptions:** Custom exception types
  *Folder:* `Exceptions`
* **Result Types:** Success/failure wrappers
  *Folder:* `Results`
* **Logging Abstractions:** ILogger interfaces (not implementations)
  *Folder:* `Abstractions/Logging`
* **External API Contracts:** Interface definitions only
  *Folder:* `Abstractions/External`

**⚠️ Warning:** This layer should contain NO implementations that depend on external frameworks (EF Core, Azure SDK, etc.). Only abstractions and pure utilities.

> Minimize dependencies on this layer to prevent tight coupling across your architecture.

---

## **1-Presentation Layer (Frameworks & Drivers)**

**Purpose:** Entry point for external interactions. This is the outermost layer where delivery mechanisms live.

**Projects:** `MotorcycleRAG.Api`, `MotorcycleRAG.UI`, or `MotorcycleRAG.Web`

**Clean Architecture Alignment:** Frameworks and Drivers layer - contains delivery mechanisms (web, API) as details that can be swapped.

**Contents:**

* **Controllers / Endpoints:** ASP.NET Core API controllers or minimal APIs
  *Folder:* `Controllers`
* **Hubs:** SignalR hubs for real-time updates
  *Folder:* `Hubs`
* **Filters / Middleware:** Exception handling, logging, request validation
  *Folder:* `Middleware`
* **ViewModels / DTOs:** Request/response payloads (Interface Adapters)
  *Folder:* `Models` or `ViewModels`
* **Mappers:** Convert between external DTOs and Application DTOs
  *Folder:* `Mappers`
* **Static Content / Pages:** Razor pages or SPA static assets
  *Folder:* `wwwroot` or `Pages`
* **Program.cs / Startup.cs:** Composition root, DI setup, and pipeline config

**Dependencies:** Application, Base
**Dependency Rule:** ✓ Points inward to Application

> **Key Principle:** Controllers are thin adapters. They receive requests, call Application use cases, and format responses. No business logic here.

---

## **2-Application Layer (Use Cases)**

**Purpose:** Contains application-specific business rules. Orchestrates the flow of data between entities and implements use cases.

**Project:** `MotorcycleRAG.Application`

**Clean Architecture Alignment:** Use Cases layer - implements all application-specific business rules.

**Contents:**

* **Use Cases / Handlers:** CQRS commands and queries implementing business workflows
  *Folder:* `Features/{FeatureName}/Commands`, `Features/{FeatureName}/Queries`
  *Example:* `Features/Documents/Commands/IndexDocument/IndexDocumentCommand.cs`
* **Services:** Application services coordinating between domain and infrastructure
  *Folder:* `Services`
* **DTOs:** Input/output models for use cases
  *Folder:* `DTOs` or within feature folders
* **Validators:** Input validation (FluentValidation)
  *Folder:* `Validators` or within feature folders
* **Authorization:** Application-level policy enforcement
  *Folder:* `Authorization`
* **Interfaces:** Abstractions for external dependencies (repositories, external services)
  *Folder:* `Interfaces`
* **Events / Notifications:** Application events
  *Folder:* `Events`
* **Dependency Injection Extensions:**
  *File:* `DependencyInjection.cs`

**Dependencies:** Domain, Base
**Dependency Rule:** ✓ Points inward to Domain

**Critical Rules:**
- No references to Presentation or Persistence projects
- No Entity Framework, SQL, HTTP, or framework-specific code
- All external dependencies accessed through interfaces defined here
- Orchestrates domain entities but doesn't contain domain logic

> **Use Case Pattern:** Each use case handles one specific application action (IndexDocument, SearchMotorcycles, RetrieveContext). Use cases call domain entities to execute business rules and use interfaces to persist changes.

---

## **3-Domain Layer (Entities - Core Business Rules)**

**Purpose:** Pure business logic and rules. The heart of the application. Enterprise-wide business rules that could be shared across applications.

**Projects:**

* `MotorcycleRAG.Domain` → Concrete domain models and logic
* `MotorcycleRAG.Contracts` → Interfaces only (abstractions owned by the domain)
* `MotorcycleRAG.Contracts.Models` → Shared contract models/DTOs (data shapes) used across boundaries; no infrastructure dependencies

**Clean Architecture Alignment:** Entities layer (innermost circle) - most stable, highest-level policies.

**DTO Placement Rules:**
* **Presentation DTOs** = API request/response models, UI models (transport).
* **Application DTOs** = use case request/response (input/output models for use cases).
* **Domain** = entities/value objects only. If it enforces business rules/invariants, it belongs here.
* **`MotorcycleRAG.Contracts`** = interfaces only (repositories/services/factories). Never DTOs/models.
* **`MotorcycleRAG.Contracts.Models`** = shared contract DTOs/models only (no interfaces, no implementations).

### **3a. Domain (Entities)**

**Contents:**

* **Entities:** Aggregate roots with identity
  *Folder:* `Entities`
  *Example:* `Document.cs`, `MotorcycleManual.cs`, `VectorChunk.cs`
  *Rule:* Rich domain models with behavior, not anemic data bags
* **Value Objects:** Immutable types without identity
  *Folder:* `ValueObjects`
  *Example:* `DocumentMetadata.cs`, `Embedding.cs`, `SearchQuery.cs`
* **Domain Services:** Business rules spanning multiple entities
  *Folder:* `Services`
  *Example:* `DocumentChunkingService.cs`
* **Domain Events:** State change notifications
  *Folder:* `Events`
  *Example:* `DocumentIndexedEvent.cs`
* **Specifications:** Reusable business rules for querying
  *Folder:* `Specifications`
* **Factories:** Complex object construction with invariants
  *Folder:* `Factories`
* **Exceptions:** Domain-specific exceptions
  *Folder:* `Exceptions`
* **Enums:** Domain-specific enums types
  *Folder:* `Exceptions`

**Dependencies:** Base (minimal - only utilities/enums)
**Dependency Rule:** ✓ No outward dependencies. Most stable layer.

### **3b. Contracts (Abstractions Owned by Domain)**

**Contents:**

* **Repository Interfaces:** Persistence contracts defined by domain needs
  *Folder:* `Repositories`
  *Example:* `IDocumentRepository.cs`, `IVectorStoreRepository.cs`
* **External Service Interfaces:** Contracts for external dependencies
  *Folder:* `Services`
  *Example:* `IEmbeddingService.cs`, `ILlmService.cs`
* **Factories (Domain Contracts):** Factory abstractions for creating aggregates/value objects while enforcing invariants
  *Folder:* `Factories`
  *Example names:* `IDocumentFactory`, `IMotorcycleManualFactory`, `IVectorChunkFactory`

* **Rule:** `MotorcycleRAG.Contracts` contains interfaces only (no DTOs/models)

**Dependencies:** Domain, Base

**Why Contracts → Domain is Correct:**
- Interfaces must reference Domain entities to define proper contracts
- Example: `Task<Document> GetDocumentAsync(Guid id)` requires `Document` from Domain
- Prevents duplicate model definitions between Contracts and Domain
- Maintains single source of truth for domain entities
- Both are in 3-Domain layer, conceptually part of the core domain

**Critical Rules:**
- NO references to any infrastructure concerns (EF Core, Azure, SQL, HTTP, etc.)
- NO persistence logic - only interfaces defining what domain needs
- CAN reference Domain entities and value objects for interface definitions
- All domain logic testable without any infrastructure

> **Key Principle:** If you removed all outer layers (UI, DB, frameworks), your domain layer should still compile and contain all core business rules. This is the "screaming architecture" - the domain tells you what the system does.

### **3c. Contracts.Models (Shared contract DTOs/models only)**

**Purpose:** Shared data shapes used across boundaries (API/Application/Persistence) without introducing infrastructure dependencies into the core.

**Project:** `MotorcycleRAG.Contracts.Models`

**Contents:**
* **DTOs / Models:** Simple data-only shapes for requests/responses between layers
* **Enums (shared):** Shared enum types required by contract models

**Dependencies:** Domain, Base (minimal)

**Allowed:** Plain C# types (records/classes), nullable annotations, simple enums, Domain value objects/types only when necessary.

**Forbidden (strict):**
- Interfaces (those belong in `MotorcycleRAG.Contracts`)
- Implementations (no services, no repositories, no factories)
- Any infrastructure/framework dependencies (ASP.NET, EF Core, Azure SDKs, SQL clients, etc.)
- Business logic (domain behavior stays in `MotorcycleRAG.Domain`)

---

## **4-Persistence Layer (Frameworks & Drivers - Data)**

**Purpose:** Implements data storage and retrieval. A detail that can be swapped.

**Project:** `MotorcycleRAG.Persistence`

**Clean Architecture Alignment:** Frameworks and Drivers layer - infrastructure detail.

**Contents:**

* **DbContext:** EF Core database context
  *Folder:* `Contexts`
  *File:* `MotorcycleRAGDbContext.cs`
* **Entity Configurations:** EF Core mappings, relationships, constraints
  *Folder:* `Configurations`
  *Example:* `DocumentConfiguration.cs`
* **Repositories:** Implementation of Domain.Contracts repository interfaces
  *Folder:* `Repositories`
  *Rule:* Implement interfaces from Domain.Contracts
* **Migrations:** Database schema migrations
  *Folder:* `Migrations`
* **Seed Data:** Initial data seeding
  *Folder:* `Seed`
* **ReadModels / Projections:** Query-optimized models (CQRS read side)
  *Folder:* `ReadModels`
* **Vector Store Implementations:** Azure AI Search, Pinecone, etc.
  *Folder:* `VectorStores`
* **Dependency Injection Extensions:**
  *File:* `DependencyInjection.cs`

**Dependencies:** Domain, Contracts, Base
**Dependency Rule:** ✓ Points inward to Domain

**Critical Rules:**
- NEVER reference Presentation or Application
- Implements interfaces defined in Domain.Contracts
- Contains all SQL, EF Core, vector database, and database-specific code
- Can be swapped for Dapper, Cosmos DB, or file storage without affecting domain

> **Dependency Inversion:** Domain defines `IDocumentRepository`, Persistence implements it. Application depends on the interface, not the implementation. This allows the database to be swapped without changing business logic.

---

## **5-Test Layer**

**Purpose:** Comprehensive testing at all levels, proving independence of frameworks and testability.

**Projects:**

* `MotorcycleRAG.Domain.Tests` → Unit tests for domain logic
* `MotorcycleRAG.Application.Tests` → Unit tests for use cases
* `MotorcycleRAG.Api.Tests` → Integration tests for API endpoints
* `MotorcycleRAG.IntegrationTests` → Full integration tests with database
* `MotorcycleRAG.Playwright-UI.Tests` → End-to-end UI tests

**Test Strategy:**

* **Domain Tests (Unit):**
  - Test entities, value objects, domain services
  - NO external dependencies (DB, APIs, frameworks)
  - Fast, isolated, pure logic tests
  
* **Application Tests (Unit):**
  - Test use cases with mocked repositories
  - Verify orchestration logic
  - No real database or external services
  
* **Integration Tests:**
  - Test full slices through Application → Domain → Persistence
  - Use real database (in-memory or TestContainers)
  - Verify data persistence and retrieval
  
* **API Tests:**
  - Test Controllers → Application integration
  - Use WebApplicationFactory
  - Verify HTTP contracts
  
* **UI Tests (E2E):**
  - Full user workflows
  - Real browser automation

**Contents:**

* **Unit Tests:** Test individual classes in isolation
* **Test Data Builders:** Fluent builders for test data
  *Folder:* `Builders`
* **Fixtures:** Shared test setup
  *Folder:* `Fixtures`
* **Mocks/Fakes:** Test doubles for external dependencies
  *Folder:* `Mocks`

> **Clean Architecture Benefit:** Because business logic is decoupled from infrastructure, you can test most of your system without databases, APIs, or UI frameworks.

---

## **6-Docs**

**Purpose:** Internal documentation, architectural decisions, and design rationale.

* `architecture-general.md` - This file
* `adr/` - Architecture Decision Records
* `diagrams/` - System diagrams (C4 model, sequence diagrams)
* `rag-pipeline.md` - RAG pipeline documentation

---

## **7-Deployment**

**Purpose:** CI/CD scripts, containerization, infrastructure as code.

**Contents:**

* **Docker:**
  *Folder:* `docker/`
  *Files:* `Dockerfile`, `docker-compose.yml`
* **Azure Bicep/Terraform:**
  *Folder:* `infrastructure/`
* **CI/CD Pipelines:**
  *Folder:* `.github/workflows/` or `azure-pipelines.yml`
* **Scripts:**
  *Folder:* `scripts/`

**Azure SKU Rule:** When deploying to Azure, use free tier SKUs from the 1-year Azure free account:
- App Service: F1 (Free)
- SQL Database: Basic (if needed)
- Storage: Standard GRS (5 GB free)
- Azure Functions: Consumption plan
- Azure AI Search: Free tier

> **Clean Architecture Benefit:** Your deployment choices are details. You can deploy to Azure App Service, Container Apps, AKS, or even AWS without changing your application code.

---
