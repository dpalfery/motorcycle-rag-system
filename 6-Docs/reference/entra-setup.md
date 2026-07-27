---
id: reference/entra-setup
title: 📘 DOCUMENT 2
doc-type: reference
status: current
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# 📘 DOCUMENT 2  
# Microsoft Entra App Registration Design & Setup

**Audience:** Engineering / Platform  
**Goal:** Correctly configure Entra app registrations to support the above design.

---

## 1. Required App Registrations

| App | Type |
|----|----|
| MotorcycleRAG.API | Web API |
| MotorcycleRAG.Web.BFF | Web / Confidential |
| MotorcycleRAG.MobileApp | Public client |
| MotorcycleRAG.AdminDesktop | Public client |

---

## 2. MotorcycleRAG.API (Web API)

### Create App Registration
- Type: Web API
- Supported account types: This tenant only

### Expose an API
- Application ID URIs:
```
api://motorcyclerag-api
api://<API_CLIENT_ID>
```

### Scopes
| Name | Admin consent |
|----|----|
| read | No |
| chat | No |
| admin | Yes |

---

### App Roles
Create:
- mcr-api-admin
- DemoUser
- ProUser
- Roadrunner

✅ Assignable to **Users/Groups**

---

### Token Configuration
- Add optional claims:
  - `roles`
- Enable **Continuous Access Evaluation**

---

## 3. MotorcycleRAG.Web.BFF (Confidential Client)

### Platform
- Web

### Redirect URIs
```
https://<bff-host>/signin-oidc
https://<bff-host>/signout-callback-oidc
```

### Authentication
- Authorization Code + PKCE
- Client secret required

### API Permissions
- Delegated:
  - `read`
  - `chat`

✅ Grant admin consent

---

## 4. MotorcycleRAG.MobileApp (Public Client)

### Platform
- Mobile and Desktop

### Redirect URI
```
msal<MOBILE_CLIENT_ID>://auth
```

### Authentication
- Authorization Code + PKCE
- No client secret

### API Permissions
- Delegated:
  - `read`
  - `chat`

---

## 5. MotorcycleRAG.AdminDesktop (Public Client)

### Platform
- Mobile and Desktop

### Redirect URI
```
http://localhost
```

### Authentication
- Authorization Code + PKCE
- No client secret

### API Permissions
- Delegated:
  - `read`
  - `chat`
  - `admin`

MotorcycleRAG.AdminDesktop clients should request the explicit admin scope URI `api://<API_CLIENT_ID>/admin`.

✅ Grant admin consent

---

## 6. User & Role Assignment

### Admins
- Entra native users only
- Assigned:
  - `mcr-api-admin` app role
  - Access to MotorcycleRAG.AdminDesktop

### External Users
- Assigned roles via:
  - Admin portal
  - Automation
  - Default role = `DemoUser`

### Onboarding Feature Mapping

For `specs/002-user-onboarding-approval`, use this canonical mapping when approval assigns a user-facing tier:

| Onboarding tier | SQL plan | Entra/API app role |
|----|----|----|
| `trial` | `Free` | `DemoUser` |
| `road runner` | `Pro` | `Roadrunner` |
| `admin` | `Pro` | `mcr-api-admin` |

`Plus` and `ProUser` remain configured for existing system behavior, but onboarding for this feature must not emit those values.

---

## 7. Security Checks

✅ Ensure:
- Only MotorcycleRAG.AdminDesktop has `admin` scope
- Only Admin users have `Admin` role
- API rejects tokens without correct `azp`

---

## 8. Validation Checklist

Before production:
- ✅ Tokens contain correct `scp`, `roles`, `azp`
- ✅ Admin endpoints reject Web/Mobile tokens
- ✅ Rate limiting enforced per `oid`
- ✅ Token revocation works (CAE)

---

