# Domain Contracts Specification — 001-system-spec

## Purpose

This document defines all **domain interfaces** (contracts) that establish the boundaries between layers in the Motorcycle RAG System. These interfaces are the **Dependency Inversion** mechanism that allows Application and Persistence layers to depend on abstractions rather than concrete implementations.

**Location**: `3-Domain/MotorcycleRAG.Contracts/Interfaces/`

---

## Interface Design Principles

### 1. Interface Segregation
- Each interface should have a single, well-defined responsibility
- Clients should not depend on methods they don't use
- Prefer multiple focused interfaces over one "kitchen sink" interface

### 2. Dependency Inversion
- High-level modules (Application) should not depend on low-level modules (Persistence)
- Both should depend on abstractions (interfaces in Contracts layer)
- Abstractions should not depend on details; details should depend on abstractions

### 3. No Infrastructure Leakage
- Interfaces must NOT expose infrastructure types (Azure SDK, EF Core, etc.)
- Return primitive types, domain models, or DTOs only
- Accept domain models or DTOs as parameters

### 4. Async-First
- All I/O operations should be async (database, HTTP, file system)
- Include `CancellationToken` parameter for all async operations
- Use `Task<T>` or `ValueTask<T>` for return types

---

## Repository Interfaces

### IUserRepository

**Purpose**: Persistence contract for User entities

**Location**: `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserRepository.cs`

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for User entity operations.
/// Implemented in Persistence layer, consumed by Application layer.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Retrieves a user by their unique identifier.
    /// </summary>
    /// <param name="userId">User ID (e.g., subject/oid from identity token)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User entity if found, null otherwise</returns>
    Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by email address.
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user in the system.
    /// </summary>
    /// <param name="user">User entity to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created user with generated ID</returns>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user by ID.
    /// </summary>
    Task DeleteAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user exists by ID.
    /// </summary>
    Task<bool> ExistsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all users with pagination support.
    /// </summary>
    Task<(User[] Users, int TotalCount)> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
```

**Key Design Decisions**:
- Returns domain `User` entity (not DTO) - repository works with domain models
- Null-safe with `User?` return type for Get operations
- Tuple return for pagination `(User[], int)` - simple and efficient
- All async methods accept `CancellationToken`

### IUserPlanRepository

**Purpose**: Persistence contract for UserPlan entities (SKU/plan management)

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for UserPlan (SKU) entity operations.
/// </summary>
public interface IUserPlanRepository
{
    Task<UserPlan?> GetByIdAsync(string planId, CancellationToken cancellationToken = default);
    Task<UserPlan?> GetByNameAsync(string planName, CancellationToken cancellationToken = default);
    Task<UserPlan[]> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserPlan> CreateAsync(UserPlan plan, CancellationToken cancellationToken = default);
    Task<UserPlan> UpdateAsync(UserPlan plan, CancellationToken cancellationToken = default);
}
```

### IAuditRepository

**Purpose**: Persistence contract for audit logging

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for audit log operations.
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    /// Logs an audit event.
    /// </summary>
    Task LogEventAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit logs for a specific user.
    /// </summary>
    Task<AuditLog[]> GetByUserIdAsync(
        string userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit logs by event type.
    /// </summary>
    Task<AuditLog[]> GetByEventTypeAsync(
        string eventType,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all audit logs with pagination and filtering.
    /// </summary>
    Task<(AuditLog[] Logs, int TotalCount)> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 100,
        string? eventType = null,
        string? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default);
}
```

### IUsageRepository

**Purpose**: Persistence contract for usage tracking/metering

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for user usage tracking and quota enforcement.
/// </summary>
public interface IUsageRepository
{
    /// <summary>
    /// Gets current usage for a user on a specific date.
    /// </summary>
    Task<UsageRecord?> GetUsageAsync(
        string userId,
        DateTime date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments usage count for a user (atomic operation).
    /// </summary>
    Task IncrementUsageAsync(
        string userId,
        DateTime date,
        int incrementBy = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets usage history for a user over a date range.
    /// </summary>
    Task<UsageRecord[]> GetUsageHistoryAsync(
        string userId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if user has exceeded their daily quota.
    /// </summary>
    Task<bool> HasExceededQuotaAsync(
        string userId,
        DateTime date,
        int quotaLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets all usage for a specific date (administrative function).
    /// </summary>
    Task ResetUsageAsync(DateTime date, CancellationToken cancellationToken = default);
}
```

### IWebSourceRepository

