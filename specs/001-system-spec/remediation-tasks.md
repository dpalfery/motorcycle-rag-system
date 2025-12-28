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

- [ ] T001 Verify current build status: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T002 Run full test suite for baseline: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln --verbosity normal`
- [ ] T003 Document current test pass rate in remediation-tasks.md (this file)
- [ ] T004 Create backup branch: `git checkout -b 001-system-spec-backup`
- [ ] T005 Switch back to working branch: `git checkout 001-system-spec`
- [ ] T006 Review migration-mapping.md to understand scope

---

## Phase 2: Configuration Migration (Domain → Contracts/Options)

**Goal**: Move all `*Configuration` classes from Domain to Contracts/Options with `*Options` naming

**Acceptance Criteria**:
- [ ] All configuration classes in `Contracts/Options/`
- [ ] Renamed to `*Options` pattern
- [ ] All `IOptions<T>` bindings updated
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Create Options Directory
- [ ] T007 Create directory `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options` (if not exists)

#### Move & Rename Configuration Classes
- [ ] T008 [P] Move AzureAIConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\AzureAIConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\AzureAIOptions.cs` (rename class and namespace)
- [ ] T009 [P] Move ResilienceConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\ResilienceConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\ResilienceOptions.cs` (rename class and namespace)
- [ ] T010 [P] Move CircuitBreakerConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\CircuitBreakerConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\CircuitBreakerOptions.cs` (rename class and namespace)
- [ ] T011 [P] Move RetryConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\RetryConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\RetryOptions.cs` (rename class and namespace)
- [ ] T012 [P] Move FallbackConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\FallbackConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\FallbackOptions.cs` (rename class and namespace)
- [ ] T013 [P] Move ConnectionStringsConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\ConnectionStringsConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\ConnectionStringsOptions.cs` (rename class and namespace)
- [ ] T014 [P] Move TelemetryConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\TelemetryConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\TelemetryOptions.cs` (rename class and namespace)
- [ ] T015 [P] Move ModelConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\ModelConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\ModelOptions.cs` (rename class and namespace)
- [ ] T016 [P] Move WebSearchConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\WebSearchConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\WebSearchOptions.cs` (rename class and namespace)
- [ ] T017 [P] Move AppConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\AppConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\AppOptions.cs` (rename class and namespace)
- [ ] T018 Move and rename SearchConfiguration from `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\SearchConfiguration.cs` to `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Options\SearchOptions.cs` (also delete duplicate in Contracts/Models/)

#### Update References
- [ ] T019 Search and replace all `using MotorcycleRAG.Domain.Models;` references to configuration classes with `using MotorcycleRAG.Contracts.Options;` across solution
- [ ] T020 Update all `IOptions<*Configuration>` bindings to `IOptions<*Options>` in `C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Program.cs`
- [ ] T021 Update all `IOptions<*Configuration>` bindings to `IOptions<*Options>` in `C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API\Configuration\ServiceConfiguration.cs`
- [ ] T022 Update all `IOptions<*Configuration>` bindings to `IOptions<*Options>` in `C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Extensions\ServiceCollectionExtensions.cs`
- [ ] T023 Update all `IOptions<*Configuration>` bindings to `IOptions<*Options>` in `C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\Azure\ServiceCollectionExtensions.cs`

#### Verify
- [ ] T024 Build solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T025 Run tests: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T026 Commit Phase 2 changes with message: "refactor: Move configuration classes to Contracts/Options"

---

## Phase 3: Duplicate Model Cleanup (Remove from Domain)

**Goal**: Delete duplicate models from Domain (Contracts versions are authoritative)

**Acceptance Criteria**:
- [ ] 13 duplicate models removed from Domain
- [ ] All references point to Contracts versions
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Update References First (Safety)
- [ ] T027 Search all `using MotorcycleRAG.Domain.Models;` statements and replace with `using MotorcycleRAG.Contracts.Models;` where duplicates are used
- [ ] T028 Verify no compilation errors after namespace changes: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`

