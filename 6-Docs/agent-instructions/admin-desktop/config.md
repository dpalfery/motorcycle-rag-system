# Admin Desktop — Config Persistence

`AppConfig` fields (all in `src/lib/config.ts → DEFAULT_CONFIG`):

| Field | Default |
|---|---|
| `apiBaseUrl` | `https://localhost:7215` |
| `authAuthority` | `https://login.microsoftonline.com/0f8f8a52-...` |
| `authClientId` | `a86e8458-...` |
| `authScope` | `api://motorcyclerag-api/admin` |
| `embeddingProviderEndpoint` | `http://localhost:11434` |
| `embeddingModel` | `qwen3-embedding` |
| `localProcessorPort` | `8100` |
| `localProcessorWorkingDir` | `""` (must be set to the local-processing-service root) |

Stored in `tauri-plugin-store` → `config.json` (OS app-data dir). Loaded at startup in `main.tsx`; `apiClient.ts` subscribes to changes and updates its base URL live.
