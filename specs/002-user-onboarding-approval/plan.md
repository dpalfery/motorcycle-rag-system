# Implementation Plan: User Onboarding Approval

**Branch**: `[002-user-onboarding-approval]` | **Date**: 2026-04-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-user-onboarding-approval/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command.

## Summary

Implement an approval-first onboarding flow across the WebUI login page, the BFF/API boundary, the Admin app, Azure SQL, and Microsoft Entra External ID. Unauthenticated visitors submit an access request from the login page, authorized admins manage pending requests and existing users from one Admin app table, and admin actions can approve, move tier, retry failed onboarding, or cancel access. Approval immediately provisions three things before first sign-in: an internal managed user, an initial usage-tracking record, and an external identity/app-role assignment in Entra. Cancelling an existing user revokes effective access while preserving managed-user, identity-link, usage, and audit history.

## Technical Context

**Language/Version**: C# 13 / .NET 10 for API, BFF, and MAUI Admin; TypeScript/React for WebUI
**Primary Dependencies**: ASP.NET Core, Microsoft.Identity.Web with the repo's existing Microsoft Graph-capable API stack, Dapper, .NET MAUI with CommunityToolkit.Mvvm, React with TanStack Query
**Storage**: Azure SQL (`Users`, `Usage`, new `AccessRequests` and `UserIdentities` tables plus audit metadata), Microsoft Entra External ID guest user objects and app role assignments
**Testing**: xUnit-based unit and integration suites in existing .NET test projects, plus Admin app service/viewmodel tests
**Target Platform**: Azure-hosted API/BFF, Windows-first MAUI admin app, browser-based WebUI login experience
**Project Type**: Multi-project web + desktop admin solution following Clean Architecture
**Performance Goals**: 95% of valid requests become visible in the admin user-management list within 1 minute of request acceptance; 95% of approved users complete first successful protected API access within 10 minutes of approval; standard-case admin approval completes within 2 minutes of the pending row becoming available
**Constraints**: Approval-time onboarding must finish before first sign-in; public request submission stays validated and rate-limited; admin endpoints must preserve `mcr-api-admin` plus `azp` isolation; no new deployment path outside GitHub Actions; no unapproved dependency additions; internal user identity must be decoupled from provider identity; the Admin app remains one management surface for pending and existing rows; the post-approval identity contract is limited to `iss`, `sub`, `email`, `name`, `oid` when present, `azp`, `scp`, and `roles`
**Scale/Scope**: One public request flow, one unified admin management flow, one approval-time provisioning pipeline, one managed-user cancellation path, and one root-level identity lookup refactor affecting current-user resolution, plan enforcement, and usage tracking

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Initial Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| Security (I) | PASS | Anonymous request submission stays limited to email + provider with strict validation and redacted logging; approval, tier-change, retry, and cancel endpoints remain admin-only through the existing `mcr-api-admin` policy and `azp` isolation. |
| Clean Architecture (II) | PASS | WebUI, Admin, API, Application services, Contracts, and Persistence changes stay in their existing layers; Graph/Entra calls are hidden behind infrastructure abstractions. |
| Code Quality (III) | PASS | The feature removes the current implicit login-time success path and replaces it with executable approval-time and revocation behavior; one C# type per file and async I/O remain required. |
| Testing (IV) | PASS | Plan includes unit coverage for approval orchestration, tier mapping, cancellation, and identity mapping, integration coverage for API/admin flows, and Admin app tests for the unified management UI surface. |
| Observability (V) | PASS | Access request creation, admin actions, Graph invitation, app-role changes, usage seed creation, cancellations, and onboarding failures will emit structured logs, telemetry metrics, correlation IDs, and health degradation signals. |
| Resilience (VI) | PASS | Email and Graph calls will use timeouts and retry/backoff; partial failures resolve to durable retryable states rather than silent drift. |
| Process (VII) | PASS WITH APPROVAL GATE | The spec already records that implementation is cross-cutting and requires explicit user approval before execution; planning itself introduces no new infra or deployment path. |
| Delivery Constraints | PASS | No direct `pulumi up`, `az` write actions, or local container publication are required for this feature design. |

