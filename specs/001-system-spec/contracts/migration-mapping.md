# Migration Mapping – 001-system-spec

## Purpose

This document provides a **complete file-by-file migration map** for the Clean Architecture remediation effort. It tracks all model consolidations, moves, and renames performed during Phase 1 to eliminate the 16+ duplicate models between `MotorcycleRAG.Domain` and `MotorcycleRAG.Contracts` projects.

**Status**: Phase 1 - Models moved to Contracts, duplicates exist awaiting cleanup

---

## Migration Summary

| Category | Count | Status |
|----------|-------|--------|
| Models Moved to Contracts | 20 | ✅ Complete |
| Duplicate Models (Domain + Contracts) | 13 | ⚠️ Awaiting Domain Cleanup |
| Configuration Models (Need Move to Options) | 10+ | 🔄 Pending Phase 2 |
| Interfaces (Already in Contracts) | 30+ | ✅ Complete |
| Optimization Interfaces (Need Move) | 3 | 🔄 Pending Phase 2 |

---

## Model Migration Map

### ✅ Models Successfully Moved to Contracts

These models were copied from `3-Domain/MotorcycleRAG.Domain/Models/` to `3-Domain/MotorcycleRAG.Contracts/Models/`. Domain versions still exist as duplicates pending cleanup.

| Model Name | Old Location | New Location | Status | Notes |
|------------|-------------|--------------|--------|-------|
| **AuditModels** | Domain/Models/AuditModels.cs | **Contracts/Models/AuditModels.cs** | ✅ Moved | Contains `AuditLog` entity - used cross-layer |
| **CSVFile** | Domain/Models/CSVFile.cs | **Contracts/Models/CSVFile.cs** | ✅ Moved | Processing DTO |
| **FileUploadModels** | Domain/Models/FileUploadModels.cs | **Contracts/Models/FileUploadModels.cs** | ✅ Moved | Contains `UploadedFile`, `UploadResult` DTOs |
| **IndexingModels** | Domain/Models/IndexingModels.cs | **Contracts/Models/IndexingModels.cs** | ✅ Moved | Contains `IndexingJob`, `IndexingStatus` |
| **IngestionModels** | Domain/Models/IngestionModels.cs (via IngestionJob.cs) | **Contracts/Models/IngestionModels.cs** | ✅ Moved | Contains `IngestionJob`, `PipelineType`, `JobStatus` |
| **MonitoringModels** | Domain/Models/MonitoringModels.cs | **Contracts/Models/MonitoringModels.cs** | ✅ Moved | Contains `HealthStatus`, `MetricsSnapshot` |
| **MotorcycleDocument** | Domain/Models/MotorcycleDocument.cs | **Contracts/Models/MotorcycleDocument.cs** | ✅ Moved | **INTENTIONAL DUPLICATE** - see notes below |
| **PDFDocument** | Domain/Models/PDFDocument.cs | **Contracts/Models/PDFDocument.cs** | ✅ Moved | Processing DTO |
| **PipelineModels** | Domain/Models/PipelineModels.cs | **Contracts/Models/PipelineModels.cs** | ✅ Moved | Contains `PipelineRun`, `PipelineStep` |
| **ProcessingModels** | Domain/Models/ProcessingModels.cs | **Contracts/Models/ProcessingModels.cs** | ✅ Moved | Contains `ProcessingResult`, `ProcessedData`, `DocumentChunk` |
| **QueryModels** | Domain/Models/QueryModels.cs | **Contracts/Models/QueryModels.cs** | ✅ Moved | Contains `MotorcycleQueryRequest`, `MotorcycleQueryResponse`, `QueryPlan`, `Citation` |
| **SearchConfiguration** | Domain/Models/SearchConfiguration.cs | **Contracts/Models/SearchConfiguration.cs** | ✅ Moved | Should be renamed to `SearchOptions` and moved to Options/ |
| **SearchResult** | Domain/Models/SearchResult.cs | **Contracts/Models/SearchResult.cs** | ✅ Moved | Value object used cross-layer |
| **UsageModels** | Domain/Models/UsageModels.cs | **Contracts/Models/UsageModels.cs** | ✅ Moved | Contains `UsageRecord`, `UsageLimit` entities |
| **UserModels** | Domain/Models/UserModels.cs | **Contracts/Models/UserModels.cs** | ✅ Moved | Contains `User`, `UserPlan`, `UserProfile` entities |
| **WebSourceModels** | Domain/Models/WebSourceModels.cs | **Contracts/Models/WebSourceModels.cs** | ✅ Moved | Contains `WebSource`, `WebScrapeRun` entities |

