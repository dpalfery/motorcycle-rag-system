# Data Model — 001-system-spec

This data model is derived from the baseline spec requirements (queries, ingestion, web sources, MCP tools, auth/SKUs, verification/citations).

## Core Entities

### User
- `UserId` (string, unique; stable subject identifier from authenticated identity, e.g., `sub`/`oid`)
- `Email` (string, optional; may be absent depending on B2C social provider consent/claims)
- `DisplayName` (string)
- `IsEnabled` (bool)
- `CreatedAt` (datetime)
- `LastSignInAt` (datetime?)
- `PlanSku` (enum: `Free`, `Plus`, `Pro`)

Auth-related fields (recommended):
- `IdentityType` (enum: `Customer`, `Admin`)
- `IdentityProvider` (string; e.g., `EntraB2C`, `EntraID`)
- `TenantId` (string?)

Validation:
- `PlanSku` required.

Rules:
- Customer authentication: Microsoft Entra External ID / B2C (OIDC), including social providers.
- Administrative access: Microsoft Entra ID (workforce).
- Administrative authorization: Entra application roles (e.g., `Admin`, `Operator`, `Viewer`) conveyed via token claims.

### UserProfile
- `UserId` (FK → User)
- `Preferences` (json/object)
- `CorrelationPreferences` (json/object)

### Plan (SKU)
- `Sku` (enum: `Free`, `Plus`, `Pro`)
- `DailyRequestLimit` (int? null => unlimited)

Rules:
- Free: 10 requests/day
- Plus: 100 requests/day
- Pro: unlimited

### UsageRecord
- `UserId` (FK → User)
- `UsageDateUtc` (date)
- `RequestCount` (int)
- `LastUpdatedAt` (datetime)

Rules:
- Reset at UTC day boundary.

## Query + Answering

### Query
- `QueryId` (string, unique)
- `UserId` (FK → User, nullable for anonymous if allowed)
- `QueryText` (string)
- `SubmittedAt` (datetime)
- `Preferences` (json/object)
- `Context` (json/object)

### SearchResult
- `ResultId` (string)
- `Content` (string)
- `RelevanceScore` (float 0..1)
- `Source` (SearchSource)
- `Metadata` (json/object)
- `Highlights` (string[])

### SearchSource
- `AgentType` (enum)
- `SourceName` (string)
- `SourceUrl` (string?)
- `DocumentId` (string?)
- `LastUpdated` (datetime?)

### Claim
- `ClaimId` (string)
- `Text` (string)

### Citation
- `CitationId` (string)
- `SourceType` (enum: `ManualPdf`, `Website`, `Dataset`)
- `Locator` (object)
  - Manual/PDF locator: `make`, `model`, `year`, `manualTitle`, `section`, `page`
  - Website locator: `url`, `title`, `retrievedAt`
  - Dataset locator: `datasetName`, `datasetVersion`, `recordId`
- `Excerpt` (string?)

### VerificationResult
- `ClaimId` (FK → Claim)
- `Status` (enum: `Supported`, `Unsupported`, `Conflicting`, `InsufficientEvidence`)
- `Rationale` (string)
- `SupportingCitationIds` (string[])

Rules:
- Claims included in final answer should be `Supported`.
- If not supported, response must omit or explicitly qualify.

## Ingestion + Pipeline

### IngestionJob
- `ExecutionId` (string, unique)
- `PipelineType` (enum: `CSV`, `PDF`, `Batch`, `Scheduled`, `WebScrape`)
- `Status` (enum)
- `StartTime` (datetime)
- `EndTime` (datetime?)
- `Errors` (string[])
- `Warnings` (string[])
- `Metrics` (json/object)
- `CreatedBy` (string)

### UploadedFile
- `FileId` (string)
- `OriginalFileName` (string)
- `StoredFileName` (string)
- `FilePath` (string)
- `FileSize` (long)
- `ContentType` (string)
- `DetectedFileType` (enum: `CSV`, `PDF`, `Unknown`)
- `UploadedAt` (datetime)

