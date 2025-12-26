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