#### Delete Domain Duplicates
- [ ] T029 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\AuditModels.cs`
- [ ] T030 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\CSVFile.cs`
- [ ] T031 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\FileUploadModels.cs`
- [ ] T032 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\IndexingModels.cs`
- [ ] T033 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\MonitoringModels.cs`
- [ ] T034 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\PDFDocument.cs`
- [ ] T035 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\PipelineModels.cs`
- [ ] T036 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\ProcessingModels.cs`
- [ ] T037 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\QueryModels.cs`
- [ ] T038 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\SearchResult.cs`
- [ ] T039 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\UsageModels.cs`
- [ ] T040 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\UserModels.cs`
- [ ] T041 [P] Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\WebSourceModels.cs`
- [ ] T042 Delete `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\Models\SearchConfiguration.cs` (moved to Options in Phase 2)

#### Verify
- [ ] T043 Build solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T044 Run tests: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T045 Commit Phase 3 changes with message: "refactor: Remove duplicate models from Domain layer"

---

## Phase 4: Interface Migration (Application → Contracts)

**Goal**: Move optimization service interfaces to Contracts/Optimization/

**Acceptance Criteria**:
- [ ] 3 interfaces created in Contracts/Optimization/
- [ ] References updated in implementations
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Create Optimization Directory in Contracts
- [ ] T046 Create directory `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Optimization` (if not exists)

#### Recreate Deleted Interfaces
- [ ] T047 Create `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Optimization\IBatchProcessingService.cs` with correct namespace
- [ ] T048 Create `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Optimization\IConnectionPoolService.cs` with correct namespace
- [ ] T049 Create `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Optimization\IVectorCompressionService.cs` with correct namespace

#### Update Implementation References
- [ ] T050 Update `C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Optimization\BatchProcessingService.cs` to use `using MotorcycleRAG.Contracts.Optimization;`
- [ ] T051 Update `C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Optimization\ConnectionPoolService.cs` to use `using MotorcycleRAG.Contracts.Optimization;`
- [ ] T052 Update `C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\Optimization\VectorCompressionService.cs` to use `using MotorcycleRAG.Contracts.Optimization;`

#### Verify
- [ ] T053 Build solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T054 Run tests: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T055 Commit Phase 4 changes with message: "refactor: Move optimization interfaces to Contracts layer"

---

## Phase 5: Special Cases & TypeForwarders

**Goal**: Handle MotorcycleDocument rename and add TypeForwarders for binary compatibility

**Acceptance Criteria**:
- [ ] Contracts version renamed to MotorcycleIndexDocument
- [ ] TypeForwarders.cs created with all mappings
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Rename MotorcycleDocument in Contracts
- [ ] T056 Rename class in `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\Models\MotorcycleDocument.cs` to `MotorcycleIndexDocument`
- [ ] T057 Update all references to `MotorcycleDocument` in Persistence layer to use `MotorcycleIndexDocument` from Contracts
- [ ] T058 Update indexing service in `C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\Search\MotorcycleIndexingService.cs` to use renamed model
- [ ] T059 Verify Domain version of `MotorcycleDocument.cs` still exists for domain logic use

#### Create TypeForwarders
- [ ] T060 Create `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Domain\TypeForwarders.cs` with assembly-level TypeForwardedTo attributes
- [ ] T061 Add TypeForwarder for AuditModels → Contracts.Models.AuditModels
- [ ] T062 Add TypeForwarder for CSVFile → Contracts.Models.CSVFile
- [ ] T063 Add TypeForwarder for all 13 moved model types (see migration-mapping.md)
- [ ] T064 Add TypeForwarder for all 10 configuration classes → Contracts.Options.*Options

#### Verify
- [ ] T065 Build solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T066 Run tests: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T067 Commit Phase 5 changes with message: "refactor: Add TypeForwarders and rename MotorcycleIndexDocument"

---

## Phase 6: Dependency Cleanup

**Goal**: Remove circular dependency (Persistence → Application) and framework dependencies

**Acceptance Criteria**:
- [ ] Persistence project does NOT reference Application
- [ ] Contracts project does NOT reference ASP.NET Core
- [ ] Application project does NOT reference Azure SDKs
- [ ] Build succeeds
- [ ] Tests pass

### Tasks

#### Remove Circular Dependency
- [ ] T068 Open `C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj`
- [ ] T069 Remove `<ProjectReference Include="..\..\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj" />` if exists
- [ ] T070 Identify any classes in Persistence using Application types, refactor to use Contracts interfaces only
- [ ] T071 Build Persistence project: `dotnet build C:\git\motorcycle-rag-system\4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj`

#### Remove Framework Dependency from Contracts
- [ ] T072 Open `C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\MotorcycleRAG.Contracts.csproj`
- [ ] T073 Remove `<PackageReference Include="Microsoft.AspNetCore.Http" />` if exists
- [ ] T074 Search for `IFormFile` usage in Contracts, replace with `Stream` + metadata parameters
- [ ] T075 Build Contracts project: `dotnet build C:\git\motorcycle-rag-system\3-Domain\MotorcycleRAG.Contracts\MotorcycleRAG.Contracts.csproj`

#### Remove Azure SDK from Application
- [ ] T076 Open `C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj`
- [ ] T077 Remove Azure.AI.OpenAI package reference (move usage to Persistence wrappers via interfaces)
- [ ] T078 Remove Azure.Search.Documents package reference (move usage to Persistence wrappers via interfaces)
- [ ] T079 Remove Azure.AI.DocumentIntelligence package reference (move usage to Persistence wrappers via interfaces)
- [ ] T080 Remove Microsoft.Extensions.Caching.StackExchangeRedis package reference (move to Persistence)
- [ ] T081 Update all Application services to use only Contracts interfaces (IAzureOpenAIClient, IAzureSearchClient, etc.)
- [ ] T082 Build Application project: `dotnet build C:\git\motorcycle-rag-system\2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj`

#### Verify
- [ ] T083 Build entire solution: `dotnet build C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T084 Verify zero warnings
- [ ] T085 Run full test suite: `dotnet test C:\git\motorcycle-rag-system\MotorcycleRAG.sln`
- [ ] T086 Commit Phase 6 changes with message: "refactor: Remove circular dependencies and framework violations"

