# Implementation Plan: Clean Architecture Remediation

**Branch**: `001-system-spec` | **Date**: 2025-12-28 | **Spec**: [spec.md](./spec.md)
**Input**: Clean Architecture violation assessment - 38 critical violations identified across dependency rules, model duplication, and infrastructure concerns

## Summary

This plan addresses critical Clean Architecture violations discovered in the Motorcycle RAG System codebase. The violations include:
- **1 CRITICAL circular dependency**: Persistence → Application (forbidden)
- **2 CRITICAL framework dependencies** in Domain/Contracts layer
- **16 HIGH duplicate models** between Domain and Contracts
- **10+ HIGH infrastructure configuration classes** misplaced in Domain layer
- **6 MEDIUM infrastructure implementations** in Application layer

The remediation will restore proper dependency flow (Presentation → Application → Domain ← Persistence), eliminate model duplication, and separate infrastructure concerns from domain logic.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: Azure.AI.OpenAI 2.1.0, Azure.Search.Documents 11.7.0, Azure.AI.DocumentIntelligence 1.0.0, Semantic Kernel
**Storage**: SQL Server (Dapper for DAL, future EF Core migrations for schema management only), Azure AI Search (vector store)
**Testing**: xUnit, Moq
**Target Platform**: Azure Container Apps (Linux), Windows desktop (.NET MAUI admin app)
**Project Type**: Multi-project clean architecture (8 layers)
**Performance Goals**: <500ms p95 for API endpoints, <100ms average for DB queries
**Constraints**: Must maintain working tests during refactoring, zero downtime migration
**Scale/Scope**: 8 layer projects, ~100+ source files affected, 16 duplicate model files to consolidate

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### ✅ **Security (NON-NEGOTIABLE)** - PASS
- No secrets in source control violations detected
- Existing code uses environment variables and Azure Key Vault pattern
- Input validation and authorization present in Presentation layer

### 🔴 **Clean Architecture** - **FAIL - BLOCKING**
Critical violations that MUST be fixed:

1. **Dependency Rule Violation** (CRITICAL)
   - ❌ Persistence → Application circular dependency (4-Persistence/MotorcycleRAG.Persistence.csproj:4)
   - **Impact**: Violates fundamental "dependencies point inward" rule
   - **Required**: Remove Application reference from Persistence project

2. **Framework Dependencies in Domain** (CRITICAL)
   - ❌ Microsoft.AspNetCore.Http in Contracts project (3-Domain/MotorcycleRAG.Contracts.csproj:10)
   - **Impact**: Domain layer coupled to ASP.NET framework
   - **Required**: Remove framework dependency, use primitives only

3. **Azure SDK in Application Layer** (CRITICAL)
   - ❌ Azure.AI.OpenAI, Azure.Search.Documents, Azure.AI.DocumentIntelligence in Application (2-Application/MotorcycleRAG.Application.csproj:9-12)
   - ❌ Microsoft.Extensions.Caching.StackExchangeRedis in Application (2-Application/MotorcycleRAG.Application.csproj:19)
   - **Impact**: Application layer coupled to Azure infrastructure
   - **Required**: Move Azure SDK usage to Persistence layer only

4. **Model Duplication** (HIGH)
   - ❌ 16 duplicate model files between Domain and Contracts
   - **Impact**: Maintenance nightmare, synchronization issues, violates DRY
   - **Required**: Consolidate to single source of truth per model

5. **Infrastructure in Domain** (HIGH)
   - ❌ 10+ configuration classes in Domain/Models (AzureAIConfiguration, ResilienceConfiguration, etc.)
   - **Impact**: Domain knows about Azure, circuit breakers, retry policies
   - **Required**: Move to Base/Shared project or Persistence

### ✅ **Code Quality** - CONDITIONAL PASS
- Existing code has zero build warnings ✓
- No placeholder code detected ✓
- **Blocker**: Refactoring MUST maintain zero warnings
- **Blocker**: All tests must pass after each phase

### ✅ **Testing** - PASS
- 80%+ coverage exists in current codebase ✓
- Unit tests use proper mocking ✓
- Integration tests exist ✓
- **Required**: Maintain test coverage during refactoring

### ✅ **Observability** - PASS
- Structured logging with ILogger<T> ✓
- Application Insights integration ✓
- Health checks present ✓
- Correlation IDs via CorrelationService ✓

