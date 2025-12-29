# Clean Architecture Remediation Tasks

**Branch**: `001-system-spec` | **Date**: 2025-12-28  
**Related Documents**: [plan.md](./plan.md) | [migration-mapping.md](./contracts/migration-mapping.md)

## Overview

This task file tracks the **Clean Architecture remediation work** to fix critical violations in the Motorcycle RAG System. This is **separate** from feature implementation tasks to avoid mixing architectural cleanup with feature development.

**Total Tasks**: 52  
**Estimated Effort**: 40-60 hours

---

## Critical Violations Being Fixed

1. ❌ **Circular Dependency**: Persistence → Application (BLOCKING)
2. ❌ **Framework in Domain**: Microsoft.AspNetCore.Http in Contracts
3. ❌ **Azure SDK in Application**: Infrastructure types in business logic layer
4. ❌ **16 Duplicate Models**: Between Domain and Contracts
5. ❌ **10+ Configuration Classes**: Infrastructure concerns in Domain

---

## Phase 1: Setup & Verification

**Goal**: Establish clean baseline and backup for safe rollback

**Acceptance Criteria**:
- [ ] All tests pass (baseline)
- [ ] Git branch created
- [ ] Build succeeds with zero warnings

### Tasks

- [X] T001 Verify current build status: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
  - **STATUS**: ❌ FAILED with 14 compilation errors
  - **ROOT CAUSE**: Multiple duplicate configuration classes with different properties
    - Domain/Models/AzureAIConfiguration.cs (has Models property)
    - Contracts/Models/ConfigurationModels.cs/AzureAIConfiguration (NO Models property)
    - Domain/Models/ModelConfiguration.cs (has QueryPlannerModel property)
    - Contracts/Models/ConfigurationModels.cs/ModelConfiguration (NO QueryPlannerModel property)
  - **IMPACT**: Application code resolves to wrong version, causing compilation failures