---

## Phase 7: Final Verification & Documentation

**Goal**: Comprehensive validation of remediation success

**Acceptance Criteria**:
- [ ] All 38 violations resolved
- [ ] Zero build warnings
- [ ] 100% test pass rate (matching baseline)
- [ ] Documentation updated
- [ ] Constitution Check passes

### Tasks

#### Testing
- [ ] T087 Run all unit tests: `dotnet test C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.UnitTests\MotorcycleRAG.UnitTests.csproj --verbosity detailed`
- [ ] T088 Run all integration tests: `dotnet test C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.IntegrationTests\MotorcycleRAG.IntegrationTests.csproj --verbosity detailed`
- [ ] T089 Run performance tests: `dotnet test C:\git\motorcycle-rag-system\5-Test\tests\MotorcycleRAG.PerformanceTests\MotorcycleRAG.PerformanceTests.csproj`
- [ ] T090 Compare test pass rate to baseline (from T003)

#### Architecture Validation
- [ ] T091 Verify Persistence does NOT reference Application: Check `4-Persistence\MotorcycleRAG.Persistence\MotorcycleRAG.Persistence.csproj`
- [ ] T092 Verify Contracts does NOT reference ASP.NET: Check `3-Domain\MotorcycleRAG.Contracts\MotorcycleRAG.Contracts.csproj`
- [ ] T093 Verify Application does NOT reference Azure SDKs: Check `2-Application\MotorcycleRAG.Application\MotorcycleRAG.Application.csproj`
- [ ] T094 Verify no duplicate models exist in Domain: Check `3-Domain\MotorcycleRAG.Domain\Models\` directory
- [ ] T095 Verify all configuration classes in Options: Check `3-Domain\MotorcycleRAG.Contracts\Options\` directory

#### Documentation
- [ ] T096 Update migration-mapping.md with final status (all tasks marked complete)
- [ ] T097 Update plan.md Constitution Check section to PASS
- [ ] T098 Update CLAUDE.md if architecture patterns changed
- [ ] T099 Update AGENTS.md with final folder structure

#### Git & Cleanup
- [ ] T100 Run `git status` to review all changes
- [ ] T101 Run `git diff --cached` to review staged changes for secrets
- [ ] T102 Create final commit: `git commit -m "refactor: Complete Clean Architecture remediation\n\nResolves all 38 critical violations:\n- Removed circular dependency (Persistence → Application)\n- Moved configuration to Contracts/Options\n- Eliminated 16 duplicate models\n- Removed framework dependencies from Domain\n- Removed Azure SDKs from Application\n\nCo-authored-by: factory-droid[bot] <138933559+factory-droid[bot]@users.noreply.github.com>"`
- [ ] T103 Run final smoke test on API: `dotnet run --project C:\git\motorcycle-rag-system\1-Presentation\MotorcycleRAG.API` and test /health endpoint

---

## Risk Mitigation

### If Tests Fail
1. **Don't panic** - Review the test failure output carefully
2. **Identify the layer** - Is it Presentation, Application, Persistence, or Domain?
3. **Check namespaces** - Most failures will be `using` statement issues
4. **Check DI registration** - IOptions<T> bindings may need updates
5. **Rollback if needed** - `git checkout 001-system-spec-backup` for clean state

### If Build Fails
1. **Read the error** - Compiler will tell you exactly what's missing
2. **Check project references** - Verify .csproj files have correct references
3. **Check namespaces** - Ensure all moved classes have correct namespace declarations
4. **Clean and rebuild** - `dotnet clean && dotnet build`

### If Runtime Errors Occur
1. **Check DI registration** - All interfaces must be registered in ServiceCollectionExtensions
2. **Check IOptions<T>** - Configuration binding must use new *Options names
3. **Check TypeForwarders** - Ensure all moved types have forwarders
4. **Add logging** - Temporary logging can help identify runtime issues

---

## Success Metrics

- [ ] **Zero** circular dependencies
- [ ] **Zero** framework dependencies in Domain/Contracts
- [ ] **Zero** Azure SDK references in Application
- [ ] **Zero** duplicate models (except intentional MotorcycleDocument/MotorcycleIndexDocument)
- [ ] **Zero** infrastructure config in Domain
- [ ] **Zero** build warnings
- [ ] **100%** test pass rate (matching baseline)
- [ ] **<5%** performance regression

---

## Completion Checklist

- [ ] Phase 1: Setup & Verification (6 tasks)
- [ ] Phase 2: Configuration Migration (19 tasks)
- [ ] Phase 3: Duplicate Model Cleanup (17 tasks)
- [ ] Phase 4: Interface Migration (9 tasks)
- [ ] Phase 5: Special Cases & TypeForwarders (12 tasks)
- [ ] Phase 6: Dependency Cleanup (19 tasks)
- [ ] Phase 7: Final Verification (17 tasks)

**Total Progress**: 0/103 tasks complete

---

**Status**: Ready for Execution  
**Next Step**: Begin with Phase 1, Task T001 (Verify current build status)  
**Estimated Time**: 40-60 hours (8-12 days at 5 hours/day)
