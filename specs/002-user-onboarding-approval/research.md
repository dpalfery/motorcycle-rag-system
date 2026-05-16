# Phase 0 Research: User Onboarding Approval

This document resolves the design-impacting questions behind `specs/002-user-onboarding-approval/spec.md` and records the decisions used by the implementation plan.

## Decision 1: Replace Login-Time Auto-Provisioning With Approval-Time Onboarding

**Decision**: Move user creation out of the current login-time `UserProvisioningService` path and into an explicit approval workflow orchestrated from the Application layer.

**Rationale**: The current implementation creates or updates a `UserDTO` as soon as a successful sign-in occurs. That behavior directly conflicts with the clarified spec, which requires admin approval, tier assignment, initial usage tracking, and external identity registration before the user can access protected features.

**Alternatives considered**:
- Keep login-time auto-provisioning and mark unapproved users as disabled. Rejected because it still creates unmanaged users before approval and does not satisfy the required approval-first lifecycle.
- Block all login attempts until a manual database row exists. Rejected because it leaves no durable public request workflow or retryable onboarding state.

## Decision 2: Introduce A Separate Provider Identity Link Model

**Decision**: Treat `Users.Id` as the internal managed-user identity and add a dedicated `UserIdentities` persistence model for `provider + email`, provider subject/object ID, and external directory object ID.

**Rationale**: The clarified spec requires each onboarded user to receive a unique internal database identity distinct from provider-specific values. The current implementation uses `CurrentUserService.UserId` directly against `Users.Id`, which couples the database primary key to the authentication provider's subject/identifier and cannot satisfy the new rule without a structural change.

**Alternatives considered**:
- Keep `Users.Id` as the provider subject and add a second internal key later. Rejected because it would preserve the identity ambiguity that caused the requirement in the first place and continue to leak provider identity throughout the application.
- Store provider details only on `Users` without a separate table. Rejected because the spec allows the same email across different providers as separate requests and requires explicit provider identity linkage.

## Decision 3: Keep A Single BFF Authentication Scheme

**Decision**: Keep the BFF on one OpenID Connect scheme targeting Microsoft Entra External ID. The user selects Microsoft or Google during the access-request flow, but the actual provider experience remains inside Entra's configured journey rather than adding parallel auth schemes to the BFF.

**Rationale**: The current BFF already uses one OpenID Connect challenge flow. Microsoft guidance for Entra External ID supports inviting external users who later authenticate with Microsoft accounts or enabled social identities such as Google. Reusing the existing tenant-backed flow keeps the WebUI/BFF simple and avoids duplicating session/callback logic.

**Alternatives considered**:
- Add separate Microsoft and Google authentication middleware to the BFF. Rejected because it creates a new parallel auth architecture that the repo does not currently use and is unnecessary if Entra External ID remains the identity broker.
- Defer Google support entirely. Rejected because the spec explicitly requires Microsoft or Google account selection.

## Decision 4: Use Invitation-Based External Identity Provisioning And Explicit App Access Assignment

**Decision**: Provision approved users in Entra by creating a Microsoft Graph invitation, persisting the returned directory metadata, and assigning application access explicitly after invitation creation.

**Rationale**: Microsoft documentation for Entra External ID and B2B onboarding describes invitation-based guest creation as the supported automation path. The same guidance also calls out explicit group/app-role assignment when application access is required. This matches the repo's existing Microsoft.Identity.Web-based auth stack and keeps onboarding aligned with the current token/role model.

**Alternatives considered**:
- Use a purely local approval table and wait for first sign-in to create the Entra user. Rejected because the clarified spec requires external identity registration before first sign-in.
- Use self-service sign-up flows as the primary implementation. Rejected because the requested behavior is explicitly admin approval driven, not self-approved onboarding.

## Decision 5: Map User-Facing Tiers To Existing Entitlement Artifacts Centrally

**Decision**: Preserve the onboarding-tier vocabulary from the spec (`trial`, `road runner`, `admin`) in the approval workflow, but resolve those values centrally to existing application entitlement artifacts such as current plan rows and app roles.

**Rationale**: Repo evidence shows a mismatch today: SQL seed data uses plan names like `Free`, `Plus`, and `Pro`, while the authorization design documents existing app roles such as `DemoUser`, `ProUser`, `Roadrunner`, and `mcr-api-admin`. Central mapping avoids a repo-wide entitlement rename as part of this feature while still honoring the user-facing tier language in the spec.

**Alternatives considered**:
- Rename all existing plans and roles to match the onboarding tiers. Rejected because it broadens the blast radius of this feature and turns onboarding work into a full authorization migration.
- Add a second parallel plan model just for onboarding. Rejected because it would duplicate entitlement logic and increase drift risk.

## Decision 6: Persist Retryable Onboarding State Instead Of Trying To Force A Distributed Transaction

**Decision**: Model onboarding as a durable state machine (`Pending`, `ApprovedPendingOnboarding`, `OnboardingFailed`, `Completed`, plus terminal rejection/disable states where needed) and allow safe retries from `OnboardingFailed`.

**Rationale**: Approval-time onboarding spans SQL persistence, usage seeding, notification dispatch, and Microsoft Graph side effects. Those operations cannot be made into one atomic transaction. The clarified spec explicitly prefers a retryable failed-onboarding state that preserves the approval decision and assigned tier.

**Alternatives considered**:
- Attempt to commit SQL only after every external dependency succeeds. Rejected because it still leaves unavoidable external side effects that can succeed or fail independently.
- Treat partial provisioning as success and rely on support intervention. Rejected because it violates the requirement for diagnosable, retryable onboarding outcomes.