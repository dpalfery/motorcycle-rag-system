# Tasks: User Onboarding Approval

**Input**: Design documents from `/specs/002-user-onboarding-approval/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED by the constitution. Include test tasks for each user story (unit/integration/Admin app as applicable).

**Approval Gates**: This feature changes cross-cutting identity lifecycle behavior. Explicit user approval must be recorded before implementation begins.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g. US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Capture the required approvals and establish the implementation baseline for the active feature.

- [x] T001 Record explicit user approval for the cross-cutting onboarding and identity changes before coding begins, referencing `specs/002-user-onboarding-approval/spec.md` and `specs/002-user-onboarding-approval/plan.md`
- [x] T002 Review and lock the canonical tier-to-plan-to-role mapping in `6-Docs/auth-design.md`, `6-Docs/entra-setup.md`, `7-Deployment/DbSetup/sql/test-data.sql`, and `specs/002-user-onboarding-approval/research.md` so implementation uses one approved entitlement source

**Recorded approval**: 2026-04-24 user request to run `/speckit.implement` for `002-user-onboarding-approval`, referencing `specs/002-user-onboarding-approval/spec.md` and `specs/002-user-onboarding-approval/plan.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core contracts, persistence, identity mapping, telemetry, and validation scaffolding that MUST exist before any user story is implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 [P] Create shared onboarding and management DTO contracts in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/CreateAccessRequestRequest.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/PublicAccessRequestResponse.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/UserManagementRow.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/UserManagementListResponse.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/ApproveAccessRequestRequest.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/ChangeManagedUserTierRequest.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/CancelAccessRequestRequest.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/CancelManagedUserRequest.cs`, and `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/AdminActionResponse.cs`
- [x] T004 [P] Create lifecycle and identity enums in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/RequestDecisionState.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/OnboardingExecutionState.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/ManagedUserAccessState.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/UserManagementRowState.cs`, `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IdentityProvider.cs`, and `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/TierLabel.cs`
- [x] T005 [P] Add repository and provisioning contracts in `3-Domain/MotorcycleRAG.Contracts/Interfaces/IAccessRequestRepository.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserIdentityRepository.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserManagementQueryRepository.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IExternalIdentityProvisioningService.cs`, and `3-Domain/MotorcycleRAG.Contracts/Interfaces/IApproverNotificationService.cs`
- [ ] T006 Extend shared identity and admin-service contracts in `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserRepository.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserAdminService.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IUserProvisioningService.cs`, and `3-Domain/MotorcycleRAG.Contracts/Interfaces/ICurrentUserService.cs` for internal user lookup, tier changes, cancellation, and approval-time onboarding
- [x] T007 Add SQL schema and migration support for `AccessRequests`, `UserIdentities`, onboarding-attempt tracking, access-cancellation metadata, and optimistic concurrency in `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql` and `4-Persistence/MotorcycleRAG.Persistence/Sql/Migrations/UserOnboardingApprovalMigration.sql`
- [x] T008 [P] Implement persistence repositories in `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/AccessRequestRepository.cs`, `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/UserIdentityRepository.cs`, and `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/UserManagementQueryRepository.cs`
- [ ] T009 Update shared repositories in `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/UserRepository.cs`, `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/UsageRepository.cs`, and `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/PlanRepository.cs` to support internal user IDs, tier mapping, usage seeding, tier moves, and access cancellation
- [x] T010 Implement the central onboarding, tier-mapping, and access-lifecycle application services in `2-Application/MotorcycleRAG.Application/Services/AccessRequestService.cs`, `2-Application/MotorcycleRAG.Application/Services/AccessRequestAdminService.cs`, `2-Application/MotorcycleRAG.Application/Services/TierEntitlementMappingService.cs`, and `2-Application/MotorcycleRAG.Application/Services/UserAccessLifecycleService.cs`
- [x] T011 Implement current-user identity-link resolution and approval-first reconciliation updates in `2-Application/MotorcycleRAG.Application/Services/UserProvisioningService.cs` and `1-Presentation/MotorcycleRAG.API/Services/CurrentUserService.cs`
- [ ] T012 Wire DI, rate limiting, authorization, and secure configuration for the new onboarding services in `1-Presentation/MotorcycleRAG.API/Configuration/Services/CoreServicesConfiguration.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/Services/PersistenceConfiguration.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/Services/RateLimitingServiceConfiguration.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/Services/AuthorizationPoliciesConfiguration.cs`, and `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/AuthenticationServiceConfiguration.cs`
- [ ] T013 Add foundational unit tests for tier mapping, lifecycle transitions, concurrency guards, identity-link lookup, and the required claims contract in `5-Test/tests/MotorcycleRAG.UnitTests/Services/AccessRequestServiceTests.cs`, `5-Test/tests/MotorcycleRAG.UnitTests/Services/TierEntitlementMappingServiceTests.cs`, `5-Test/tests/MotorcycleRAG.UnitTests/Services/UserAccessLifecycleServiceTests.cs`, and `5-Test/tests/MotorcycleRAG.UnitTests/Presentation/API/Services/CurrentUserServiceTests.cs`
- [ ] T014 Implement onboarding telemetry, latency metrics, and dependency health signaling in `1-Presentation/MotorcycleRAG.API/Program.cs`, `1-Presentation/MotorcycleRAG.API/Extensions/LoggingExtensions.cs`, and `4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs`
- [x] T015 Define the local validation path for this feature in `specs/002-user-onboarding-approval/quickstart.md` and ensure no task depends on direct cloud mutation or non-GitHub-Actions deployment behavior