### Chunk
- `ChunkId` (string)
- `DocumentId` (string)
- `ChunkIndex` (int)
- `Text` (string)
- `Metadata` (json/object: page/section/etc)
- `Vector` (float[] or binary, depending on storage)

## Web Sources

### WebSource
- `WebSourceId` (string)
- `Url` (string)
- `IsEnabled` (bool)
- `CreatedAt` (datetime)
- `UpdatedAt` (datetime)
- `Notes` (string?)

### WebScrapeRun
- `RunId` (string)
- `WebSourceId` (FK → WebSource)
- `Status` (enum)
- `StartedAt` (datetime)
- `EndedAt` (datetime?)
- `PagesDiscovered` (int)
- `PagesIndexed` (int)
- `Errors` (string[])

## MCP Tools

### McpConfigurationVersion
- `ConfigVersionId` (string)
- `Version` (int)
- `Status` (enum: `Draft`, `Active`, `Archived`)
- `CreatedAt` (datetime)
- `CreatedByUserId` (FK → User)
- `ActivatedAt` (datetime?)
- `ActivatedByUserId` (FK → User?)

Rules:
- Exactly one configuration version should be `Active`.
- New edits create a new `Draft` version and then activate it (supports safe rollout).

### McpServerDefinition
- `ServerId` (string)
- `ConfigVersionId` (FK → McpConfigurationVersion)
- `Name` (string)
- `Transport` (enum: `Stdio`, `Http`)
- `Endpoint` (string? for HTTP)
- `Command` (string? for stdio)
- `Args` (string[]?)
- `IsEnabled` (bool)
- `Auth` (object)
  - `AuthType` (enum: `None`, `Header`, `OAuth`, `ManagedIdentity`, `KeyVaultReference`)
  - `HeaderName` (string?)
  - `SecretReference` (string?)
- `CreatedAt` (datetime)
- `UpdatedAt` (datetime)

Rules:
- Secrets should be referenced (e.g., Key Vault reference), not stored as raw values.

### ToolDefinition
- `ToolId` (string)
- `ServerId` (FK → McpServerDefinition, optional)
- `Name` (string)
- `Description` (string?)
- `IsEnabled` (bool)
- `Config` (json/object)
- `CreatedAt` (datetime)
- `UpdatedAt` (datetime)

Rules:
- Tools may be discovered from an MCP server or manually defined; either way, runtime availability is gated by `IsEnabled`.

### ToolConfigurationAuditEvent
- `EventId` (string)
- `ToolId` (FK → ToolDefinition)
- `ActorUserId` (FK → User)
- `ChangedAt` (datetime)
- `ChangeSummary` (string)
- `Before` (json/object)
- `After` (json/object)

### McpConfigurationConsumption
- Agents should consume MCP configuration via a provider that can refresh when the `Active` config version changes.
- Recommended: treat toolsets as versioned and bind each agent run/thread to a specific `ConfigVersionId` to avoid mid-run drift.

## Relationships (Summary)
- User 1—1 UserProfile
- User 1—N UsageRecord
- User 1—N Query
- Query 1—N Claim
- Claim 1—1 VerificationResult
- Claim N—N Citation (via `SupportingCitationIds`)
- WebSource 1—N WebScrapeRun
- ToolDefinition 1—N ToolConfigurationAuditEvent

---

# Clean Architecture Model Consolidation

## Purpose

This section defines the **consolidated C# model structure** after Clean Architecture remediation. It addresses the 16+ duplicate models between `MotorcycleRAG.Domain` and `MotorcycleRAG.Contracts` projects, providing clear placement rules and migration paths.

## Model Classification Decision Tree

```
Does model cross API/layer boundary?
├─ YES → DTO (place in Contracts/Models/)
└─ NO → Does it have identity + lifecycle?
    ├─ YES → Entity (place in Domain/Models/Entities/)
    └─ NO → Is it for configuration binding?
        ├─ YES → Options (place in Contracts/Options/)
        └─ NO → Value Object (Contracts/Models/ if shared, Domain/Models/ValueObjects/ if domain-specific)
```

## Target Folder Structure