**Purpose**: Persistence contract for managing allowed web sources for scraping

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for web source management (allow-listed sites for scraping).
/// </summary>
public interface IWebSourceRepository
{
    Task<WebSource?> GetByIdAsync(string webSourceId, CancellationToken cancellationToken = default);
    Task<WebSource?> GetByUrlAsync(string url, CancellationToken cancellationToken = default);
    Task<WebSource[]> GetAllEnabledAsync(CancellationToken cancellationToken = default);
    Task<WebSource> CreateAsync(WebSource webSource, CancellationToken cancellationToken = default);
    Task<WebSource> UpdateAsync(WebSource webSource, CancellationToken cancellationToken = default);
    Task DeleteAsync(string webSourceId, CancellationToken cancellationToken = default);
}
```

### IPlanRepository

**Purpose**: Persistence contract for query plans (RAG execution plans)

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Repository interface for query plan persistence (execution plan storage for debugging/analytics).
/// </summary>
public interface IPlanRepository
{
    Task<QueryPlan?> GetByIdAsync(string planId, CancellationToken cancellationToken = default);
    Task<QueryPlan> CreateAsync(QueryPlan plan, CancellationToken cancellationToken = default);
    Task<QueryPlan[]> GetByUserIdAsync(string userId, int limit = 10, CancellationToken cancellationToken = default);
    Task DeleteAsync(string planId, CancellationToken cancellationToken = default);
}
```

---

## Azure Service Interfaces

### IAzureOpenAIClient

**Purpose**: Abstraction for Azure OpenAI operations (chat, embeddings, vision)