### Post-Design Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| Security (I) | PASS | Public request endpoint remains minimal, admin endpoints preserve the current authorization model, and Entra provisioning uses service-side Graph calls only. |
| Clean Architecture (II) | PASS | New contracts focus on requests, internal identity resolution, cancellation, unified management rows, and provisioning orchestration without leaking Entra SDK concerns into Domain. |
| Code Quality (III) | PASS | The design addresses the root identity mismatch by introducing explicit identity-link persistence and managed-user access states instead of patching around `Users.Id`. |
| Testing (IV) | PASS | Tests map cleanly to request submission, unified admin management, approval-time onboarding, tier moves, cancellation, and protected-access enforcement. |
| Observability (V) | PASS | Status transitions, retry reasons, cancellations, queue visibility latency, approval latency, and dependency health are first-class modeled outcomes, not only transient logs. |
| Resilience (VI) | PASS | Approval-time work is idempotent and retryable across SQL + Graph boundaries, and cancellation is modeled as access revocation instead of destructive deletion. |
| Process (VII) | PASS WITH APPROVAL GATE | Implementation still requires explicit approval because identity lifecycle behavior is cross-cutting. |
| Delivery Constraints | PASS | Validation can be done locally with existing projects/tests and, if needed later, through the normal GitHub Actions path only. |

## Project Structure

### Documentation (this feature)

```text
specs/002-user-onboarding-approval/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── onboarding-api.yaml
└── tasks.md
```

### Source Code (repository root)

```text
1-Presentation/
├── MotorcycleRag.WebUI/
│   └── src/
│       ├── pages/
│       │   └── LoginPage.tsx
│       └── contexts/
│           └── AuthContext.tsx
├── MotorcycleRag.WebUI.BFF/
│   ├── Controllers/
│   │   └── AuthController.cs
│   └── Configuration/Services/
│       └── AuthenticationServiceConfiguration.cs
├── MotorcycleRAG.API/
│   ├── Controllers/
│   │   ├── MeController.cs
│   │   ├── MotorcycleController.cs
│   │   ├── UsersAdminController.cs
│   │   └── [new onboarding admin controllers]
│   └── Services/
│       └── CurrentUserService.cs

2-Application/MotorcycleRAG.Application/
├── Services/
│   ├── UserAdminService.cs
│   ├── UserProvisioningService.cs
│   └── [new onboarding approval and access lifecycle services]

3-Domain/
├── MotorcycleRAG.Contracts/
│   └── Interfaces/
│       ├── IUserAdminService.cs
│       ├── IUserProvisioningService.cs
│       ├── IUserRepository.cs
│       └── IPlanRepository.cs
└── MotorcycleRAG.Contracts.Models/
    └── [new DTOs for request, management-row, and admin action flows]

4-Persistence/MotorcycleRAG.Persistence/
└── Sql/
    ├── schema.sql
    └── Repositories/
        └── UserRepository.cs

5-Test/
├── MotorcycleRAG.Admin.Tests/
├── tests/MotorcycleRAG.UnitTests/
└── tests/MotorcycleRAG.IntegrationTests/
```

**Structure Decision**: Extend the existing multi-client Clean Architecture solution in place. The feature touches all three presentation surfaces, adds new onboarding contracts and application services, introduces new persistence tables/repositories for access requests and provider identity links, adds a unified admin management query model, and reuses the existing test projects rather than creating new top-level projects.

## Phase 0 Research Summary

1. Approval-time onboarding must replace login-time auto-provisioning because the current `UserProvisioningService` creates users on successful login, which directly conflicts with the clarified requirement that onboarding completes only after admin approval.
2. The current `Users.Id` cannot continue to be the provider subject because the clarified spec requires a unique internal database identity distinct from provider-specific identifiers.
3. The BFF should remain a single OpenID Connect integration against the External ID tenant. The user selects Microsoft or Google during the access-request flow, but the actual provider experience remains inside Entra's configured journey rather than by adding parallel auth schemes to the BFF.
4. Entra provisioning should use invitation-based B2B onboarding and explicit app-role assignment through Microsoft Graph, which fits Microsoft guidance and the repo's current Microsoft.Identity.Web-based auth stack.
5. Tier names exposed in the onboarding workflow should be resolved centrally to existing entitlement artifacts instead of renaming the existing auth/plan model in this feature.