### Contracts Project (Shared Kernel)
```
3-Domain/MotorcycleRAG.Contracts/
├── Interfaces/
│   ├── IUserRepository.cs
│   ├── IAzureOpenAIClient.cs
│   └── ... (all service interfaces)
├── Models/
│   ├── Query/
│   │   ├── MotorcycleQueryRequest.cs      [DTO]
│   │   ├── MotorcycleQueryResponse.cs     [DTO]
│   │   ├── SearchPreferences.cs           [Value Object - CONSOLIDATE from Domain]
│   │   └── Citation.cs                    [Value Object - MOVE from Domain]
│   ├── Search/
│   │   ├── SearchResult.cs                [Value Object - shared]
│   │   ├── MotorcycleIndexDocument.cs     [DTO - RENAME from MotorcycleDocument]
│   │   └── SearchSource.cs                [Value Object]
│   ├── Processing/
│   │   ├── ProcessingResult.cs            [DTO - CONSOLIDATE from both]
│   │   ├── ProcessedData.cs               [DTO - CONSOLIDATE from both]
│   │   └── DocumentChunk.cs               [Value Object]
│   └── Motorcycle/
│       ├── MotorcycleSpecification.cs     [Value Object]
│       ├── EngineSpecification.cs         [Value Object]
│       ├── PerformanceMetrics.cs          [Value Object]
│       └── SafetyFeatures.cs              [Value Object]
└── Options/
    ├── AzureAIOptions.cs                  [Options - MOVE+RENAME from Domain/AzureAIConfiguration]
    ├── ResilienceOptions.cs               [Options - MOVE+RENAME from Domain/ResilienceConfiguration]
    ├── CacheOptions.cs                    [Options - EXTRACT from Application layer]
    ├── SearchOptions.cs                   [Options - RENAME from SearchConfiguration]
    └── SqlOptions.cs                      [Options - KEEP as-is]
```

### Domain Project (Business Logic)
```
3-Domain/MotorcycleRAG.Domain/
├── Models/
│   ├── Entities/
│   │   ├── User.cs                        [Entity - KEEP, add TypeForwarder in old location]
│   │   ├── UserPlan.cs                    [Entity - KEEP, add TypeForwarder]
│   │   ├── MotorcycleDocument.cs          [Entity - rich domain model]
│   │   ├── AuditLog.cs                    [Entity]
│   │   └── UsageRecord.cs                 [Entity]
│   └── ValueObjects/
│       └── DocumentMetadata.cs            [Value Object - domain-specific]
└── TypeForwarders.cs                      [NEW - for binary compatibility]
```

## Consolidation Map: Duplicate Models

| Model Name | Domain Location | Contracts Location | Decision | Action |
|------------|----------------|-------------------|----------|---------|
| **User** | ✅ Has | ✅ Has | Keep in Domain (Entity) | Remove from Contracts, add TypeForwarder |
| **UserPlan** | ✅ Has | ✅ Has | Keep in Domain (Entity) | Remove from Contracts, add TypeForwarder |
| **SearchPreferences** | ✅ Has | ✅ Has | Keep in Contracts (Value Object - shared) | Remove from Domain, add TypeForwarder |
| **ProcessingResult** | ✅ Has | ✅ Has | Keep in Contracts (DTO) | Remove from Domain, add TypeForwarder |
| **ProcessedData** | ✅ Has | ✅ Has | Keep in Contracts (DTO) | Remove from Domain, add TypeForwarder |
| **MotorcycleDocument** | ✅ Has (Entity) | ✅ Has (Index DTO) | **KEEP BOTH - different purposes** | Rename Contracts version to MotorcycleIndexDocument |
| **Citation** | ✅ Has (Domain) | ❌ Missing | Move to Contracts (shared) | Move from Domain to Contracts |
| **AzureAIConfiguration** | ✅ Has (WRONG) | ❌ Missing | Move to Contracts/Options | Rename to AzureAIOptions, move to Contracts/Options/ |
| **ResilienceConfiguration** | ✅ Has (WRONG) | ❌ Missing | Move to Contracts/Options | Rename to ResilienceOptions, move to Contracts/Options/ |
| **CacheConfiguration** | Embedded in code | ❌ Missing | Extract to Contracts/Options | Create CacheOptions in Contracts/Options/ |
| **SearchConfiguration** | ❌ Missing | ✅ Has | Rename to Options | Rename to SearchOptions, move to Options/ |