**Location**: `3-Domain/MotorcycleRAG.Contracts/Interfaces/IAzureOpenAIClient.cs`

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure OpenAI client operations.
/// Implemented in Persistence layer (wraps Azure SDK), consumed by Application layer.
/// </summary>
public interface IAzureOpenAIClient
{
    /// <summary>
    /// Gets chat completion from Azure OpenAI.
    /// </summary>
    /// <param name="model">Model deployment name (e.g., "gpt-4o", "gpt-4o-mini")</param>
    /// <param name="prompt">User prompt or conversation history</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated text response</returns>
    Task<string> GetChatCompletionAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets chat completion with system message and user message.
    /// </summary>
    Task<string> GetChatCompletionAsync(
        string model,
        string systemMessage,
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single embedding vector from Azure OpenAI.
    /// </summary>
    /// <param name="model">Embedding model deployment name (e.g., "text-embedding-3-large")</param>
    /// <param name="text">Text to embed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Embedding vector (1536 dimensions for text-embedding-3-large)</returns>
    Task<float[]> GetEmbeddingAsync(
        string model,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple embedding vectors from Azure OpenAI (batch operation).
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(
        string model,
        string[] texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes multimodal (vision + text) content using GPT-4 Vision.
    /// </summary>
    /// <param name="model">Vision-capable model deployment name (e.g., "gpt-4-vision")</param>
    /// <param name="textPrompt">Text prompt describing the analysis task</param>
    /// <param name="imageData">Image data as byte array</param>
    /// <param name="imageContentType">Image MIME type (e.g., "image/png", "image/jpeg")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vision analysis result as text</returns>
    Task<string> ProcessMultimodalContentAsync(
        string model,
        string textPrompt,
        byte[] imageData,
        string imageContentType,
        CancellationToken cancellationToken = default);
}
```

**Key Design Decisions**:
- Returns primitive types (`string`, `float[]`) - NO Azure SDK types leaked
- Overloads for common patterns (single message vs system+user)
- Model name as parameter for flexibility (can switch deployments without code changes)
- All methods async with `CancellationToken`

### IAzureSearchClient

**Purpose**: Abstraction for Azure AI Search operations (vector search, indexing)

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure AI Search client operations.
/// Implemented in Persistence layer (wraps Azure SDK), consumed by Application layer.
/// </summary>
public interface IAzureSearchClient
{
    /// <summary>
    /// Performs vector search using query text (automatically generates embedding).
    /// </summary>
    Task<SearchResult[]> VectorSearchAsync(
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs vector search using pre-computed embedding.
    /// </summary>
    Task<SearchResult[]> VectorSearchAsync(
        float[] embedding,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs hybrid search (combines vector similarity and keyword matching).
    /// </summary>
    Task<SearchResult[]> HybridSearchAsync(
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs keyword search only (no vector search).
    /// </summary>
    Task<SearchResult[]> SearchAsync(
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes documents to Azure AI Search.
    /// </summary>
    Task IndexDocumentsAsync(
        IEnumerable<MotorcycleIndexDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes documents with detailed result tracking.
    /// </summary>
    Task<IndexingResult> IndexDocumentsWithResultsAsync(
        IEnumerable<MotorcycleIndexDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes documents from the search index by ID.
    /// </summary>
    Task DeleteDocumentsAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets document count in the index.
    /// </summary>
    Task<long> GetDocumentCountAsync(CancellationToken cancellationToken = default);
}
```

### IDocumentIntelligenceClient

**Purpose**: Abstraction for Azure Document Intelligence (PDF parsing)

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for Azure Document Intelligence client operations.
/// Implemented in Persistence layer, consumed by Application layer.
/// </summary>
public interface IDocumentIntelligenceClient
{
    /// <summary>
    /// Analyzes a document from URL using Azure Document Intelligence.
    /// </summary>
    /// <param name="documentUrl">URL to the document</param>
    /// <param name="modelId">Document Intelligence model (default: "prebuilt-layout")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed document analysis result</returns>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        string documentUrl,
        string modelId = "prebuilt-layout",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes document content from stream.
    /// </summary>
    Task<DocumentAnalysisResult> AnalyzeDocumentAsync(
        Stream documentStream,
        string contentType,
        string modelId = "prebuilt-layout",
        CancellationToken cancellationToken = default);
}
```

---

## Application Service Interfaces

### IMotorcycleRAGService

**Purpose**: Core business logic interface for RAG queries

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Core business logic interface for motorcycle RAG queries.
/// Implemented in Application layer, consumed by Presentation layer.
/// </summary>
public interface IMotorcycleRAGService
{
    /// <summary>
    /// Executes a motorcycle query using the RAG pipeline.
    /// </summary>
    Task<MotorcycleQueryResponse> QueryAsync(
        MotorcycleQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query for a specific authenticated user (with quota enforcement).
    /// </summary>
    Task<MotorcycleQueryResponse> QueryAsync(
        string userId,
        MotorcycleQueryRequest request,
        CancellationToken cancellationToken = default);
}
```

### IAgentOrchestrator

**Purpose**: Multi-agent coordination interface

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for multi-agent orchestration (coordinates search agents).
/// Implemented in Application layer.
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>
    /// Executes the multi-agent search pipeline for a query.
    /// </summary>
    Task<MotorcycleQueryResponse> ExecuteSearchAsync(
        MotorcycleQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and executes a query plan using QueryPlannerAgent.
    /// </summary>
    Task<QueryPlan> PlanAndExecuteAsync(
        MotorcycleQueryRequest request,
        CancellationToken cancellationToken = default);
}
```

### ISearchAgent

**Purpose**: Base interface for specialized search agents

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Base interface for specialized search agents.
/// Implemented by VectorSearchAgent, WebSearchAgent, PDFSearchAgent.
/// </summary>
public interface ISearchAgent
{
    /// <summary>
    /// Unique identifier for this agent type.
    /// </summary>
    SearchAgentType AgentType { get; }

    /// <summary>
    /// Performs search using this agent's specialized capabilities.
    /// </summary>
    Task<SearchResult[]> SearchAsync(
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this agent can handle the given query.
    /// </summary>
    Task<bool> CanHandleAsync(string query, CancellationToken cancellationToken = default);
}
```

### IQueryPlannerAgent

**Purpose**: Query analysis and planning agent

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for query planning agent (analyzes query and creates execution plan).
/// Implemented in Application layer using GPT-4o.
/// </summary>
public interface IQueryPlannerAgent
{
    /// <summary>
    /// Analyzes a query and generates an execution plan.
    /// </summary>
    Task<QueryPlan> PlanQueryAsync(
        string query,
        QueryContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decomposes a complex query into sub-queries.
    /// </summary>
    Task<string[]> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default);
}
```

---

## Infrastructure Service Interfaces

### IDataPipelineOrchestrator

**Purpose**: Data ingestion pipeline orchestration

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for data pipeline orchestration (CSV/PDF ingestion).
/// Implemented in Application layer.
/// </summary>
public interface IDataPipelineOrchestrator
{
    Task<ProcessingResult> ProcessCSVAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> ProcessPDFAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> ProcessBatchAsync(
        IEnumerable<Stream> fileStreams,
        CancellationToken cancellationToken = default);
}
```

### IMotorcycleIndexingService

**Purpose**: Document indexing service

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for motorcycle document indexing service.
/// Implemented in Persistence layer.
/// </summary>
public interface IMotorcycleIndexingService
{
    Task<IndexingResult> IndexDocumentsAsync(
        IEnumerable<MotorcycleIndexDocument> documents,
        CancellationToken cancellationToken = default);

    Task<IndexingResult> DeleteDocumentsAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken = default);

    Task<long> GetIndexedDocumentCountAsync(CancellationToken cancellationToken = default);
}
```

### IFileUploadService

**Purpose**: File upload handling

```csharp
namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for file upload service.
/// Implemented in Application layer.
/// </summary>
public interface IFileUploadService
{
    Task<UploadResult> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<FileInfo> GetFileInfoAsync(
        string fileId,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        string fileId,
        CancellationToken cancellationToken = default);
}
```

---

## Optimization Service Interfaces

### IBatchProcessingService

**Purpose**: Batch processing optimization (already in Contracts/Optimization/)

**Current Status**: ✅ Already correctly placed in Contracts

```csharp
namespace MotorcycleRAG.Contracts.Optimization;

/// <summary>
/// Service for batching operations to reduce network round-trips.
/// </summary>
public interface IBatchProcessingService
{
    Task<T[]> BatchAsync<T>(
        IEnumerable<T> items,
        Func<IEnumerable<T>, Task<T[]>> batchOperation,
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}
```

### IConnectionPoolService

**Purpose**: Connection pooling management

**Current Status**: ✅ Already correctly placed in Contracts

### IVectorCompressionService

**Purpose**: Vector compression for storage optimization

**Current Status**: ✅ Already correctly placed in Contracts

---

## Interface Migration Status

| Interface | Current Location | Correct? | Action Required |
|-----------|-----------------|----------|-----------------|
| `IUserRepository` | Contracts/Interfaces/ | ✅ YES | None |
| `IAuditRepository` | Contracts/Interfaces/ | ✅ YES | None |
| `IUsageRepository` | Contracts/Interfaces/ | ✅ YES | None |
| `IWebSourceRepository` | Contracts/Interfaces/ | ✅ YES | None |
| `IPlanRepository` | Contracts/Interfaces/ | ✅ YES | None |
| `IAzureOpenAIClient` | Contracts/Interfaces/ | ✅ YES | None |
| `IAzureSearchClient` | Contracts/Interfaces/ | ✅ YES | None |
| `IDocumentIntelligenceClient` | Contracts/Interfaces/ | ✅ YES | None |
| `IMotorcycleRAGService` | Contracts/Interfaces/ | ✅ YES | None |
| `IAgentOrchestrator` | Contracts/Interfaces/ | ✅ YES | None |
| `ISearchAgent` | Contracts/Interfaces/ | ✅ YES | None |
| `IQueryPlannerAgent` | Contracts/Interfaces/ | ✅ YES | None |
| `IDataPipelineOrchestrator` | Contracts/Interfaces/ | ✅ YES | None |
| `IMotorcycleIndexingService` | Contracts/Interfaces/ | ✅ YES | None |
| `IBatchProcessingService` | Contracts/Optimization/ | ✅ YES | None |
| `IConnectionPoolService` | Contracts/Optimization/ | ✅ YES | None |
| `IVectorCompressionService` | Contracts/Optimization/ | ✅ YES | None |

**Assessment**: ✅ All interfaces are already in correct locations. No migration needed for interfaces.

---

## Interface Anti-Patterns to Avoid

### ❌ Anti-Pattern 1: Exposing Infrastructure Types

```csharp
// BAD - Exposes Azure SDK Response<T> type
public interface IAzureOpenAIClient
{
    Task<Response<ChatCompletion>> GetChatCompletionAsync(string prompt);
}

// GOOD - Returns primitive type
public interface IAzureOpenAIClient
{
    Task<string> GetChatCompletionAsync(string model, string prompt, CancellationToken ct);
}
```

### ❌ Anti-Pattern 2: Kitchen Sink Interface

```csharp
// BAD - Too many responsibilities
public interface IRepository
{
    Task<object> DoEverythingAsync(params object[] args);
}

// GOOD - Focused interface
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
    Task<User> CreateAsync(User user);
}
```

### ❌ Anti-Pattern 3: Missing CancellationToken

```csharp
// BAD - No cancellation support
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
}

// GOOD - Supports cancellation
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}
```

---

## References

- **Data Model**: `specs/001-system-spec/data-model.md` - Model definitions
- **Research**: `specs/001-system-spec/research.md` - Interface design patterns
- **Shared Types**: Prefer Domain value objects / records referenced by interfaces (the `MotorcycleRAG.Contracts` project is interfaces only)
