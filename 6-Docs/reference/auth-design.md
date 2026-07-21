---
id: reference/auth-design
title: 📘 DOCUMENT 1
doc-type: reference
status: current
owner: Architecture maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# 📘 DOCUMENT 1  
# Authentication & Authorization Design  
**MotorcycleRAG – Azure / Entra ID**

**Status:** Implemented (Jan 2026)
**Audience:** Engineering (Claude Code)  
**Goal:** Implement secure authN/authZ meeting **ASVS Level 2** with **ASVS 3 characteristics where feasible**, using **Microsoft Entra External ID**.

---

## 1. Scope

This document defines:
- Authentication flows for **Web+BFF**, **MotorcycleRAG.MobileApp**, **mcr-api-admin**
- Authorization model enforced by **MotorcycleRAG.API**
- Token handling and trust boundaries
- Rate limiting strategy
- Security invariants that must not be violated

---

## 2. Identity Platform

- **Identity Provider:** Microsoft Entra External ID
- **Tenant:** Single tenant
- **User types:**
  - External users (social IdPs)
  - Entra native users (mcr-api-admins only)

---

## 3. Core Security Principles (Non‑Negotiable)

1. **API is the sole authorization authority**
2. **Only access tokens are used for authorization**
3. **ID tokens are never used for RBAC**
4. **No tokens are exposed to the browser**
5. **Short‑lived access tokens + refresh tokens**
6. **Per‑user rate limiting enforced at API**
7. **mcr-api-admin endpoints require both role AND client isolation**

---

## 4. Authentication Flows

*(Implemented as designed)*

---

## 4.1 Web + BFF

### Flow
- OAuth 2.0 Authorization Code Flow + PKCE
- Implemented in **BFF only**
- SPA uses session‑based auth via BFF endpoints

### Token Handling
| Token | Storage |
|----|----|
| Access token | Server memory / auth session |
| Refresh token | Server memory / auth session |
| ID token | Never forwarded |

### Session
- Cookie name: `__Host-MotorcycleRAG`
- `HttpOnly`, `Secure`, `SameSite=Strict`
- Idle timeout: 20–30 minutes
- Absolute timeout: 8–24 hours

### API Calls
- BFF injects:
```http
Authorization: Bearer <access_token>
```

---

## 4.2 MotorcycleRAG.MobileApp

### Flow
- MSAL Public Client
- Authorization Code + PKCE
- System browser (Implemented: `UseEmbeddedWebView = false`)
- Silent token refresh via MSAL

### Token Storage
- OS‑protected keystore only (Implemented via MSAL Extensions)
- No custom persistence

---

## 4.3 MotorcycleRAG.AdminDesktop

### Flow (Option A – Required)
- MSAL Public Client
- Authorization Code + PKCE
- System browser (Implemented: `UseEmbeddedWebView = false`)
- Entra native users only

### Requirements
- Refresh tokens enabled
- Tokens cached using MSAL secure storage (Implemented via `MsalCacheHelper`)
- No device code flow
- No ID token authorization
- Authority must target the workforce tenant: `https://login.microsoftonline.com/<tenant-id>`
- Desktop redirect URI: `http://localhost`
- Request the explicit admin scope: `api://<API_CLIENT_ID>/admin`
- Do not use `/.default` or legacy `admin_access` scope names

---

## 5. Authorization Model

---

## 5.1 Scopes (OAuth)

Defined on **MotorcycleRAG.API**:

```
api://motorcyclerag-api/read
api://motorcyclerag-api/chat
api://motorcyclerag-api/admin
```

The API app registration may expose both `api://motorcyclerag-api` and `api://<API_CLIENT_ID>` as identifier URIs. For MotorcycleRAG.AdminDesktop, prefer requesting `api://<API_CLIENT_ID>/admin` so the token audience always matches the GUID-based API registration.

---

## 5.2 App Roles

Defined on **MotorcycleRAG.API**:

| Role | Description |
|----|----|
| mcr-api-admin | Full access |
| DemoUser | Read/chat with strict limits |
| ProUser | Read/chat with moderate limits |
| Roadrunner | Read/chat unlimited |

Roles appear in **access tokens** as `roles`.

### Onboarding Feature Mapping

For `specs/002-user-onboarding-approval`, the approved onboarding vocabulary is fixed to the following entitlement mapping:

| Onboarding tier | Plan | App role |
|----|----|----|
| `trial` | `Free` | `DemoUser` |
| `road runner` | `Pro` | `Roadrunner` |
| `admin` | `Pro` | `mcr-api-admin` |

`Plus` and `ProUser` remain valid existing entitlement artifacts elsewhere in the system, but they are not part of the onboarding vocabulary for this feature.

---

## 5.3 Authorization Rules (API)

| Endpoint Type | Required |
|----|----|
| Read | `read` scope |
| Chat | `chat` scope |
| mcr-api-admin | `admin` scope + `roles=mcr-api-admin` + `azp=AdminClientId` |

*(Client Isolation implemented in `Program.cs` via `IsAuthorizedClient` check)*

---

## 6. Client Isolation (Critical)

mcr-api-admin endpoints must validate:

```text
roles contains "mcr-api-admin"
AND
azp == <MotorcycleRAG.AdminDesktop Client ID>
```

This prevents:
- Mobile/Web tokens calling admin APIs
- Privilege escalation via stolen tokens

*(Status: Implemented in API policies)*

---

## 7. Rate Limiting

### Strategy
- Enforced in **MotorcycleRAG.API**
- ASP.NET RateLimiter Middleware
- Redis backing store (In-memory used for MVP, Redis planned)
- Partition key: `oid` (user object ID)

### Limits
| Role | Hourly | Daily |
|----|----|----|
| DemoUser | 50 | 200 |
| ProUser | 500 | 5,000 |
| Roadrunner | Unlimited | Unlimited |
| mcr-api-admin | Unlimited | Unlimited |

*(Status: Implemented via `RateLimitPartition` in `Program.cs`)*

---

## 8. Token Validation Requirements (API)

API must validate:
- `iss`
- `aud`
- `exp`, `nbf`
- `scp`
- `roles`
- `azp`
- CAE claims

Use `Microsoft.Identity.Web`.

---

## 9. Continuous Access Evaluation (CAE)

- Enabled on API app registration
- Allows near‑real‑time revocation:
  - Role removal
  - User disabled
  - Session revocation

No UX impact.

---

## 10. Explicitly Forbidden

- ❌ Using ID tokens for authorization
- ❌ Device Code flow (mcr-api-admin)
- ❌ Tokens in browser
- ❌ Implicit flow
- ❌ ROPC

---

## 11. ASVS Mapping (Summary)

| Area | Level |
|----|----|
| Web Auth | ASVS 3 |
| API AuthZ | ASVS 3 |
| Mobile | ASVS 2 |
| mcr-api-admin | ASVS 2+ |
| Overall | ASVS 2 |

---

