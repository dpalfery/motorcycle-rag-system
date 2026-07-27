---
id: webui-bff/requirements
title: MotorcycleRAG Web UI BFF Requirements
doc-type: requirements
status: current
component: MotorcycleRAG Web UI BFF
source-root: 1-Presentation/MotorcycleRag.WebUI.BFF
owner: Web UI maintainers
last-reviewed: 2026-07-21
code-refs:
  - AuthController
  - HostHeaderValidationMiddleware
  - YarpServiceConfiguration
  - DataProtectionHealthCheck
api-endpoints:
  - GET /auth/login
  - POST /auth/logout
  - GET /auth/me
decided-by: []
supersedes: []
---

# MotorcycleRAG Web UI BFF Requirements

## Introduction

The Web UI BFF is the browser's single origin for the MotorcycleRAG web experience. It owns interactive sign-in, session custody, web security policy, and authenticated proxying to the platform API. These requirements govern the host itself; the SPA's own requirements are in [Web UI requirements](../MotorcycleRag.WebUI/requirements.md).

## Requirements

### Requirement 1: Single-origin token custody

**User Story:** As a security-conscious operator, I want API credentials held server-side, so that a browser-side compromise cannot exfiltrate an access token.

#### Acceptance Criteria

1.1. WHEN the browser calls an API route THEN the BFF SHALL proxy `/api/*` to the configured destination and SHALL attach the session's access token as a bearer credential server-side.
1.2. WHEN the BFF serves any response to the browser THEN the system SHALL NOT expose an access token in the response body, a cookie readable by script, or any browser-accessible storage.
1.3. WHEN a proxied request is issued THEN the BFF SHALL set the downstream `Host` header to the configured API host.
1.4. IF no access token is present on the session THEN the BFF SHALL forward the request without an `Authorization` header rather than failing the proxy stage.

### Requirement 2: Approval-aware session state

**User Story:** As an end user, I want the interface to tell me whether I am signed in and whether my access is approved, so that a pending approval is not presented as a sign-in failure.

#### Acceptance Criteria

2.1. WHEN the SPA requests session state THEN the BFF SHALL respond to `GET /auth/me` with HTTP 200 in all cases, including signed-out, and SHALL convey state in the body.
2.2. WHEN a session is established and the API confirms approval THEN the BFF SHALL report `sessionAuthenticated`, `accessApproved`, and `approvalStatus` of `Approved`.
2.3. IF the API returns 403 or 401 for the approval lookup THEN the BFF SHALL report `approvalStatus` of `ApprovalRequired` or `ApiUnauthorized` respectively, with `sessionAuthenticated` remaining true.
2.4. IF the access token is missing, the API base address is unconfigured, the API returns an unexpected status, or the lookup throws THEN the BFF SHALL report `approvalStatus` of `Unknown` and SHALL log the cause at warning or error.
2.5. WHEN the approval lookup fails for any reason THEN the BFF SHALL NOT fail the request or sign the user out.

### Requirement 3: Sign-in and sign-out

**User Story:** As an end user, I want to sign in and out through the application origin, so that my session is established and cleared reliably.

#### Acceptance Criteria

3.1. WHEN a user starts sign-in THEN the BFF SHALL issue an OpenID Connect challenge and SHALL return the user to the requested return path, or to `/` when none is supplied.
3.2. WHEN a user signs out THEN the BFF SHALL sign out both the cookie and the OpenID Connect authentication schemes.

### Requirement 4: Web security policy

**User Story:** As a security-conscious operator, I want browser-facing protections applied uniformly, so that no route can be served without them.

#### Acceptance Criteria

4.1. WHEN the host starts THEN the BFF SHALL apply forwarded-header processing before any policy that depends on scheme or host.
4.2. WHEN a request arrives THEN the BFF SHALL validate the `Host` header against the configured allowlist before routing.
4.3. IF the configured host allowlist parses to an empty set and is not the wildcard THEN the BFF SHALL fail to start rather than accept all hosts.
4.4. WHEN any response is emitted THEN the BFF SHALL apply HSTS, frame, content-type, referrer, permissions, and Content-Security-Policy headers, and the production policy SHALL be no weaker than the development policy.
4.5. WHEN a cross-origin request is evaluated THEN the BFF SHALL permit only configured origins, methods, and headers.

### Requirement 5: Session continuity

**User Story:** As an operator, I want sessions to survive restarts and scale-out, so that a deployment does not sign every user out.

#### Acceptance Criteria

5.1. WHEN the host runs outside Development THEN the BFF SHALL persist data-protection keys to the configured durable store.
5.2. IF durable key persistence is required by the environment but not configured THEN the host SHALL fail to start rather than fall back to ephemeral keys.
5.3. WHEN health is requested THEN the BFF SHALL report data-protection key-store reachability through an injected probe, and SHALL NOT construct credentials or SDK clients during the health request.

### Requirement 6: Quality verification

**User Story:** As a developer, I want the BFF's security and session behaviour under test, so that regressions surface before release.

#### Acceptance Criteria

6.1. WHEN the BFF test suite runs THEN it SHALL cover each `approvalStatus` branch of the session-state endpoint.
6.2. WHEN the BFF test suite runs THEN it SHALL cover host-header allowlist acceptance, rejection, and the empty-allowlist start-up failure.
6.3. WHEN the BFF test suite runs THEN it SHALL cover data-protection configuration and health-probe states without contacting a live key store.
