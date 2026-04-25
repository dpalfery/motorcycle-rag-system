# Phase 1 Data Model: User Onboarding Approval

This data model describes the entities and relationships required to implement `specs/002-user-onboarding-approval/spec.md` while honoring the clarified requirement that internal user identity must be distinct from provider identity and that pending requests and existing users can be managed from the same Admin app table.

## Core Entities

### AccessRequest

**Purpose**: Represents a public request for access before the requester is an onboarded application user.

**Key fields**:
- `AccessRequestId` (GUID or sequential unique identifier)
- `RequestedEmail` (normalized email, required)
- `RequestedProvider` (`Microsoft` or `Google`, required)
- `RequestDecisionState` (`Pending`, `Approved`, `Cancelled`)
- `OnboardingExecutionState` (`NotStarted`, `InProgress`, `Failed`, `Completed`, `NotRequired`)
- `RequestedAtUtc`
- `NotificationSentAtUtc` (nullable)
- `AssignedTier` (`trial`, `road runner`, `admin`, nullable until approval)
- `ApprovedByUserId` (internal admin user ID, nullable)
- `ApprovedAtUtc` (nullable)
- `CancelledByUserId` (nullable)
- `CancelledAtUtc` (nullable)
- `CancelReason` (nullable, redacted)
- `OnboardingAttemptCount`
- `LastFailureCode` (nullable)
- `LastFailureMessage` (nullable, redacted)
- `ManagedUserId` (internal user ID, nullable until onboarding creates or links user)
- `ExternalDirectoryObjectId` (nullable until Entra user exists)
- `CorrelationId`
- `RowVersion`

**Validation rules**:
- `RequestedProvider` must be one of the supported providers.
- `RequestedEmail` must be well-formed and normalized.
- Only one `Pending` request may exist for the same `RequestedProvider + RequestedEmail` pair.
- A different provider for the same email is permitted as a separate request.
- A cancelled pending request may be resubmitted as a new request.

**State transitions**:
- `RequestDecisionState`: `Pending` → `Approved` or `Cancelled`
- `OnboardingExecutionState`: `NotStarted` → `InProgress` → `Completed` or `Failed`
- `OnboardingExecutionState`: `Failed` → `InProgress` on retry
- `OnboardingExecutionState`: `NotStarted` or `Failed` → `NotRequired` if the pending request is cancelled before provisioning completes

### ManagedUser

**Purpose**: Represents the application-owned user record referenced by user management, rate limiting, plan enforcement, and usage tracking.

**Key fields**:
- `Id` (internal managed-user identifier; becomes the canonical `Users.Id` value)
- `Email`
- `DisplayName`
- `FirstName`
- `LastName`
- `PlanId` (existing entitlement reference)
- `TierLabel` (`trial`, `road runner`, `admin`)
- `AccessState` (`None`, `Active`, `Cancelled`, `Disabled`)
- `CancelledAtUtc` (nullable)
- `CancelledByUserId` (nullable)
- `CancelReason` (nullable, redacted)
- `IsEnabled`
- `CreatedDate`
- `LastUpdatedDate`
- `RowVersion`

**Validation rules**:
- Must not be created from an unapproved request.
- Must be linked to at least one provider identity record before protected access is allowed.
- `Id` must remain immutable once created.
- Cancelling an existing user revokes effective access without deleting the managed user or its history.
- Tier changes update entitlement state in place and do not reopen the original request.

**State transitions**:
- `None` → `Active` after successful approval-time onboarding
- `Active` → `Cancelled` when an admin revokes access from the management table
- `Active` or `Cancelled` → `Disabled` when a broader non-feature disable action is applied outside this feature

### UserIdentityLink

**Purpose**: Maps an internal managed user to the selected external identity/provider metadata.

**Key fields**:
- `UserIdentityId` (GUID or surrogate key)
- `ManagedUserId` (FK to `ManagedUser.Id`)
- `Provider` (`Microsoft` or `Google`)
- `ProviderEmail`
- `ProviderUserId` (nullable until the external user redeems or signs in)
- `ExternalDirectoryObjectId` (nullable until invitation or provisioning completes)
- `InvitationStatus` (`NotCreated`, `PendingAcceptance`, `Accepted`, `Failed`, `Revoked`)
- `InvitationCreatedAtUtc`
- `InvitationRedeemedAtUtc` (nullable)
- `AccessRevokedAtUtc` (nullable)
- `LastSyncedAtUtc`

**Validation rules**:
- `Provider + ProviderEmail` is unique across active identity links.
- A managed user may have multiple identity links only when the business chooses to support more than one provider later; this feature starts with one active link per approved provider request.
- Cancelling an existing user revokes effective access and marks downstream assignment state accordingly without deleting the identity-link row.
- The identity link must be able to reconcile the approved sign-in to the minimal claims contract: `iss`, `sub`, `email`, `name`, `oid` when present, plus `azp`, `scp`, and `roles` where authorization rules require them.

### UsageSeedRecord

**Purpose**: Represents the initial usage-tracking row or equivalent durable marker created at approval time so the user exists in usage tracking before first application use.

**Key fields**:
- Existing `Usage.Id`
- `UserId` (FK to internal `ManagedUser.Id`)
- `Endpoint` (system-generated seed endpoint name)
- `HttpMethod`
- `StatusCode`
- `IsSuccess`
- `RequestTime`
- `DurationMs`