### ✅ **Resilience** - PASS
- Polly patterns implemented ✓
- Circuit breakers configured ✓
- Retry policies present ✓
- **Note**: Configuration classes misplaced but logic sound

### ✅ **Process & Workflow** - PASS
- Task tracking will use TodoWrite tool ✓
- Commit discipline: small changes with tests ✓
- PowerShell scripts for Windows environment ✓

**GATE RESULT**: **BLOCKED** - Clean Architecture violations MUST be remediated before new feature work can proceed.

## Project Structure

### Documentation (this feature)

```text
specs/001-system-spec/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0: Research best practices for refactoring
├── data-model.md        # Phase 1: Consolidated model definitions
├── quickstart.md        # Phase 1: Migration guide for developers
├── contracts/           # Phase 1: Proposed new project structure
│   ├── domain-contracts.md       # Interfaces that belong in Contracts
│   ├── shared-models.md          # Models that should be shared
│   └── migration-mapping.md      # Old → New location mapping
└── tasks.md             # Phase 2: Implementation task breakdown
```

### Source Code (repository root)

```text
# CURRENT STRUCTURE (VIOLATIONS HIGHLIGHTED)
3-Domain/
├── MotorcycleRAG.Domain/
│   └── Models/
│       ├── ❌ AzureAIConfiguration.cs           # VIOLATION: Infrastructure in Domain
│       ├── ❌ ResilienceConfiguration.cs        # VIOLATION: Infrastructure in Domain
│       ├── ❌ CircuitBreakerConfiguration.cs    # VIOLATION: Infrastructure in Domain
│       ├── ❌ RetryConfiguration.cs             # VIOLATION: Infrastructure in Domain
│       ├── ❌ FallbackConfiguration.cs          # VIOLATION: Infrastructure in Domain
│       ├── ❌ ConnectionStringsConfiguration.cs # VIOLATION: Infrastructure in Domain
│       ├── ❌ TelemetryConfiguration.cs         # VIOLATION: Infrastructure in Domain
│       ├── ❌ WebSearchConfiguration.cs         # VIOLATION: Infrastructure in Domain
│       ├── ❌ ModelConfiguration.cs             # VIOLATION: Infrastructure in Domain
│       ├── ❌ AppConfiguration.cs               # VIOLATION: Infrastructure in Domain
│       ├── ⚠️  MotorcycleDocument.cs             # DUPLICATE with Contracts
│       ├── ⚠️  UserModels.cs                     # DUPLICATE with Contracts
│       ├── ⚠️  CSVFile.cs                        # DUPLICATE with Contracts
│       ├── ⚠️  PDFDocument.cs                    # DUPLICATE with Contracts
│       └── [10+ more duplicate models]
└── MotorcycleRAG.Contracts/
    ├── ❌ Package: Microsoft.AspNetCore.Http     # VIOLATION: Framework dependency
    ├── Utilities/
    │   └── ❌ JsonSerializationConfiguration.cs  # VIOLATION: Implementation in Contracts
    ├── Options/
    │   └── ❌ SqlOptions.cs                      # VIOLATION: Infrastructure in Contracts
    ├── Optimization/
    │   └── ❌ IBatchProcessingService.cs         # VIOLATION: Contains implementation classes
    └── Models/
        ├── ⚠️  MotorcycleDocument.cs             # DUPLICATE with Domain
        ├── ⚠️  UserModels.cs                     # DUPLICATE with Domain
        └── [14+ more duplicate models]

2-Application/
├── MotorcycleRAG.Application/
    ├── ❌ Package: Azure.AI.OpenAI              # VIOLATION: Azure SDK in Application
    ├── ❌ Package: Azure.Search.Documents       # VIOLATION: Azure SDK in Application
    ├── ❌ Package: Azure.AI.DocumentIntelligence # VIOLATION: Azure SDK in Application
    ├── ❌ Package: StackExchangeRedis           # VIOLATION: Infrastructure in Application
    ├── Caching/
    │   ├── ❌ DistributedQueryCacheService.cs   # VIOLATION: Implementation in Application
    │   └── ❌ MemoryQueryCacheService.cs        # VIOLATION: Implementation in Application
    └── Optimization/
        ├── ❌ BatchProcessingService.cs         # VIOLATION: Implementation in Application
        ├── ❌ ConnectionPoolService.cs          # VIOLATION: Infrastructure in Application
        └── ❌ VectorCompressionService.cs       # VIOLATION: Infrastructure in Application

4-Persistence/
├── MotorcycleRAG.Persistence/
    └── ❌ ProjectReference: Application         # VIOLATION: Circular dependency

# TARGET STRUCTURE (POST-REMEDIATION)
0-Base/                                          # NEW: Shared kernel
├── MotorcycleRAG.Shared/
    ├── Configuration/
    │   ├── AzureAIConfiguration.cs             # MOVED from Domain
    │   ├── ResilienceConfiguration.cs          # MOVED from Domain
    │   ├── CircuitBreakerConfiguration.cs      # MOVED from Domain
    │   ├── RetryConfiguration.cs               # MOVED from Domain
    │   ├── FallbackConfiguration.cs            # MOVED from Domain
    │   ├── ConnectionStringsConfiguration.cs   # MOVED from Domain
    │   ├── TelemetryConfiguration.cs           # MOVED from Domain
    │   ├── WebSearchConfiguration.cs           # MOVED from Domain
    │   ├── ModelConfiguration.cs               # MOVED from Domain
    │   ├── AppConfiguration.cs                 # MOVED from Domain
    │   └── JsonSerializationConfiguration.cs   # MOVED from Contracts/Utilities
    ├── Options/
    │   └── SqlOptions.cs                       # MOVED from Contracts/Options
    └── Models/
        └── BatchProcessingModels.cs            # MOVED from Contracts/Optimization

3-Domain/
├── MotorcycleRAG.Domain/
│   └── Models/                                 # CONSOLIDATE: Keep domain entities only
│       ├── MotorcycleSpecification.cs         # Domain entity (keep)
│       ├── QueryPlan.cs                       # Domain entity (keep)
│       ├── TrustedSource.cs                   # Domain entity (keep)
│       ├── CircuitBreakerState.cs             # Domain entity (keep)
│       └── IngestionJob.cs                    # Domain entity (keep)
└── MotorcycleRAG.Contracts/
    ├── ✅ NO Framework Dependencies            # FIXED: Remove Microsoft.AspNetCore.Http
    ├── Interfaces/                             # KEEP: Domain interfaces
    ├── Models/                                 # CONSOLIDATE: DTOs and shared models
    │   ├── MotorcycleDocument.cs              # CONSOLIDATED (single version)
    │   ├── UserModels.cs                      # CONSOLIDATED (single version)
    │   ├── CSVFile.cs                         # CONSOLIDATED (single version)
    │   ├── PDFDocument.cs                     # CONSOLIDATED (single version)
    │   └── [other models - single version]
    └── Optimization/
        └── IBatchProcessingService.cs         # INTERFACE ONLY (remove impl classes)

2-Application/
├── MotorcycleRAG.Application/
    ├── ✅ NO Azure SDK Packages                # FIXED: Remove Azure packages
    ├── ✅ NO Infrastructure Packages           # FIXED: Remove StackExchangeRedis
    ├── Interfaces/                             # NEW: Application-defined interfaces
    │   ├── IQueryCacheService.cs              # Interface only (keep here)
    │   ├── IBatchProcessingService.cs         # Reference from Contracts
    │   └── IConnectionPoolService.cs          # Reference from Contracts
    └── [Business logic only - no implementations]

4-Persistence/
├── MotorcycleRAG.Persistence/
    ├── ✅ NO Application Reference             # FIXED: Remove circular dependency
    ├── Azure/                                  # Azure SDK usage HERE ONLY
    │   ├── [Existing Azure wrappers]
    ├── Caching/                                # MOVED from Application
    │   ├── DistributedQueryCacheService.cs
    │   └── MemoryQueryCacheService.cs
    └── Optimization/                           # MOVED from Application
        ├── BatchProcessingService.cs
        ├── ConnectionPoolService.cs
        └── VectorCompressionService.cs
```

