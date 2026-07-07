# Admin Desktop — Screen → Endpoint Map

| Screen | Client | Key endpoints |
|---|---|---|
| ProcessorScreen | Rust commands | `GET /health`, `GET /jobs`, `POST /jobs/cleanup`, `POST /control/shutdown` |
| IngestionScreen | `uploadApi` + `api` | `POST /api/file-upload/with-processing`, `GET /api/datapipeline/upload-constraints`, `GET /api/ingestion/jobs/upload` |
| JobsScreen | `api` | `GET /api/ingestion/jobs`, `PUT /api/ingestion/jobs/{id}/retry`, `DELETE /api/ingestion/jobs/{id}` |
| WebSourcesScreen | `api` | `GET/POST /api/admin/web-sources`, `PUT/DELETE /api/admin/web-sources/{id}` |
| McpToolsScreen | `api` | `GET /api/admin/mcp-tools`, `PUT /api/admin/mcp-tools/{id}` |
| UsersScreen | `api` | `GET /api/admin/users` |
| SettingsScreen | `useConfig` store | No API calls — reads/writes `tauri-plugin-store` |
