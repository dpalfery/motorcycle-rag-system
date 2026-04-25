# Feature Specification: User Onboarding Approval

**Feature Branch**: `[002-user-onboarding-approval]`  
**Created**: 2026-04-23  
**Status**: Draft  
**Input**: User description: "we need to add a user onboarding and approval feature, on the login page there should be a signup feature where they provide the account they want to authenticate with , microsoft or google account and then an email request is sent to me. I can then go to the admin app and see the list of all users and their teir, trial, road runner or admin (use the already deinfed teirs) . on the admin user management page in the admin app i will assing the tier and then user onbaording will happen, add them to user management and usage tables and register them in entra id as a external identity with their claims"

**Approval Notes**: This feature changes authentication-adjacent onboarding behavior, introduces admin-facing approval workflow changes, and adds external identity provisioning expectations. Explicit user approval is required before implementation because the behavior is cross-cutting and affects identity lifecycle.

## Clarifications

### Session 2026-04-23

- Q: When should onboarding and external identity provisioning happen? → A: Admin approval immediately creates the managed user, prepares usage tracking, and registers the Entra external identity before first sign-in.
- Q: Who is allowed to manage access requests and existing users? → A: Only callers that satisfy the existing `mcr-api-admin` API policy and the Admin app `azp` isolation requirement can perform admin actions for this feature.
- Q: What should happen if approval-time onboarding fails? → A: Keep the request in a failed-onboarding state that admins can retry without losing the assigned tier or approval decision.
- Q: How should provider identity be modeled? → A: The combination of provider and email is the request identity; a different provider for the same email requires a separate request and approval, and each onboarded user receives a unique internal database identity.
- Q: When should the initial usage-tracking record be created? → A: Admin approval creates the managed user and an initial usage-tracking record immediately, before the user's first real request.
- Q: How should the Admin app manage users? → A: The Admin app uses one user-management table that shows pending requests and existing users together and supports approve, move tier, retry onboarding, and cancel from the same surface.
- Q: What does cancel mean for an existing user? → A: Canceling an existing user revokes effective access and downstream app-role assignments while preserving the managed user, usage history, identity-link history, and audit history.
- Q: What minimum claims must the approved identity provide after onboarding? → A: The approved identity contract is limited to trusted identity and authorization claims already used by the platform: `iss`, `sub`, `email`, `name`, `oid` when present, `azp` for client isolation, `scp` for delegated permissions, and `roles` for app-role authorization.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Request Access From Login (Priority: P1)

A prospective user can open the login page, choose whether they intend to use a Microsoft account or Google account, submit their email address, and trigger an approval request to the designated approver.

**Why this priority**: Without a user-facing request flow, onboarding remains manual and invisible to the requester, so no scalable approval workflow exists.

**Independent Test**: Can be fully tested by submitting a new access request from the login page and verifying that the request is captured, deduplicated, and routed to the approver without requiring any admin assignment yet.

**Acceptance Scenarios**:

1. **Given** a visitor is on the login page and has not been approved, **When** they submit an access request with a supported identity provider and email address, **Then** the system records the request and sends an approval email to the designated approver.
2. **Given** a visitor has already submitted a pending request for the same email and provider, **When** they submit again, **Then** the system prevents duplicate pending requests and shows the current request status.
3. **Given** a visitor submits the same email address with a different provider, **When** the second request is submitted, **Then** the system treats it as a separate request that requires its own approval.

---

### User Story 2 - Manage Pending And Existing Users In The Admin App (Priority: P2)

A caller that satisfies the existing `mcr-api-admin` API policy and Admin app `azp` isolation requirement can open one user-management table in the Admin app, review pending requests and existing users together, and perform the row actions allowed by the current lifecycle state.

**Why this priority**: The signup request has no operational value until an authorized admin can manage the full user lifecycle from one consistent surface.

**Independent Test**: Can be fully tested by opening the Admin app as an authorized admin user, loading the unified user-management table, approving a pending request, changing the tier for an existing user, and canceling both a pending request and an existing user without requiring the end user to sign in during the test.

**Acceptance Scenarios**:

1. **Given** one or more pending access requests or existing managed users exist, **When** an authorized admin opens the user-management page, **Then** the page shows one table containing both pending and existing rows with their current tier, lifecycle state, and allowed actions.
2. **Given** a pending request is ready for approval, **When** an authorized admin assigns a valid tier and approves it, **Then** the system immediately creates the managed user, prepares usage tracking, registers the Entra external identity, and records the chosen tier before marking onboarding complete.
3. **Given** an existing managed user is active, **When** an authorized admin changes the user's tier, **Then** the system updates the entitlement mapping, downstream app-role assignment, and management row state without creating a second managed user.
4. **Given** a pending request or an active managed user should no longer have access, **When** an authorized admin cancels the row, **Then** the system moves the row to a cancelled state, blocks protected access, and preserves diagnostic and audit history.

---

### User Story 3 - Complete Onboarding After Approval (Priority: P3)