**Checkpoint**: Foundation ready, the state model is stable, and user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Request Access From Login (Priority: P1) 🎯 MVP

**Goal**: Allow an unauthenticated visitor to submit a deduplicated access request from the login page using a Microsoft or Google account email and have that request routed to the approver.

**Independent Test**: Submit a new access request from the login page, verify it is persisted, deduplicated by `provider + email`, and reflected through the public API response and admin management list without requiring any admin action.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T016 [P] [US1] Add API integration tests for public access-request submission, duplicate pending requests, and provider-specific deduplication in `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AccessRequestApiIntegrationTests.cs`
- [x] T017 [P] [US1] Add application and persistence unit tests for request validation and duplicate handling in `5-Test/tests/MotorcycleRAG.UnitTests/Services/AccessRequestValidationTests.cs`

### Implementation for User Story 1

- [x] T018 [P] [US1] Create public access-request models for the WebUI in `1-Presentation/MotorcycleRag.WebUI/src/types/accessRequests.ts`
- [x] T019 [US1] Add login-page access-request UI and status handling in `1-Presentation/MotorcycleRag.WebUI/src/pages/LoginPage.tsx`
- [x] T020 [US1] Add WebUI request submission client logic in `1-Presentation/MotorcycleRag.WebUI/src/lib/accessRequests.ts` and update `1-Presentation/MotorcycleRag.WebUI/src/contexts/AuthContext.tsx` only as needed to preserve the existing sign-in flow
- [x] T021 [US1] Add the anonymous access-request API endpoint in `1-Presentation/MotorcycleRAG.API/Controllers/AccessRequestsController.cs`
- [x] T022 [US1] Implement request persistence, deduplication, and notification dispatch in `2-Application/MotorcycleRAG.Application/Services/AccessRequestService.cs`
- [x] T023 [US1] Implement approver notification delivery in `4-Persistence/MotorcycleRAG.Persistence/Notifications/ApproverNotificationService.cs`
- [ ] T024 [US1] Register the new controller, request throttling behavior, and SC-001 timing telemetry in `1-Presentation/MotorcycleRAG.API/Program.cs` and `1-Presentation/MotorcycleRAG.API/Configuration/Services/RateLimitingServiceConfiguration.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Manage Pending And Existing Users In The Admin App (Priority: P2)

**Goal**: Allow an authorized admin to use one management table to review pending requests and existing users, approve requests, change tiers, retry failed onboarding, and cancel pending or existing rows.

**Independent Test**: Open the Admin app as an authorized admin user, load the unified user-management table, approve a pending request, change the tier of an existing user, cancel a pending request, and cancel an existing user while verifying row state changes correctly.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add admin API integration tests for the unified management list, approve, tier-change, cancel-pending, cancel-existing-user, and authorization failure cases in `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AdminUserManagementIntegrationTests.cs`
- [x] T026 [P] [US2] Add Admin app viewmodel tests for loading unified rows, enabling allowed actions, changing tiers, retrying onboarding, and cancelling rows in `5-Test/MotorcycleRAG.Admin.Tests/ViewModels/UserManagementViewModelTests.cs`

### Implementation for User Story 2

- [x] T027 [P] [US2] Add Admin app DTOs for unified management rows and admin actions in `1-Presentation/MotorcycleRAG.Admin/Services/Dtos/UserManagementRowDto.cs`, `1-Presentation/MotorcycleRAG.Admin/Services/Dtos/UserManagementListResponseDto.cs`, `1-Presentation/MotorcycleRAG.Admin/Services/Dtos/ApproveAccessRequestDto.cs`, `1-Presentation/MotorcycleRAG.Admin/Services/Dtos/ChangeManagedUserTierDto.cs`, and `1-Presentation/MotorcycleRAG.Admin/Services/Dtos/CancelManagementItemDto.cs`
- [x] T028 [US2] Add admin management-list and request-action API endpoints in `1-Presentation/MotorcycleRAG.API/Controllers/AccessRequestsAdminController.cs`
- [x] T029 [US2] Add admin managed-user action endpoints for tier changes and existing-user cancellation in `1-Presentation/MotorcycleRAG.API/Controllers/UsersAdminController.cs`
- [x] T030 [US2] Implement admin review, approval, and cancellation application logic in `2-Application/MotorcycleRAG.Application/Services/AccessRequestAdminService.cs` and update `2-Application/MotorcycleRAG.Application/Services/UserAdminService.cs` for tier-change and existing-user cancellation flows
- [x] T031 [US2] Extend the Admin API client for management-list, approve, tier-change, retry, and cancel actions in `1-Presentation/MotorcycleRAG.Admin/Services/ApiClient.cs`
- [x] T032 [P] [US2] Create the Admin app user-management viewmodel and item models in `1-Presentation/MotorcycleRAG.Admin/ViewModels/UserManagementViewModel.cs` and `1-Presentation/MotorcycleRAG.Admin/ViewModels/UserManagementRowViewModel.cs`
- [x] T033 [US2] Create the Admin app user-management page in `1-Presentation/MotorcycleRAG.Admin/Pages/UserManagementPage.xaml` and `1-Presentation/MotorcycleRAG.Admin/Pages/UserManagementPage.xaml.cs`
- [x] T034 [US2] Add navigation and shell registration for the new management page in `1-Presentation/MotorcycleRAG.Admin/AppShell.xaml`, `1-Presentation/MotorcycleRAG.Admin/AppShell.xaml.cs`, and `1-Presentation/MotorcycleRAG.Admin/Services/NavigationService.cs`

**Checkpoint**: At this point, User Stories 1 and 2 should both work independently

---

## Phase 5: User Story 3 - Complete Onboarding And Enforce Access State (Priority: P3)

**Goal**: Complete approval-time onboarding by creating the internal managed user, seeding usage tracking, provisioning the Entra external identity and app access, enforcing provider-aligned first sign-in, and propagating later tier changes or cancellations correctly.

**Independent Test**: Approve a request, verify the managed user, identity link, usage seed, and mapped role assignment are created, then complete first sign-in with the selected provider and confirm protected access resolves the correct internal user and assigned tier. Verify later tier changes and cancellations propagate to protected access correctly.

### Tests for User Story 3

- [x] T035 [P] [US3] Add unit tests for approval-time onboarding orchestration, retry behavior, tier reassignment, and existing-user cancellation in `5-Test/tests/MotorcycleRAG.UnitTests/Services/ApprovalOnboardingServiceTests.cs`
- [ ] T036 [P] [US3] Add integration tests covering approval-to-completed onboarding state, tier changes, and existing-user cancellation in `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/ApprovedUserOnboardingIntegrationTests.cs`
- [ ] T037 [P] [US3] Add integration tests for `/api/me`, motorcycle-access paths, provider-mismatch blocking, and the required claims contract using identity-link resolution in `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MeApiIntegrationTests.cs` and `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MotorcycleApiIntegrationTests.cs`

### Implementation for User Story 3

- [x] T038 [P] [US3] Implement approval-time onboarding orchestration and entitlement reconciliation in `2-Application/MotorcycleRAG.Application/Services/ApprovalOnboardingService.cs`
- [ ] T039 [P] [US3] Implement external identity provisioning, app-role assignment, and revocation in `4-Persistence/MotorcycleRAG.Persistence/Azure/ExternalIdentityProvisioningService.cs`
- [x] T040 [US3] Update request approval, retry, and request-cancellation persistence states in `2-Application/MotorcycleRAG.Application/Services/AccessRequestAdminService.cs` and `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/AccessRequestRepository.cs`
- [x] T041 [US3] Create the initial usage-tracking seed behavior and protect it from duplicate retries in `2-Application/MotorcycleRAG.Application/Services/UsageTrackingService.cs` and `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/UsageRepository.cs`
- [x] T042 [US3] Complete internal-user lookup refactoring and cancelled-user blocking across protected API surfaces in `1-Presentation/MotorcycleRAG.API/Services/CurrentUserService.cs`, `1-Presentation/MotorcycleRAG.API/Controllers/MeController.cs`, and `1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs`
- [x] T043 [US3] Add admin retry, tier-change reconciliation, and cancellation handling in `1-Presentation/MotorcycleRAG.API/Controllers/AccessRequestsAdminController.cs`, `1-Presentation/MotorcycleRAG.API/Controllers/UsersAdminController.cs`, and `2-Application/MotorcycleRAG.Application/Services/UserAccessLifecycleService.cs`
- [x] T044 [US3] Ensure BFF and API sign-in flow respects the new approval-first and provider-matching model in `1-Presentation/MotorcycleRag.WebUI.BFF/Controllers/AuthController.cs`, `1-Presentation/MotorcycleRag.WebUI.BFF/Configuration/Services/AuthenticationServiceConfiguration.cs`, and `2-Application/MotorcycleRAG.Application/Services/UserProvisioningService.cs`

**Checkpoint**: All user stories should now be independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T045 [P] Add structured onboarding and admin-action telemetry, latency metrics, and degraded dependency logging in `2-Application/MotorcycleRAG.Application/Services/ApprovalOnboardingService.cs`, `2-Application/MotorcycleRAG.Application/Services/AccessRequestService.cs`, `2-Application/MotorcycleRAG.Application/Services/UserAccessLifecycleService.cs`, and `4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs`
- [ ] T046 [P] Add health-check coverage and integration tests for notification and external identity dependency degradation in `1-Presentation/MotorcycleRAG.API/Program.cs` and `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AdminUserManagementIntegrationTests.cs`
- [ ] T047 [P] Harden authorization and admin-client isolation checks for the new admin endpoints in `1-Presentation/MotorcycleRAG.API/Configuration/AuthorizationPolicyNames.cs`, `1-Presentation/MotorcycleRAG.API/Configuration/Services/AuthorizationPoliciesConfiguration.cs`, and `1-Presentation/MotorcycleRAG.API/Middleware/AuthorizationMiddleware.cs`
- [ ] T048 [P] Update test data and local development configuration for onboarding, cancellation, and entitlement-mapping scenarios in `7-Deployment/DbSetup/sql/test-data.sql` and `1-Presentation/MotorcycleRAG.API/appsettings.Development.json`
- [ ] T049 Run the quickstart validation scenarios in `specs/002-user-onboarding-approval/quickstart.md` and capture any required doc adjustments there
- [ ] T050 Verify all deployment-affecting changes remain compatible with the GitHub Actions-only delivery path by reviewing `.github/workflows/deploy.yml` against `specs/002-user-onboarding-approval/plan.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately and records the required approval gate.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion.
- **User Story 2 (Phase 4)**: Depends on Phase 2 completion and consumes the request model introduced in US1, but remains independently testable once public requests and existing-user data are available.
- **User Story 3 (Phase 5)**: Depends on Phase 2 completion and integrates with the admin actions built in US2.
- **Polish (Phase 6)**: Depends on all targeted user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on other stories after foundational work.
- **User Story 2 (P2)**: Requires the persisted access-request model, unified management projection, and entitlement mapping from Phase 2.
- **User Story 3 (P3)**: Depends on approval and lifecycle actions from US2 and the identity or persistence groundwork from Phase 2.