**Validation rules**:
- Must be created during successful approval-time onboarding.
- Must reference the internal user ID, not a provider subject.
- Retry after `OnboardingFailed` must not create duplicate usage-seed records.

### TierAssignment

**Purpose**: Captures the user-facing onboarding tier and its mapping to existing plan and app-role artifacts.

**Key fields**:
- `TierLabel` (`trial`, `road runner`, `admin`)
- `PlanId` (`free-plan-001`, `pro-plan-001`)
- `PlanName` (`Free`, `Pro`)
- `AppRoleValues` (`DemoUser`, `Roadrunner`, `mcr-api-admin`)
- `IsAdminCapable`
- `DailyLimitPolicy`

**Canonical mapping for this feature**:
- `trial` → `Free` plan + `DemoUser`
- `road runner` → `Pro` plan + `Roadrunner`
- `admin` → `Pro` plan + `mcr-api-admin`

**Validation rules**:
- The admin workflow can assign only the three supported tier labels.
- Mapping from label to existing entitlement artifacts must be defined centrally and used consistently by approval, tier changes, and cancellation or revocation.
- The existing `Plus` plan and `ProUser` role remain outside the onboarding vocabulary for this feature.

### OnboardingAttempt

**Purpose**: Tracks each approval-time provisioning attempt for diagnostics and retries.

**Key fields**:
- `OnboardingAttemptId`
- `AccessRequestId`
- `AttemptNumber`
- `StartedAtUtc`
- `CompletedAtUtc`
- `Result` (`Succeeded`, `Failed`, `Cancelled`)
- `FailureStage` (`UserCreate`, `UsageSeed`, `GraphInvitation`, `AppAssignment`, `Notification`, etc.)
- `FailureMessage` (redacted)

**Validation rules**:
- Retry attempts increment monotonically.
- Failure detail must be supportable without leaking secrets or raw tokens.
- Retry after `Failed` reuses the same internal managed user and must not create duplicate identity links or duplicate usage-seed rows.

### UserManagementRow

**Purpose**: Represents the unified admin projection used by the one-table Admin app surface.

**Key fields**:
- `RowId` (stable identifier for the management row)
- `RowType` (`PendingRequest`, `ManagedUser`)
- `AccessRequestId` (nullable for managed-user-only rows)
- `ManagedUserId` (nullable for pending rows)
- `Email`
- `Provider`
- `TierLabel` (nullable until approval)
- `RequestDecisionState`
- `OnboardingExecutionState`
- `ManagedUserAccessState`
- `UserManagementRowState` (`PendingApproval`, `OnboardingInProgress`, `OnboardingFailed`, `Active`, `Cancelled`)
- `AllowedActions` (`Approve`, `RetryOnboarding`, `ChangeTier`, `Cancel`)
- `LastFailureCode` (nullable)
- `LastFailureMessage` (nullable, redacted)
- `CorrelationId`
- `RowVersion`

**Validation rules**:
- The row projection is read-only from the client perspective; all mutations route to request or managed-user actions with concurrency checks.
- Allowed actions are derived from persisted state, not client-side inference.

## Relationships

- `AccessRequest` optionally resolves to one `ManagedUser` after approval-time onboarding.
- `ManagedUser` has one or more `UserIdentityLink` records over its lifetime.
- `ManagedUser` has many `Usage` records, beginning with the approval-time `UsageSeedRecord`.
- `AccessRequest` has many `OnboardingAttempt` records.
- `TierAssignment` is applied during approval and tier changes and determines the entitlement data stamped onto `ManagedUser` and Entra app access.
- `UserManagementRow` is a derived projection over `AccessRequest`, `ManagedUser`, and `TierAssignment` for admin-management queries.

## Current Schema Impact

The current schema uses `Users.Id` as the primary key and current request handling resolves that ID straight from claims. To satisfy the clarified feature requirements, implementation must:

1. Change the conceptual meaning of `Users.Id` to an internal managed-user ID.
2. Add a `UserIdentities`-style table to hold provider-specific identity values.
3. Add request decision, onboarding execution, cancellation, and concurrency metadata needed by the unified admin-management surface.
4. Update current-user resolution, usage tracking, and `/api/me` profile lookup to resolve internal user IDs through the provider identity link.
5. Add the query shape needed to return one admin-management table without forcing the client to merge request and managed-user data independently.

## Recommended State Model

Use the following persisted state dimensions and derived row states:

- `PendingApproval`: `RequestDecisionState=Pending`, `OnboardingExecutionState=NotStarted`, `ManagedUserAccessState=None`
- `OnboardingInProgress`: `RequestDecisionState=Approved`, `OnboardingExecutionState=InProgress`, `ManagedUserAccessState=None`
- `OnboardingFailed`: `RequestDecisionState=Approved`, `OnboardingExecutionState=Failed`, `ManagedUserAccessState=None`
- `Active`: `RequestDecisionState=Approved`, `OnboardingExecutionState=Completed`, `ManagedUserAccessState=Active`
- `Cancelled`: either a pending request cancelled before provisioning or an existing managed user cancelled after onboarding, surfaced as one derived row state with different persistence semantics underneath