**Structure Decision**: Create new `0-Base/MotorcycleRAG.Shared` project for cross-cutting configuration and utilities. Consolidate duplicate models into single locations (Contracts for DTOs/shared models, Domain for pure domain entities). Move all infrastructure implementations from Application to Persistence. Remove circular dependency by extracting shared interfaces to Contracts.

## Complexity Tracking

### Justified Deviations from Constitution

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Creating new `0-Base` layer | Constitution allows for Base/Shared cross-cutting concerns; configuration classes (Azure, Resilience, etc.) don't belong in Domain but are needed across layers | Putting configuration in Persistence would require Application/Presentation to reference Persistence (violates dependency rule); duplicating across layers violates DRY |
| Multi-phase refactoring plan | Codebase has 38 violations affecting ~100 files; atomic fix risks breaking working tests and deployment | Single massive commit would be unreviewable and untestable; phased approach allows validation at each step |

### Constitution Violations REQUIRING Immediate Fix

| Violation | Current Impact | Fix Required |
|-----------|----------------|--------------|
| Persistence → Application reference | Circular dependency prevents independent testing and deployment; violates fundamental CA principle | Remove `<ProjectReference Include="Application"/>` from Persistence.csproj; extract shared interfaces to Contracts |
| Microsoft.AspNetCore.Http in Contracts | Domain coupled to ASP.NET; cannot test domain without web framework | Remove package reference; replace IFormFile usage with byte[] or Stream |
| Azure SDKs in Application | Application cannot be tested without Azure clients; violates infrastructure independence | Move all Azure SDK references to Persistence; Application uses only interfaces |
| 16 duplicate models | Maintenance debt; changes require updates in 2 places; high risk of desynchronization | Consolidate each model to single location; update all references |