### New Models Created in Contracts Only

| Model Name | Location | Purpose |
|------------|----------|---------|
| **ConfigurationModels** | Contracts/Models/ConfigurationModels.cs | Placeholder for consolidated configuration DTOs |
| **UpdateProfileRequest** | Contracts/Models/UpdateProfileRequest.cs | API DTO for profile updates |
| **UsageResponse** | Contracts/Models/UsageResponse.cs | API response DTO for usage queries |
| **UserProfileResponse** | Contracts/Models/UserProfileResponse.cs | API response DTO for profile queries |

---

## ⚠️ Duplicate Models Requiring Cleanup

These models exist in **both** Domain and Contracts. Domain versions must be **deleted** after verifying all references updated.

| Model | Domain Path | Contracts Path | Cleanup Action |
|-------|------------|----------------|----------------|
| AuditModels | Domain/Models/AuditModels.cs | Contracts/Models/AuditModels.cs | Delete Domain version, add TypeForwarder |
| CSVFile | Domain/Models/CSVFile.cs | Contracts/Models/CSVFile.cs | Delete Domain version, add TypeForwarder |
| FileUploadModels | Domain/Models/FileUploadModels.cs | Contracts/Models/FileUploadModels.cs | Delete Domain version, add TypeForwarder |
| IndexingModels | Domain/Models/IndexingModels.cs | Contracts/Models/IndexingModels.cs | Delete Domain version, add TypeForwarder |
| MonitoringModels | Domain/Models/MonitoringModels.cs | Contracts/Models/MonitoringModels.cs | Delete Domain version, add TypeForwarder |
| **MotorcycleDocument** | Domain/Models/MotorcycleDocument.cs | Contracts/Models/MotorcycleDocument.cs | **KEEP BOTH** - Different purposes (see below) |
| PDFDocument | Domain/Models/PDFDocument.cs | Contracts/Models/PDFDocument.cs | Delete Domain version, add TypeForwarder |
| PipelineModels | Domain/Models/PipelineModels.cs | Contracts/Models/PipelineModels.cs | Delete Domain version, add TypeForwarder |
| ProcessingModels | Domain/Models/ProcessingModels.cs | Contracts/Models/ProcessingModels.cs | Delete Domain version, add TypeForwarder |
| QueryModels | Domain/Models/QueryModels.cs | Contracts/Models/QueryModels.cs | Delete Domain version, add TypeForwarder |
| SearchConfiguration | Domain/Models/SearchConfiguration.cs | Contracts/Models/SearchConfiguration.cs | Delete Domain version, rename Contracts version to SearchOptions, move to Options/ |
| SearchResult | Domain/Models/SearchResult.cs | Contracts/Models/SearchResult.cs | Delete Domain version, add TypeForwarder |
| UsageModels | Domain/Models/UsageModels.cs | Contracts/Models/UsageModels.cs | Delete Domain version, add TypeForwarder |
| UserModels | Domain/Models/UserModels.cs | Contracts/Models/UserModels.cs | Delete Domain version, add TypeForwarder |
| WebSourceModels | Domain/Models/WebSourceModels.cs | Contracts/Models/WebSourceModels.cs | Delete Domain version, add TypeForwarder |

