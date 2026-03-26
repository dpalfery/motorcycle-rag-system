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
| MotorcycleRAG.Mobile | Public client |
| MotorcycleRAG.Admin | Public client |

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
- Admin
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

## 4. MotorcycleRAG.Mobile (Public Client)

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

## 5. MotorcycleRAG.Admin (Public Client)

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

Desktop admin clients should request the explicit admin scope URI `api://<API_CLIENT_ID>/admin`.

✅ Grant admin consent

---

## 6. User & Role Assignment

### Admins
- Entra native users only
- Assigned:
  - `Admin` app role
  - Access to Admin app

### External Users
- Assigned roles via:
  - Admin portal
  - Automation
  - Default role = `DemoUser`

---

## 7. Security Checks

✅ Ensure:
- Only Admin app has `admin` scope
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