## Phase 0: Research & Prerequisites

### Research Tasks

1. **Best Practices for Large-Scale Refactoring**
   - Research: Safe refactoring techniques for .NET solutions with 100+ files
   - Research: Strategies for maintaining test coverage during architectural changes
   - Research: Git strategies for large refactoring PRs (branch strategy, commit granularity)
   - Output: `research.md` section on refactoring approach

2. **Model Consolidation Patterns**
   - Research: DTO vs Entity vs Value Object placement in Clean Architecture
   - Research: Shared kernel patterns in multi-project .NET solutions
   - Research: C# namespace aliasing for transitional compatibility
   - Output: `research.md` section on model placement strategy

3. **Dependency Injection Migration**
   - Research: Impact of moving implementations between projects on DI registration
   - Research: Extension method patterns for layer-specific service registration
   - Research: Testing strategies during service location changes
   - Output: `research.md` section on DI migration approach

4. **Azure SDK Isolation**
   - Research: Interface extraction patterns for Azure SDK clients
   - Research: Mock strategies for Azure services in unit tests
   - Research: Performance impact of additional abstraction layers
   - Output: `research.md` section on Azure SDK containment

### Prerequisites Checklist

- [x] Clean Architecture violation report complete (38 violations identified)
- [ ] Research.md complete with all 4 research sections
- [ ] Constitution Check re-evaluated (must pass after research)
- [ ] Stakeholder approval for multi-phase refactoring plan
- [ ] Development branch created: `001-system-spec`
- [ ] Baseline test suite executed and passing (100% pass rate)
- [ ] Backup/rollback strategy documented

## Phase 1: Design & Contracts

### Deliverables

1. **data-model.md**: Consolidated model definitions
   - Section 1: Domain Entities (pure business logic, stay in Domain)
   - Section 2: Shared Models/DTOs (cross-layer, move to Contracts)
   - Section 3: Configuration Models (infrastructure, move to Base/Shared)
   - Section 4: Migration mapping (old location → new location)

