---
id: admin-desktop/api-client
title: Admin Desktop — Cloud API Client
doc-type: reference
status: current
component: MotorcycleRAG Admin Desktop
owner: Admin Desktop maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Admin Desktop — Cloud API Client

Two axios instances sharing the same interceptor (base URL from `useConfig` + Bearer token from `useAuth`):

- **`api`** — `timeout: 30_000`. Use for all standard API calls.
- **`uploadApi`** — `timeout: 0` (unlimited). Use for file uploads **only**.

**Important:** never set `Content-Type: multipart/form-data` manually on a FormData upload. Axios sets it with the correct `boundary` automatically. Setting it manually strips the boundary and the server cannot parse the body.

The base URL and token provider are wired in `main.tsx`:

```ts
setTokenProvider(getAccessToken);
useConfig.getState().load().then(() => setApiBaseUrl(...));
useConfig.subscribe((s) => setApiBaseUrl(s.config.apiBaseUrl));
```