- [ ] T002 Run full test suite for baseline: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln --verbosity normal`
  - **STATUS**: BLOCKED - Cannot run tests until build succeeds
- [X] T003 Document current test pass rate in remediation-tasks.md (this file)
  - **BASELINE**: Build broken, 14 compilation errors, 0% test pass rate (tests cannot run)
- [X] T004 Create backup branch: `git checkout -b 001-system-spec-backup`
- [X] T005 Switch back to working branch: `git checkout 001-system-spec`
- [X] T006 Review migration-mapping.md to understand scope
- [X] **REMEDIATION COMPLETE**: Fixed Clean Architecture by establishing correct dependency flow
  - Added Domain project reference to Contracts (Contracts → Domain)
  - Deleted 13 duplicate model files from Contracts/Models
  - Added `using MotorcycleRAG.Domain.Models;` to 24 Contracts interfaces, 25 Application files, 18 Persistence files, 39 test files
  - Fixed namespace for 3 API DTO files (UpdateProfileRequest, UsageResponse, UserProfileResponse)
  - Resolved TelemetryConfiguration ambiguous reference with fully qualified names
  - **Result**: Build succeeds with 0 errors, 0 warnings!

---

## Phase 2: Configuration Migration (Domain → Base/Options) ✅ **COMPLETE**

**Goal**: Move all `*Configuration` classes from Domain to Base/Options with `*Options` naming

**Acceptance Criteria**:
- [x] All configuration classes in `0-Base/MotorcycleRAG.Core/Options/`
- [x] Renamed to `*Options` pattern
- [x] All `IOptions<T>` bindings updated
- [x] Build succeeds
- [x] Tests pass

### Tasks

#### Create Options Directory
- [x] T007 Create directory `C:\git\motorcycle-rag-system\0-Base\MotorcycleRAG.Core\Options` (COMPLETED)

#### Move & Rename Configuration Classes
- [x] T008 [P] Move AzureAIConfiguration → 0-Base/MotorcycleRAG.Core/Options/AzureAIOptions.cs (COMPLETED)
- [x] T009 [P] Move ResilienceConfiguration → 0-Base/MotorcycleRAG.Core/Options/ResilienceOptions.cs (COMPLETED)
- [x] T010 [P] Move CircuitBreakerConfiguration → 0-Base/MotorcycleRAG.Core/Options/CircuitBreakerOptions.cs (COMPLETED)
- [x] T011 [P] Move RetryConfiguration → 0-Base/MotorcycleRAG.Core/Options/RetryOptions.cs (COMPLETED)
- [x] T012 [P] Move FallbackConfiguration → 0-Base/MotorcycleRAG.Core/Options/FallbackOptions.cs (COMPLETED)
- [x] T013 [P] Move ConnectionStringsConfiguration → 0-Base/MotorcycleRAG.Core/Options/ConnectionStringsOptions.cs (COMPLETED)
- [x] T014 [P] Move TelemetryConfiguration → 0-Base/MotorcycleRAG.Core/Options/TelemetryOptions.cs (COMPLETED)
- [x] T015 [P] Move ModelConfiguration → 0-Base/MotorcycleRAG.Core/Options/ModelOptions.cs (COMPLETED)
- [x] T016 [P] Move WebSearchConfiguration → 0-Base/MotorcycleRAG.Core/Options/WebSearchOptions.cs (COMPLETED)
- [x] T017 [P] Move AppConfiguration → 0-Base/MotorcycleRAG.Core/Options/AppOptions.cs (COMPLETED)
- [x] T018 Move SearchConfiguration → 0-Base/MotorcycleRAG.Core/Options/SearchOptions.cs (COMPLETED)

#### Update References
- [x] T019 Updated all `using` references to `using MotorcycleRAG.Core.Options;` (COMPLETED)
- [x] T020 Updated IOptions<*Configuration> bindings in Program.cs (COMPLETED)
- [x] T021 Updated IOptions<*Configuration> bindings in ServiceConfiguration.cs (COMPLETED)
- [x] T022 Updated IOptions<*Configuration> bindings in Application ServiceCollectionExtensions.cs (COMPLETED)
- [x] T023 Updated IOptions<*Configuration> bindings in Persistence ServiceCollectionExtensions.cs (COMPLETED)

#### Verify
- [x] T024 Build solution: ✅ **PASSING** (0 errors, 2 warnings in test project only)
- [x] T025 Run tests: ✅ **PASSING** (329/329 unit tests pass)
- [x] T026 Commit Phase 2 changes: ✅ **COMMITTED**

**PHASE 2 COMPLETION**: ✅ **100% COMPLETE - 19/19 tasks**

---

## Phase 3: Model Organization (Domain Entities, DTOs to Contracts)

**Architectural Clarification**:
- **ONLY interfaces belong in Contracts** - No model files should be in Contracts/Models/
- Move any DTOs/data classes from Contracts/Models/ → Domain/Models/DTOs/
- Classify each file in Domain/Models/ as Entity (has logic) or DTO (data transfer only)
- Entities with logic stay in Domain, DTOs get organized appropriately

**Goal**:
1. Move any existing models from Contracts/Models/ to Domain
2. Classify each Domain/Models/ file as Entities or DTO
3. Organize DTOs into Domain/DTOs/ subfolder and Entities into Domain/Entities/
4. Ensure Contracts contains ONLY interfaces

**Acceptance Criteria**:
- [x] Contracts/Models/ folder contains NO model classes (only interfaces in Contracts)
- [ ] All DTOs moved to Domain/DTOs/ or other appropriate Domain subfolder
- [ ] All entities mopve to Domain/Entities with business logic intact
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Update References First (Safety)
- [ ] T027 Search all `using MotorcycleRAG.Contracts.Models;` statements and replace with `using MotorcycleRAG.Domain.DTO;` or using MotorcycleRAG.Domain.entities;` where duplicates are used
- [ ] T028 Verify no compilation errors after namespace changes: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`

#### Verify
- [ ] T043 Build solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T044 Run tests: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T045 Commit Phase 3 changes with message: "refactor: Remove duplicate models from Domain layer"

---