Once a request is approved, the system immediately finishes onboarding by creating or updating the user's records in user management and usage tracking, then registering the approved user as an external identity with the mapped app-role assignments so the user can authenticate with the selected provider.

**Why this priority**: Approval alone is incomplete unless it results in a usable authenticated account with the correct tier, identity link, and usage tracking eligibility.

**Independent Test**: Can be fully tested by approving a request, then completing first sign-in with the selected provider and verifying that the user exists in the user management view, usage tracking is enabled, and the approved role/tier mapping is present.

**Acceptance Scenarios**:

1. **Given** a request has been approved with a tier, **When** the approval action completes, **Then** the user appears in the managed user list, is eligible for usage tracking, is associated with the approved tier, and has an Entra external identity registered before first sign-in.
2. **Given** an approved user signs in with the provider selected during signup, **When** the authentication succeeds, **Then** access is granted using the internal user identity and the tier-to-plan-to-role mapping established during approval.
3. **Given** an approved user attempts first sign-in with a different provider than the one approved, **When** authentication succeeds at the identity provider, **Then** protected application access is blocked until the provider mismatch is resolved through the approved identity link.
4. **Given** approval-time onboarding fails after the tier is assigned, **When** the failure is recorded, **Then** the request remains in a failed-onboarding state that an authorized admin can retry without requiring a new signup request.
5. **Given** an active managed user has been cancelled, **When** that user attempts protected application access, **Then** access remains blocked until a later admin action restores the user's effective access state.

### Edge Cases

- A `provider + email` combination that already has a pending request returns the existing request state and does not create a duplicate row.
- The same email submitted through a different provider remains a separate request and cannot reuse the existing provider identity link automatically.
- A `provider + email` combination that already maps to an active managed user reuses that managed user for tier moves or later re-approval and never creates a second managed user.
- External identity registration failure after approval leaves the request in a retryable failed-onboarding state and does not grant protected access.
- Notification delivery failure records degraded operational state and correlation data without dropping the pending request.
- Cancelling a pending request creates no managed user, no identity link, and no usage-seed record.
- Cancelling an existing user revokes effective access without deleting the managed user, usage history, identity-link history, or audit history.
- Conflicting admin actions on the same row must surface a concurrency or invalid-state failure instead of applying silently out of order.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST present a signup/access-request path on the login page for unauthenticated users who do not yet have access.
- **FR-002**: System MUST allow the requester to choose a supported authentication provider from Microsoft or Google when submitting an access request.
- **FR-003**: System MUST collect the email address associated with the requester's chosen authentication account.
- **FR-004**: System MUST validate that the submitted provider and email combination is complete, well-formed, and not already pending in a duplicate request state.
- **FR-005**: System MUST send an approval notification email for each new valid request to a configured approver address or distribution list.
- **FR-006**: System MUST store pending access requests so they can be reviewed in the admin app.
- **FR-007**: System MUST provide one admin user-management view that lists pending requests and existing managed users together in a single table.
- **FR-008**: System MUST allow only callers that satisfy the existing `mcr-api-admin` API policy and Admin app `azp` isolation requirement to perform admin actions for this feature.
- **FR-009**: System MUST allow an authorized admin to approve a pending request and assign one of the supported tiers `trial`, `road runner`, or `admin`.
- **FR-010**: System MUST allow an authorized admin to change the tier of an existing managed user from the same management surface without creating a second managed user.
- **FR-011**: System MUST allow an authorized admin to cancel either a pending request or an existing managed user from the same management surface.
- **FR-012**: System MUST prevent onboarding completion unless an administrator has approved the request and assigned a valid tier.
- **FR-013**: System MUST complete onboarding as part of the admin approval action by adding the user to user management records and creating the initial usage-tracking record for that user before first sign-in.
- **FR-014**: System MUST register each approved user as an external identity in Entra ID and apply the app-role assignments mapped from the approved tier before the approved user is allowed to complete first sign-in.
- **FR-015**: System MUST ensure that an approved user receives access consistent with the assigned tier mapping when they authenticate with the approved provider.
- **FR-016**: System MUST record request decision changes, onboarding execution changes, and managed-user access changes in a way that administrators can diagnose partial failures and retry safely.
- **FR-017**: System MUST preserve the assigned tier and approval decision when onboarding fails after approval, and expose the request in a failed-onboarding state until an authorized admin retries or resolves it.
- **FR-018**: System MUST block unauthorized, unapproved, failed-onboarding, provider-mismatched, or cancelled users from completing sign-in access to protected application features.
- **FR-019**: System MUST treat `provider + email` as the unique identity for access requests and approval decisions, so the same email submitted through a different provider requires a separate request and approval.
- **FR-020**: System MUST assign each onboarded user a unique internal database identity that is distinct from provider-specific identity values and persists across user management and usage tracking records.
- **FR-021**: System MUST expose a management-row projection that includes request state, onboarding state, managed-user access state, assigned tier, allowed actions, timestamps, correlation data, and row versioning for optimistic concurrency.
- **FR-022**: System MUST use one canonical entitlement mapping for this feature: `trial` maps to `Free` plan plus `DemoUser`, `road runner` maps to `Pro` plan plus `Roadrunner`, and `admin` maps to `Pro` plan plus `mcr-api-admin`.
- **FR-023**: System MUST reject conflicting or stale admin actions using optimistic concurrency or an equivalent guard and return a diagnosable invalid-state or conflict response.