### Special Case: MotorcycleDocument (Intentional Duplicate)

**Decision**: **KEEP BOTH** versions - they serve different purposes.

**Domain Version** (`Domain/Models/MotorcycleDocument.cs`):
- **Purpose**: Rich domain entity with business logic
- **Characteristics**: Validation methods, state transitions, domain rules
- **Usage**: Application layer business logic, domain services
- **Namespace**: `MotorcycleRAG.Domain.Models`

**Contracts Version** (`Contracts/Models/MotorcycleDocument.cs`):
- **Purpose**: Flattened DTO for Azure AI Search indexing
- **Characteristics**: Simple POCO, optimized for serialization
- **Usage**: Persistence layer indexing, search result deserialization
- **Namespace**: `MotorcycleRAG.Contracts.Models`
- **Recommendation**: Rename to `MotorcycleIndexDocument` to clarify purpose

**Rationale**: Domain entities should not be directly indexed. Separation allows:
- Domain model evolution without breaking search index schema
- Optimized serialization format for search (e.g., flattened metadata)
- Clear mapping layer between domain and persistence concerns

---

## 🔄 Pending Migrations (Phase 2)

### Configuration Models → Contracts/Options/

All `*Configuration` classes in Domain must move to `Contracts/Options/` and be renamed to `*Options` per .NET conventions.

