---
id: admin-desktop/configuration
title: Admin Desktop — Config Persistence
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
# Admin Desktop — Config Persistence

`AppConfig` fields (all in `src/lib/config.ts → DEFAULT_CONFIG`). Treat source as authoritative for current values; the table records the configuration surface rather than stable deployment defaults:

| Field | Default |
| --- | --- |
| `apiBaseUrl` | configured cloud API endpoint |
| `authAuthority` | configured Entra authority |
| `authClientId` | configured Admin client ID |
| `authScope` | configured Admin API scope |
| `embeddingProviderEndpoint` | configured OpenAI-compatible embedding endpoint |
| `embeddingModel` | `qwen3-embedding` |
| `tokenizerModelPath` | optional local tokenizer path |
| `graphExtractionEndpoint` / `graphExtractionModel` | graph extraction provider configuration |
| `localProcessorPort` | `8100` |
| `localProcessorWorkingDir` | `""` (must be set to the local-processing-service root) |
| `azureStorageAccountUrl` | configured Blob Storage account endpoint |
| `autoResolveProcessor` | auto-resolve the processor directory when unset |
| `pythonUploadJobSecret` | optional local processor upload credential; never document a value |
| `selectedChromeProfile` | selected browser profile |

Stored in `tauri-plugin-store` → `config.json` (OS app-data dir). Loaded at startup in `main.tsx`; `apiClient.ts` subscribes to changes and updates its base URL live.