### Required Claims Contract

- **RC-001**: After approval-time onboarding, the authenticated identity presented to the BFF and API MUST expose `iss` and `sub` so the external identity can be traced to a stable issuer-subject pair.
- **RC-002**: The authenticated identity MUST expose `email` and `name`, and the platform MUST treat these as profile attributes rather than the internal managed-user identifier.
- **RC-003**: When Entra issues an object identifier for the approved external identity, the authenticated identity MUST expose `oid`, and the system MUST persist it as a provider-linked external identifier rather than replacing the internal `Users.Id` value.
- **RC-004**: Tokens presented to the API for protected access MUST continue to satisfy the repo's existing authorization contract by exposing `azp` for client isolation, `scp` for delegated permissions, and `roles` when app-role authorization applies.
- **RC-005**: The onboarding workflow MUST fail closed if the post-approval identity does not provide the claims required for identity linking and authorization under `RC-001` through `RC-004`.

### Lifecycle State Vocabulary

- **Request Decision State**: Tracks whether the request record is `Pending`, `Approved`, or `Cancelled`.
- **Onboarding Execution State**: Tracks provisioning progress as `NotStarted`, `InProgress`, `Failed`, `Completed`, or `NotRequired`.
- **Managed User Access State**: Tracks effective access for the managed user as `None`, `Active`, `Cancelled`, or `Disabled`.
- **User Management Row State**: The Admin app shows a derived row state such as `PendingApproval`, `OnboardingInProgress`, `OnboardingFailed`, `Active`, or `Cancelled`, while persistence continues to store the underlying state dimensions separately.

### Key Entities *(include if feature involves data)*

- **Access Request**: Represents a public request for access, including requested email address, chosen provider, request decision state, onboarding execution state, timestamps, and approver-facing state.
- **Managed User**: Represents an onboarded user record used by user management, including a unique internal database identity, assigned tier, managed-user access state, enabled state, and identity linkage.
- **Usage Tracking Record**: Represents the initial persisted usage-tracking row or equivalent tracking record created during approval-time onboarding so the user is present in usage tracking before first use.
- **Tier Assignment**: Represents the approved entitlement level for a user, constrained to the supported tiers `trial`, `road runner`, and `admin`, and resolved through one canonical mapping to plan and app-role artifacts.
- **External Identity Registration**: Represents the approved external identity entry and role assignment needed to allow the user to authenticate through the selected provider.
- **User Management Row**: Represents the unified admin projection used by the one-table Admin app surface, combining request details, managed-user details, lifecycle states, and allowed row actions.

## Success Criteria *(mandatory)*

## Constitution Alignment *(mandatory)*

Describe how this feature complies with each principle in
`.specify/memory/constitution.md`.

- Security (I): Access requests remain minimal and validated, admin actions require the existing `mcr-api-admin` policy plus Admin app `azp` isolation, and logs/telemetry remain redacted.
- Clean Architecture (II): Login UX, admin UI, approval workflow, identity resolution, tier mapping, and Entra provisioning remain separated by presentation, application, domain, and infrastructure boundaries.
- Code Quality (III): The feature is expected to be implemented end-to-end without placeholder onboarding steps, with zero warnings and clear async behavior across identity, notification, and cancellation operations.
- Testing (IV): The feature requires independent tests for login-page request submission, unified admin management actions, approval-time onboarding, tier changes, cancellation, and protected-access enforcement.
- Observability (V): Approval requests, onboarding transitions, cancellations, tier moves, dependency degradation, notification failures, external identity provisioning outcomes, and timing metrics must be visible through structured logs, health signals, and operational metrics.
- Resilience (VI): Email delivery, identity registration, cancellation, and onboarding completion must fail safely, surface retryable states where appropriate, and avoid leaving users in an undiagnosable partial state.
- Process & Workflow (VII): approvals required, repository hygiene preserved, and delivery uses the GitHub Actions path only.

### Measurable Outcomes

- **SC-001**: 95% of valid access requests transition from accepted by `POST /api/access-requests` to visible in the admin user-management list within 1 minute.
- **SC-002**: 95% of approved users transition from approval recorded to first successful protected API access with the approved provider within 10 minutes.
- **SC-003**: In standard cases where dependencies are healthy and no retry is required, administrators can load the pending row, assign a tier, and complete approval in under 2 minutes.
- **SC-004**: 100% of approved users appear in the managed user list with the correct tier and mapped role assignment before they are granted protected application access.
- **SC-005**: 100% of pending, failed-onboarding, provider-mismatched, or cancelled rows remain blocked from protected application access until an authorized admin action restores a valid access state.