| Current Location | Target Location | Rename |
|-----------------|-----------------|---------|
| Domain/Models/**AzureAIConfiguration.cs** | **Contracts/Options/AzureAIOptions.cs** | YES |
| Domain/Models/**ResilienceConfiguration.cs** | **Contracts/Options/ResilienceOptions.cs** | YES |
| Domain/Models/**CircuitBreakerConfiguration.cs** | **Contracts/Options/CircuitBreakerOptions.cs** | YES |
| Domain/Models/**RetryConfiguration.cs** | **Contracts/Options/RetryOptions.cs** | YES |
| Domain/Models/**FallbackConfiguration.cs** | **Contracts/Options/FallbackOptions.cs** | YES |
| Domain/Models/**ConnectionStringsConfiguration.cs** | **Contracts/Options/ConnectionStringsOptions.cs** | YES |
| Domain/Models/**TelemetryConfiguration.cs** | **Contracts/Options/TelemetryOptions.cs** | YES |
| Domain/Models/**ModelConfiguration.cs** | **Contracts/Options/ModelOptions.cs** | YES |
| Domain/Models/**WebSearchConfiguration.cs** | **Contracts/Options/WebSearchOptions.cs** | YES |
| Domain/Models/**AppConfiguration.cs** | **Contracts/Options/AppOptions.cs** | YES |
| **Contracts/Options/SqlOptions.cs** | (Keep as-is) | NO |

**Rationale**:
- Configuration classes are infrastructure concerns, not domain logic
- IOptions<T> pattern requires `*Options` naming convention
- Contracts project is appropriate for cross-layer configuration contracts

### Optimization Interfaces → Contracts/Optimization/

| Current Location | Target Location | Notes |
|-----------------|-----------------|-------|
| Application/Optimization/**IBatchProcessingService.cs** | **Contracts/Optimization/IBatchProcessingService.cs** | Deleted from Application, needs to be in Contracts |
| Application/Optimization/**IConnectionPoolService.cs** | **Contracts/Optimization/IConnectionPoolService.cs** | Deleted from Application, needs to be in Contracts |
| Application/Optimization/**IVectorCompressionService.cs** | **Contracts/Optimization/IVectorCompressionService.cs** | Deleted from Application, needs to be in Contracts |

**Status**: Git status shows these interfaces were **deleted** from Application layer but not yet created in Contracts. They exist in `Contracts/Optimization/` directory per domain-contracts.md.

### Domain Models Remaining (Keep in Domain)

These models should **stay** in Domain as they are pure domain entities with business logic:

| Model | Location | Reason |
|-------|----------|--------|
| **IngestionJob** | Domain/Models/IngestionJob.cs | Domain entity with lifecycle management |
| **MotorcycleSpecification** | Domain/Models/MotorcycleSpecification.cs | Rich value object with domain rules |
| **QueryPlan** | Domain/Models/QueryPlan.cs | Domain entity representing RAG execution plan |
| **TrustedSource** | Domain/Models/TrustedSource.cs | Domain value object for source trust tiers |
| **CircuitBreakerState** | Domain/Models/CircuitBreakerState.cs | Domain value object (though could argue for moving to Contracts) |
| **ServiceCircuitBreakerConfig** | Domain/Models/ServiceCircuitBreakerConfig.cs | Domain-specific resilience config |

---

## TypeForwarder Implementation Plan

**File**: `3-Domain/MotorcycleRAG.Domain/TypeForwarders.cs` (to be created)

```csharp
using System.Runtime.CompilerServices;

// Models moved from Domain/Models to Contracts/Models
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.AuditModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.CSVFile))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.FileUploadModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.IndexingModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.IngestionModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.MonitoringModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.PDFDocument))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.PipelineModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.ProcessingModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.QueryModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.SearchResult))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.UsageModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.UserModels))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Models.WebSourceModels))]

// Configuration models renamed and moved to Contracts/Options
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.AzureAIOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.ResilienceOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.CircuitBreakerOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.RetryOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.FallbackOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.ConnectionStringsOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.TelemetryOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.ModelOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.WebSearchOptions))]
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.AppOptions))]

// SearchConfiguration renamed to SearchOptions and moved to Options
[assembly: TypeForwardedTo(typeof(MotorcycleRAG.Contracts.Options.SearchOptions))]
```

**Benefits**:
- Binary compatibility: Existing compiled code continues to work
- Source compatibility: Old `using` statements still resolve
- Gradual migration: No "big bang" recompilation required

**Note**: TypeForwarders should only be added **after**:
1. Target files exist in Contracts with correct namespaces
2. All project references are updated
3. All `using` statements are updated
4. All tests pass with new locations

---

## Namespace Migration Map

### Before (Incorrect)

```csharp
using MotorcycleRAG.Domain.Models;  // Contains 30+ mixed models

// Usage
var request = new MotorcycleQueryRequest();
var user = new User();
var config = new AzureAIConfiguration();  // ❌ Configuration in Domain
```

### After (Correct)

```csharp
using MotorcycleRAG.Contracts.Models;    // DTOs and shared entities
using MotorcycleRAG.Contracts.Options;   // Configuration options
using MotorcycleRAG.Domain.Models;       // Pure domain entities only

// Usage
var request = new MotorcycleQueryRequest();  // From Contracts.Models
var user = new User();                       // From Contracts.Models
var config = new AzureAIOptions();           // ✅ From Contracts.Options
```

---

## Validation Checklist

Phase 1 is complete when:

- [x] All 20 model files exist in Contracts/Models/
- [ ] All 13 duplicate models removed from Domain (except MotorcycleDocument)
- [ ] Contracts version of MotorcycleDocument renamed to MotorcycleIndexDocument
- [ ] All 10+ configuration models moved to Contracts/Options/ and renamed to *Options
- [ ] TypeForwarders.cs created in Domain project
- [ ] All project `using` statements updated
- [ ] All DI registrations updated to use IOptions<T>
- [ ] Build succeeds with zero warnings
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] Git diff reviewed for unintended changes

---

## References

- **Plan**: `specs/001-system-spec/plan.md` - Phase 1 objectives
- **Data Model**: `specs/001-system-spec/data-model.md` - Model consolidation strategy
- **Domain Contracts**: `specs/001-system-spec/contracts/domain-contracts.md` - Interface specifications
- **Quickstart**: `specs/001-system-spec/quickstart.md` - Developer migration guide

---

**Last Updated**: 2025-12-28 (Phase 1 - Models moved, cleanup pending)
