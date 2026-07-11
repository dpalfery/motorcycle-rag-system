# Admin Desktop — Screen → Endpoint Map

| Screen | Client | Key endpoints |
| --- | --- | --- |
| ProcessorScreen | Rust commands | `GET /health`, `GET /jobs`, `POST /jobs/cleanup`, `POST /control/shutdown` |
| IngestionScreen | `api` | `GET /api/ingestion/jobs/upload-constraints`, `GET/POST /api/ingestion/jobs`, `DELETE /api/ingestion/jobs/{id}` |
| JobsScreen | `api` | `GET /api/ingestion/jobs`, `GET /api/ingestion/jobs/{id}`, `POST /api/ingestion/jobs/{id}/retry`, `POST /api/ingestion/jobs/{id}/cancel`, `DELETE /api/ingestion/jobs/{id}` |
| WebSourcesScreen | `api` | `GET/POST /api/admin/web-sources`, `PUT/DELETE /api/admin/web-sources/{id}` |
| McpToolsScreen | `api` | `GET /api/admin/mcp-tools`, `PUT /api/admin/mcp-tools/{id}` |
| UsersScreen | `api` | `GET /api/admin/users` |
| SettingsScreen | `useConfig` store | No API calls — reads/writes `tauri-plugin-store` |