### Within Each User Story

- Tests MUST be written and fail before implementation.
- Contracts and DTOs before services.
- Services before controllers and UI wiring.
- Persistence and identity-link updates before first-sign-in verification.
- Concurrency, cancellation, and metrics validation must not be deferred to ad hoc follow-up work.

### Parallel Opportunities

- T003, T004, and T005 can run in parallel.
- T008 and T013 can proceed in parallel once the foundational contracts and schema design are defined.
- T016 and T017 can run in parallel for US1.
- T025 and T026 can run in parallel for US2.
- T035, T036, and T037 can run in parallel for US3.
- UI tasks T032 and T033 can proceed in parallel after the Admin DTO and API shape are fixed.

---

## Parallel Example: User Story 2

```text
Task: "Add admin API integration tests for the unified management list, approve, tier-change, cancel-pending, cancel-existing-user, and authorization failure cases in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AdminUserManagementIntegrationTests.cs"
Task: "Add Admin app viewmodel tests for loading unified rows, enabling allowed actions, changing tiers, retrying onboarding, and cancelling rows in 5-Test/MotorcycleRAG.Admin.Tests/ViewModels/UserManagementViewModelTests.cs"

Task: "Add Admin app DTOs for unified management rows and admin actions in 1-Presentation/MotorcycleRAG.Admin/Services/Dtos/UserManagementRowDto.cs, 1-Presentation/MotorcycleRAG.Admin/Services/Dtos/UserManagementListResponseDto.cs, 1-Presentation/MotorcycleRAG.Admin/Services/Dtos/ApproveAccessRequestDto.cs, 1-Presentation/MotorcycleRAG.Admin/Services/Dtos/ChangeManagedUserTierDto.cs, and 1-Presentation/MotorcycleRAG.Admin/Services/Dtos/CancelManagementItemDto.cs"
Task: "Create the Admin app user-management viewmodel and item models in 1-Presentation/MotorcycleRAG.Admin/ViewModels/UserManagementViewModel.cs and 1-Presentation/MotorcycleRAG.Admin/ViewModels/UserManagementRowViewModel.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: Verify request submission, deduplication, notification routing, and management-list visibility independently.

### Incremental Delivery

1. Finish Setup + Foundational to establish internal identity, management-row, observability, and request-state primitives.
2. Deliver User Story 1 as the public request-entry MVP.
3. Deliver User Story 2 so admins can manage pending requests and existing users from one table.
4. Deliver User Story 3 to complete approval-time provisioning, first-sign-in behavior, tier moves, and access revocation behavior.
5. Finish with Phase 6 hardening, telemetry, validation, and delivery checks.

### Parallel Team Strategy

1. One engineer handles schema, contracts, repositories, and telemetry in Phase 2.
2. One engineer handles WebUI request submission once the API contract is stable.
3. One engineer handles Admin app unified management UI once the management-list and action endpoints are defined.
4. One engineer handles approval-time provisioning, tier reconciliation, and identity lookup refactoring once foundational persistence is in place.

## Notes

- [P] tasks = different files, no dependencies
- [Story] labels map tasks to specific user stories for traceability
- This feature includes a real approval gate before implementation because the behavior is cross-cutting and identity-sensitive
- Do not include manual deployment, direct cloud mutation, or local container publication tasks