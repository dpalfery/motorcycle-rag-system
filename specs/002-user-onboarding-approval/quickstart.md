# Quickstart: User Onboarding Approval

This quickstart is for local validation of the onboarding-and-approval workflow described in `specs/002-user-onboarding-approval/spec.md`.

## Prerequisites

- .NET 10 SDK from `global.json`
- Local configuration for the API, BFF, and Admin app
- A Microsoft Entra External ID tenant configured for the application's current OpenID Connect flow
- Microsoft or Google identities enabled in the tenant/user flow used by the WebUI sign-in experience
- Seeded SQL data for existing plans and test users
- Service-side permissions/configuration for invitation-based external user provisioning and application access assignment
- Seeded application entitlement data so approval tiers can be mapped to existing plan/app-role artifacts

## Build The Relevant Projects

```powershell
dotnet build 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj /p:Configuration=Debug
dotnet build 1-Presentation/MotorcycleRag.WebUI.BFF/MotorcycleRag.WebUI.BFF.csproj /p:Configuration=Debug
dotnet build 1-Presentation/MotorcycleRAG.Admin/MotorcycleRAG.Admin.csproj /p:Configuration=Debug /p:TargetFramework=net10.0-windows10.0.19041.0
```

## Run The Applications

### API

```powershell
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

### BFF / WebUI Host

```powershell
dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF/MotorcycleRag.WebUI.BFF.csproj
```

### Admin App

Preferred local path: use the existing VS Code launch profile `Admin + API (Windows)` or start the built MAUI executable after a successful build.

## Validation Scenarios

### Scenario 1: Submit A New Access Request

1. Open the WebUI login page.
2. Confirm the existing sign-in path is still present.
3. Submit a new access request with provider `Microsoft` or `Google` and a valid email.
4. Verify the request is accepted, the login-page access-request panel shows `PendingReview`, and duplicate re-submission for the same `provider + email` pair is prevented.
5. Re-enter the same provider and email and verify the same panel returns the current requester-visible status instead of creating another pending request.
6. Verify the request becomes visible in the admin user-management list within 1 minute of `POST /api/access-requests` returning `202`.

### Scenario 2: Approve From The Unified Admin Management Table

1. Sign into the Admin app with a user that satisfies the existing admin authorization policy.
2. Open the user-management view and verify one table shows both pending requests and existing managed users, including the provider for each row so same-email requests across providers remain distinguishable.
3. Locate the pending request row and assign one of the supported tiers: `trial`, `road runner`, or `admin`.
4. Approve the request.
5. Verify the row transitions through onboarding and finishes in an active state.
6. Verify the managed user now exists with an internal user ID, a provider identity link, and an initial usage-tracking record.

### Scenario 3: Complete First Sign-In After Approval

1. Use the provider chosen in the original request to sign in through the WebUI.
2. Verify protected application access succeeds for the protected WebUI session, `/api/me`, and motorcycle query access.
3. Verify `/api/me` resolves the approved user profile via the internal identity mapping rather than failing with `User not found`.
4. Verify the resulting identity presented to the BFF and API includes the minimum claims contract used by this feature: `iss`, `sub`, `email`, `name`, `oid` when present, plus `azp`, `scp`, and `roles` where applicable.
5. Verify the resulting access level matches the approved tier mapping.
6. Verify the time from approval recorded to first successful protected API access remains within the SC-002 target.

### Scenario 4: Move Tier Or Cancel From The Same Admin Table

1. Open the unified Admin app user-management table.
2. Select an active managed-user row.
3. Change the user's tier and verify the management row, plan mapping, and app-role assignment update without creating a second managed user.
4. Cancel the same managed user and verify protected access is revoked while the managed user, identity link, usage history, and audit history remain present.
5. Select a pending request row and cancel it.
6. Verify no managed user, identity link, or usage-seed record is created for the cancelled pending request.

### Scenario 5: Retry A Failed Onboarding Attempt

1. Force or simulate a provisioning failure after the approval decision is recorded.
2. Verify the request lands in `OnboardingFailed` without losing the tier assignment and that the failure stage is visible for admin diagnosis.
3. Retry onboarding from the admin surface.
4. Verify the retry is idempotent and ends in `Completed` without duplicate users or duplicate identity links.

### Scenario 6: Decline Or Notification Degradation Behavior

1. Cancel a pending request from the admin surface and verify this acts as the decline path for the request.
2. Re-enter the same provider and email on the login page and verify the requester-visible status is `Cancelled` until a fresh request is created.
3. Simulate notification-delivery failure for a newly accepted request.
4. Verify the request remains pending, the admin surface shows degraded notification state with correlation data, and the requester still sees `PendingReview` rather than a prompt to resubmit.

## Suggested Automated Validation

Run the existing test suites that are closest to the impacted area before and after implementation:

```powershell
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests/MotorcycleRAG.UnitTests.csproj
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests/MotorcycleRAG.IntegrationTests.csproj
dotnet test 5-Test/MotorcycleRAG.Admin.Tests/MotorcycleRAG.Admin.Tests.csproj
```

Focus additions for this feature should cover:

- request deduplication and approval orchestration
- unified admin management list behavior
- admin tier changes and cancellation behavior
- internal user identity resolution after first sign-in
- provider-mismatch blocking behavior
- retry handling for failed-onboarding requests
- timing evidence for SC-001, SC-002, and SC-003