## Type Forwarding Strategy

**File**: `3-Domain/MotorcycleRAG.Domain/TypeForwarders.cs` (NEW)

```csharp
using System.Runtime.CompilerServices;

// Models moved from Domain to Contracts
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.Query.SearchPreferences))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.Query.Citation))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.Processing.ProcessingResult))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.Processing.ProcessedData))]

// Configuration models renamed and moved to Contracts/Options
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.AzureAIOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.ResilienceOptions))]
```

**Benefits**:
- Binary compatibility - no recompilation needed
- Source compatibility with using statements
- Gradual migration support

## Intentional Duplicates (Keep Both)

### MotorcycleDocument (Two Versions - Different Purposes)

**Domain Entity** (`Domain/Models/Entities/MotorcycleDocument.cs`):
```csharp
/// <summary>
/// Rich domain model with behavior and business rules.
/// </summary>
public class MotorcycleDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DocumentType Type { get; set; }
    public DocumentMetadata Metadata { get; set; } = new();
    public float[]? ContentVector { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Domain methods
    public bool HasEmbedding() => ContentVector?.Length > 0;
    public void UpdateContent(string newContent) { ... }
}
```

**Index DTO** (`Contracts/Models/Search/MotorcycleIndexDocument.cs` - RENAMED):
```csharp
/// <summary>
/// Flattened structure optimized for Azure AI Search indexing.
/// INTENTIONALLY SEPARATE from domain MotorcycleDocument.
/// </summary>
public class MotorcycleIndexDocument
{
    public string Id { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[] ContentVector { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

**Mapping** (Application layer):
```csharp
public static class MotorcycleDocumentMapper
{
    public static MotorcycleIndexDocument ToIndexDocument(this MotorcycleDocument domain)
    {
        return new MotorcycleIndexDocument
        {
            Id = domain.Id,
            Make = domain.Metadata.Make,
            Model = domain.Metadata.Model,
            Content = domain.Content,
            ContentVector = domain.ContentVector ?? Array.Empty<float>()
        };
    }
}
```

## Model Usage Patterns by Layer

| Model | Presentation | Application | Persistence | Domain |
|-------|--------------|-------------|-------------|--------|
| MotorcycleQueryRequest | ✅ Receive | ✅ Validate | ❌ | ❌ |
| MotorcycleQueryResponse | ✅ Return | ✅ Build | ❌ | ❌ |
| User (Entity) | ❌ | ✅ Via IUserRepository | ✅ Persist/Retrieve | ✅ Define+Behavior |
| UserPlan (Entity) | ❌ | ✅ Via IUserRepository | ✅ Persist/Retrieve | ✅ Define+Behavior |
| MotorcycleIndexDocument | ❌ | ❌ | ✅ Index to Azure Search | ❌ |
| MotorcycleDocument (Entity) | ❌ | ✅ Business logic | ✅ Map to/from index | ✅ Define+Behavior |
| AzureAIOptions | ✅ Configure | ✅ Inject via IOptions | ✅ Inject via IOptions | ❌ |
| SearchPreferences | ✅ From request | ✅ Apply to search | ✅ Pass to search client | ❌ |

## Migration Validation Checklist

Phase completion criteria:

- [ ] All DTOs in `Contracts/Models/`
- [ ] All Entities in `Domain/Models/Entities/`
- [ ] All Options in `Contracts/Options/` with `*Options` naming
- [ ] No duplicates (except MotorcycleDocument/MotorcycleIndexDocument)
- [ ] TypeForwarders.cs created in Domain project
- [ ] All namespaces match folder structure
- [ ] Build succeeds with zero warnings
- [ ] All tests pass (unit + integration)
- [ ] No Azure SDK types in interfaces
- [ ] No infrastructure concerns in Domain layer

## References

- **Research**: `specs/001-system-spec/research.md` - Detailed consolidation patterns
- **Migration Mapping**: `specs/001-system-spec/contracts/migration-mapping.md` - File-by-file migration plan