## Phase 1 Design Summary

### Identity And Persistence

- Add a durable `AccessRequests` table that tracks request decision state, onboarding execution state, correlation data, and concurrency versioning.
- Introduce a `UserIdentities` link table that maps `provider + email` and later provider object IDs to an internal `Users.Id` value.
- Keep `Usage.UserId` pointing to the internal `Users.Id`, allowing approval-time creation of the initial usage row without waiting for the first request.
- Model managed-user access revocation explicitly so canceling an existing user blocks access without deleting the managed user or audit history.
- Migrate current-user lookup from direct claim-to-`Users.Id` resolution to provider identity link resolution.

### Required Claims Contract

- Limit identity-linking and authorization assumptions to claims already defined by the repo auth model and current Entra setup.
- Use `iss` + `sub` as the stable issuer-subject identity pair for the approved external identity.
- Use `email` + `name` as profile attributes only; they do not replace the internal managed-user ID.
- Persist `oid` when Entra provides it so the invited external identity can be correlated safely across retries and later sign-ins.
- Continue to require `azp` for admin-client isolation, `scp` for delegated permission checks, and `roles` for app-role authorization at the API boundary.

### Entitlement Mapping

- Use one canonical mapping in this feature:
  - `trial` → `Free` plan + `DemoUser`
  - `road runner` → `Pro` plan + `Roadrunner`
  - `admin` → `Pro` plan + `mcr-api-admin`
- Keep the existing `Plus` plan and `ProUser` role out of the onboarding vocabulary for this feature.
- Apply tier-to-role mapping during approval, tier moves, and user cancellation or revocation workflows.

### Application Services

- Add an approval orchestration service responsible for validating request state, assigning tier, creating or updating the managed user, seeding usage tracking, creating the Entra invitation, assigning app access, and persisting durable status transitions.
- Split login-time reconciliation from approval-time provisioning so first sign-in can reconcile profile fields without creating unauthorized users.
- Add a managed-user lifecycle service responsible for tier changes, cancellation, and access-state enforcement for existing users.
- Centralize tier-to-entitlement mapping in one application service to bridge user-facing tiers (`trial`, `road runner`, `admin`) and the repo's existing plan/app-role model.

### Presentation Surfaces

- WebUI login page adds a public access-request form while preserving the existing authenticated redirect behavior.
- BFF authentication remains single-scheme OIDC; no second auth subsystem is introduced in this feature.
- Admin app adds one user-management page/viewmodel/service flow for listing requests and managed users together, assigning tiers, approving requests, retrying failed onboarding, and canceling pending or existing rows.
- API adds anonymous request submission plus admin-only management-list, approve, retry, change-tier, and cancel endpoints.

### Observability And Recovery

- Every transition stores status, timestamps, attempt count, row version, and a redacted failure reason.
- Approval-time failures are not treated as partial success; they land in `OnboardingFailed` and can be retried idempotently.
- Request visibility latency, approval latency, first-sign-in readiness latency, cancellations, retries, and dependency failures emit named metrics as well as structured logs.
- Health checks and dependency degradation signals cover notification delivery and external identity provisioning so operators can distinguish degraded behavior from hard failure.
- Graph invitation ID, external directory object ID, and internal user ID are stored for supportability and audit.

## Phase 2 Implementation Outline

### User Story 1

- Add the login-page access-request UX and anonymous submission contract.
- Add persistence and API handling for deduplicated pending requests.
- Add notification dispatch to the configured approver mailbox/distribution list.

### User Story 2

- Add one admin management-list endpoint and Admin app user-management UI that combines pending requests and existing users.
- Add approve, tier-change, retry, and cancel command handling from the same surface.
- Surface pending, active, failed-onboarding, and cancelled states in the Admin app.

### User Story 3

- Implement approval-time provisioning orchestration across SQL, usage seeding, and Entra invitation/app access.
- Refactor current-user lookup to resolve internal users from identity links instead of raw claims.
- Add retry handling, access revocation, and completed-sign-in verification for approved users.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations require justification in the design phase.