2. **contracts/**: New project structure specifications
   - `domain-contracts.md`: Interfaces that belong in MotorcycleRAG.Contracts
   - `shared-models.md`: Models that should be in MotorcycleRAG.Contracts/Models
   - `base-shared.md`: Configuration and utilities for new MotorcycleRAG.Shared project
   - `migration-mapping.md`: Complete file-by-file migration map

3. **quickstart.md**: Developer migration guide
   - How to update project references
   - How to update using statements
   - How to update DI registration
   - How to run tests after migration
   - Troubleshooting common issues

### Design Decisions (To Be Made in Phase 1)

1. **Model Consolidation Strategy**
   - For each duplicate model pair, decide: Keep Domain version OR Contracts version?
   - Decision criteria: Is it a pure domain entity (Domain) or DTO/shared model (Contracts)?
   - Document rationale for each decision

2. **Interface Placement**
   - Which interfaces stay in Contracts (domain contracts)?
   - Which interfaces move to Application/Interfaces (application-specific)?
   - Which interfaces move to Shared (cross-cutting)?

3. **Configuration Model Placement**
   - All `*Configuration` classes → 0-Base/MotorcycleRAG.Shared/Configuration
   - SqlOptions → 0-Base/MotorcycleRAG.Shared/Options
   - JsonSerializationConfiguration → 0-Base/MotorcycleRAG.Shared/Utilities

4. **Service Implementation Placement**
   - DistributedQueryCacheService → 4-Persistence/Caching
   - MemoryQueryCacheService → 4-Persistence/Caching
   - BatchProcessingService → 4-Persistence/Optimization
   - ConnectionPoolService → 4-Persistence/Optimization
   - VectorCompressionService → 4-Persistence/Optimization

### Constitution Re-Check (Post-Design)

After Phase 1 design is complete:
- [ ] Verify proposed structure eliminates all circular dependencies
- [ ] Verify no framework dependencies in Domain/Contracts
- [ ] Verify Azure SDKs only in Persistence
- [ ] Verify all models consolidated (zero duplication)
- [ ] Verify all infrastructure config in Base/Shared
- [ ] Document any remaining complexity in Complexity Tracking table

## Phase 2: Task Generation (OUT OF SCOPE for /speckit.plan)

This phase is handled by the `/speckit.tasks` command and will generate:
- `tasks.md`: Detailed implementation tasks with dependency ordering
- Task breakdown for each phase of migration
- Testing checkpoints after each task
- Rollback procedures if issues arise

Tasks will be generated based on the design artifacts from Phase 1.

## Risk Assessment

### High Risks

1. **Test Breakage Risk** (HIGH)
   - Impact: ~100 unit and integration tests may break during refactoring
   - Mitigation: Run full test suite after each file move; fix immediately before proceeding
   - Rollback: Git revert to last known-good state

2. **Merge Conflict Risk** (HIGH)
   - Impact: Large-scale file moves may conflict with concurrent development
   - Mitigation: Coordinate with team; freeze feature development during refactoring; use short-lived branch
   - Rollback: Rebase strategy with conflict resolution plan

3. **Runtime Failure Risk** (MEDIUM)
   - Impact: DI registration errors may only appear at runtime, not compile time
   - Mitigation: Comprehensive integration test suite; manual smoke testing after each phase
   - Rollback: Blue-green deployment with quick rollback capability

### Medium Risks

4. **Performance Regression Risk** (MEDIUM)
   - Impact: Additional abstraction layers may impact performance
   - Mitigation: Performance test suite before/after; profile critical paths
   - Acceptance: <5% performance degradation acceptable for architectural correctness

5. **Documentation Drift Risk** (MEDIUM)
   - Impact: Existing docs may reference old file locations
   - Mitigation: Update CLAUDE.md, README, and architecture docs in same PR
   - Rollback: Version-controlled docs allow easy revert

### Low Risks

6. **Build Time Increase** (LOW)
   - Impact: Additional project may increase build time
   - Mitigation: Parallel build already enabled; impact expected <10%
   - Acceptance: Small build time increase acceptable

## Success Criteria

### Phase 0 Complete When:
- [ ] research.md contains all 4 research sections with concrete recommendations
- [ ] Constitution Check passes (all CRITICAL violations have documented fixes)
- [ ] Stakeholder sign-off obtained

### Phase 1 Complete When:
- [ ] data-model.md documents all 16 duplicate models with consolidation decisions
- [ ] contracts/ folder contains complete migration mapping
- [ ] quickstart.md provides step-by-step developer guide
- [ ] Constitution Re-Check passes (proposed structure is compliant)
- [ ] Design review approved by technical lead

### Overall Success Criteria:
- [ ] Zero circular dependencies (Persistence does NOT reference Application)
- [ ] Zero framework dependencies in Domain/Contracts
- [ ] Zero Azure SDK references in Application
- [ ] Zero duplicate models (16 consolidations complete)
- [ ] Zero infrastructure config in Domain (10+ moved to Base/Shared)
- [ ] All tests pass (100% pass rate maintained)
- [ ] Zero build warnings
- [ ] Performance tests show <5% regression
- [ ] Documentation updated and reviewed

## Next Steps

1. **Execute Phase 0**: Run research tasks and produce `research.md`
2. **Execute Phase 1**: Design model consolidation and produce artifacts
3. **Generate Tasks**: Use `/speckit.tasks` command to generate implementation plan
4. **Execute Migration**: Implement tasks in dependency order
5. **Validate**: Run full test suite and performance tests
6. **Deploy**: Merge to main branch after all validations pass

---

**Status**: Plan Created - Awaiting Phase 0 Research Execution
**Blockers**: Constitution Check FAILED - Must remediate before proceeding with feature work
**Estimated Effort**: 40-60 hours (8-12 days at 5 hours/day)
