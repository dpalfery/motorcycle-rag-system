# MotorcycleRAG Admin Desktop

MotorcycleRAG Admin Desktop is the operator-facing desktop application for local-first knowledge ingestion and administration. It is a Tauri 2 application: a React/Vite webview provides the UI, while a Rust host supplies native capabilities, secure token storage, process supervision, and the local ingestion queue.

Its primary responsibility is running the Python Local Processing Service on the operator's machine and presenting its state. When an operator selects a PDF manual or CSV specification file, the app creates the corresponding API ingestion job and publishes a file-and-manifest pair to the local processor's watch folder. The Python service performs the extraction and reports progress back through the MotorcycleRAG API.

The application also provides authenticated administration screens for processor operations, web sources, MCP tools, users, and configuration.

## Main components

- `src/` — React 19 UI, routing, authentication state, API client, and Tauri IPC clients.
- `src-tauri/` — Tauri host, Entra ID token/keychain operations, processor lifecycle commands, file picker, and watch-folder queue writer.
- `../../2-Application/local-processing-service/` — separately maintained Python/FastAPI processor launched and supervised by this app; it is not Rust code.

## Documentation

- [Developer onboarding](../../6-Docs/MotorcycleRAG.AdminDesktop/onboarding.md)
- [Architecture](../../6-Docs/MotorcycleRAG.AdminDesktop/architecture.md)
- [Requirements](../../6-Docs/MotorcycleRAG.AdminDesktop/requirements.md)
- [Authentication](../../6-Docs/MotorcycleRAG.AdminDesktop/authentication.md)
- [Configuration](../../6-Docs/MotorcycleRAG.AdminDesktop/configuration.md)
- [API client](../../6-Docs/MotorcycleRAG.AdminDesktop/api-client.md)
- [Endpoint map](../../6-Docs/MotorcycleRAG.AdminDesktop/endpoint-map.md)
- [UI conventions](../../6-Docs/MotorcycleRAG.AdminDesktop/ui-conventions.md)
