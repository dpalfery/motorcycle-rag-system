# Environment Variables Naming Strategy

This document defines the canonical naming convention for **ALL environment variables** across the MotorcycleRAG system. It ensures consistency, clarity, and security across all applications (API, BFF, Admin App, Mobile).

---

## General Pattern

```
MCR_<APP>_<VARIABLE>
```

| Component | Description | Values |
| --- | --- | --- |
| `MCR` | Project prefix (MotorcycleRAG) | Fixed |
| `<APP>` | Application identifier | `API`, `BFF`, `ADMIN`, `MOBILE` |
| `<VARIABLE>` | Descriptive variable name | `SCREAMING_SNAKE_CASE` |

### Examples
- `MCR_API_AZURE_OPENAI_ENDPOINT` → Azure OpenAI endpoint for the API
- `MCR_ADMIN_CLIENT_ID` → Admin app's Entra ID client ID
- `MCR_BFF_VALID_ISSUER` → BFF token validation issuer
- `MCR_MOBILE_AUTH_AUTHORITY` → Mobile app's authentication authority

---

## Security Rules (Non-Negotiable)

1. **Secrets NEVER in code**: All sensitive values (keys, tokens, connection strings) must come from environment variables ONLY.
2. **Fail-fast validation**: If a required environment variable is missing, the application must throw an `InvalidOperationException` with a clear message specifying which variable to set.
3. **No placeholder values**: Do not provide default or placeholder values in code. Require explicit user setup.
4. **User Secrets for development**: Use `dotnet user-secrets` to set sensitive values locally.
5. **Environment-only loading**: No configuration files (appsettings.json, .env) should contain sensitive data. They may reference the structure but never actual secrets.

---

## MCR_API_* Variables

**Application**: `1-Presentation/MotorcycleRAG.API`

### Authentication & Authorization
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_AZURE_AD_TENANT_ID` | `00000000-0000-0000-0000-000000000000` | Azure AD tenant ID for token validation. | Yes | Secret |
| `MCR_API_AZURE_AD_CLIENT_ID` | `00000000-0000-0000-0000-000000000000` | API app registration client ID. | Yes | Secret |

### Azure Services (Endpoints)
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_AZURE_OPENAI_ENDPOINT` | `https://my-openai.openai.azure.com` | Base URL for Azure OpenAI resource. | No* | Non-Secret |
| `MCR_API_AZURE_SEARCH_ENDPOINT` | `https://my-search.search.windows.net` | Base URL for Azure AI Search service. | No* | Non-Secret |
| `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT` | `https://my-di.cognitiveservices.azure.com` | Endpoint for Document Intelligence. | No* | Non-Secret |
| `MCR_API_AZURE_FOUNDRY_ENDPOINT` | `https://my-foundry.cognitiveservices.azure.com` | Endpoint for Azure AI Foundry. | No* | Non-Secret |

\* **Degraded Mode**: The API will start without these endpoints configured, but features requiring these services will fail with a "not yet configured" error. This allows development to proceed before Azure resources are provisioned.

### Azure Services (Keys)
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_AZURE_OPENAI_API_KEY` | `xxxxxxxxxxxxx` | Azure OpenAI resource key. | Yes | Secret |
| `MCR_API_AZURE_SEARCH_API_KEY` | `xxxxxxxxxxxxx` | Azure AI Search admin/query key. | Yes | Secret |
| `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_API_KEY` | `xxxxxxxxxxxxx` | Document Intelligence key. | Yes | Secret |

### Database
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_SQL_CONNECTION_STRING` | `Server=tcp:...;Database=...;User ID=...;Password=...;` | SQL Server connection string. | Yes | Secret |

### Observability
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_APPINSIGHTS_CONNECTION_STRING` | `InstrumentationKey=...;IngestionEndpoint=...` | Application Insights telemetry connection string. | No | Secret |

---

## MCR_ADMIN_* Variables

**Application**: `1-Presentation/MotorcycleRAG.Admin`

### Authentication
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_ADMIN_CLIENT_ID` | `00000000-0000-0000-0000-000000000000` | Admin app's Entra ID client ID. | Yes | Secret |
| `MCR_ADMIN_AUTHORITY` | `https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000` | Workforce tenant authority URL used by the admin app. | Yes | Non-Secret |
| `MCR_ADMIN_API_SCOPE` | `api://00000000-0000-0000-0000-000000000000/admin` | Explicit admin scope for token requests. | Yes | Non-Secret |

Use the tenant that issues the API's admin tokens, and request the explicit admin scope URI. Do not use `/.default` or legacy `admin_access` scope names for the MAUI admin app.

### API Connectivity
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_ADMIN_API_BASE_URL` | `http://localhost:5028` (dev) or `https://api.yourdomain.com` (prod) | API base URL for admin app. | Yes | Non-Secret |

---

## MCR_BFF_* Variables

**Application**: `1-Presentation/MotorcycleRag.WebUI.BFF`

### Authentication & Token Validation
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_BFF_VALID_AUDIENCE` | `00000000-0000-0000-0000-000000000000` | API client ID for token audience validation. | Yes | Non-Secret |
| `MCR_BFF_VALID_ISSUER` | `https://login.microsoftonline.com/tenant-id/v2.0` | Token issuer for validation. | Yes | Non-Secret |
| `MCR_BFF_CLIENT_SECRET` | `xxxxxxxxxxxxx` | BFF app registration client secret. | Yes | Secret |

### API Reverse Proxy
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_BFF_API_BASE_URL` | `http://localhost:5028` (dev) or `https://api.yourdomain.com` (prod) | Target API URL for reverse proxy (YARP). | Yes | Non-Secret |

---

## MCR_MOBILE_* Variables

**Application**: `1-Presentation/MotorcycleRag.Mobile` (if applicable)

### Authentication
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_MOBILE_AUTH_AUTHORITY` | `https://tenant-name.b2clogin.com` | Entra External ID / B2C authority. | Yes | Non-Secret |
| `MCR_MOBILE_CLIENT_ID` | `00000000-0000-0000-0000-000000000000` | Mobile app's client ID. | Yes | Secret |
| `MCR_MOBILE_REDIRECT_URI` | `msamobile://auth` | OAuth redirect URI. | Yes | Non-Secret |
| `MCR_MOBILE_AUTH_SCOPES` | `openid profile email api://api-id/.default` | OAuth scopes for API access. | Yes | Non-Secret |

### API Connectivity
| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_MOBILE_API_BASE_URL` | `http://localhost:5028` (dev) or production URL | API base URL for mobile app. | Yes | Non-Secret |
| `MCR_MOBILE_API_RESOURCE` | `00000000-0000-0000-0000-000000000000` | API resource identifier for tokens. | Yes | Non-Secret |

---

## Setting Environment Variables

### Local Development (Recommended)

Use `dotnet user-secrets` to store sensitive values securely:

```powershell
# For the API
dotnet user-secrets set "MCR_API_AZURE_AD_TENANT_ID" "your-tenant-id" --project 1-Presentation/MotorcycleRAG.API
dotnet user-secrets set "MCR_API_AZURE_AD_CLIENT_ID" "your-client-id" --project 1-Presentation/MotorcycleRAG.API
dotnet user-secrets set "MCR_API_AZURE_OPENAI_API_KEY" "your-key" --project 1-Presentation/MotorcycleRAG.API
# ... etc for other secrets

# For the Admin app
dotnet user-secrets set "MCR_ADMIN_CLIENT_ID" "your-admin-client-id" --project 1-Presentation/MotorcycleRAG.Admin
dotnet user-secrets set "MCR_ADMIN_AUTHORITY" "https://login.microsoftonline.com/your-tenant-id" --project 1-Presentation/MotorcycleRAG.Admin
dotnet user-secrets set "MCR_ADMIN_API_SCOPE" "api://your-api-client-id/admin" --project 1-Presentation/MotorcycleRAG.Admin
```

User secrets are stored in platform-specific secure locations:
- **Windows**: `%APPDATA%\Microsoft\UserSecrets\{project-id}\secrets.json`
- **macOS/Linux**: `~/.microsoft/usersecrets/{project-id}/secrets.json`

### Production Deployment

Set environment variables via:
- **Azure App Service**: Application Settings (not Connection Strings)
- **Azure Container Apps**: Environment Variables in the container definition
- **Kubernetes**: Secrets and ConfigMaps
- **Manual/On-Premises**: System environment variables or `.env` file (NOT committed to repo)

### GitHub Actions / CI/CD

GitHub Secrets are injected as environment variables during workflows. Reference them with the exact names (e.g., `${{ secrets.MCR_API_AZURE_AD_CLIENT_ID }}`).

---

## Validation & Error Handling

Every application must validate required environment variables **at startup** and fail fast with clear error messages.

### Example (C#):
```csharp
var clientId = Environment.GetEnvironmentVariable("MCR_API_AZURE_AD_CLIENT_ID")
    ?? throw new InvalidOperationException(
        "REQUIRED: Set the MCR_API_AZURE_AD_CLIENT_ID environment variable. " +
        "This should be your API app registration client ID in Entra ID. " +
        "For local development, use: dotnet user-secrets set \"MCR_API_AZURE_AD_CLIENT_ID\" \"your-id\"");
```

### Key Points:
- ✅ **Fail fast**: Throw before the app starts
- ✅ **Clear message**: Specify which variable is missing
- ✅ **Instructions included**: Tell users how to set it
- ✅ **No defaults**: Never provide placeholder values

---

## Migration from Old Naming

If migrating from older variable names (e.g., `ENTRA_CLIENT_ID`, `API_SCOPE`), follow this checklist:

1. Update all code references to use new `MCR_*` names
2. Update all documentation (deployment.md, runbooks, etc.)
3. Update GitHub Secrets and Actions workflows
4. Test locally with `dotnet user-secrets`
5. Redeploy to all environments with new variable names
6. Old variables can be removed after verification

---

## Cross-Reference

For Azure resource naming conventions (e.g., `mcr-rag-dev-eus2-rg`), see `azure-naming-standards.md`.

For deployment procedures and GitHub Actions setup, see `deployment.md`.

---

## Local Processing Service (Python) — Hybrid Embedding Pipeline

**Application**: `2-Application/local-processing-service`

> These variables are read directly from the OS environment (or a local `.env` file, never committed).
> See `.env.example` in the local-processing-service directory for a ready-to-copy template.

### Embedding Backend

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `EMBEDDING_BACKEND` | `ollama` | Selects which embedder to use at ingestion time. Valid: `ollama` \| `foundry_local` \| `deepinfra`. | No | Non-Secret |

### Ollama (Local GPU)

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `OLLAMA_BASE_URL` | `http://localhost:11434` | Base URL of the local Ollama server. | No | Non-Secret |
| `OLLAMA_MODEL` | `qwen3-embedding` | Ollama model name for embeddings. | No | Non-Secret |

### Azure AI Foundry Local

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `AZURE_FOUNDRY_LOCAL_ENDPOINT` | `http://localhost:5272` | Base URL of the Azure AI Foundry Local OpenAI-compatible server. | No | Non-Secret |
| `AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL` | `qwen3-embedding` | Model name loaded in Azure AI Foundry Local. | No | Non-Secret |

### DeepInfra (Remote Query-Time Embeddings)

| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `DEEPINFRA_API_KEY` | `xxxxxxxxxxxxx` | DeepInfra API key for Qwen3-Embedding-4B. | Yes (if backend=deepinfra) | Secret |
| `DEEPINFRA_BASE_URL` | `https://api.deepinfra.com/v1/openai` | DeepInfra OpenAI-compatible endpoint. | Yes (if backend=deepinfra) | Non-Secret |
| `DEEPINFRA_EMBEDDING_MODEL` | `Qwen/Qwen3-Embedding-4B` | DeepInfra embedding model name. | Yes (if backend=deepinfra) | Non-Secret |

### Azure AI Search (Direct Upload)

| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `AZURE_SEARCH_ENDPOINT` | `https://<service>.search.windows.net` | Azure AI Search service endpoint. | Yes (for indexing) | Non-Secret |
| `AZURE_SEARCH_INDEX` | `motorcycle-index` | Target search index (must have 3584-dim vector field). | No | Non-Secret |
| `AZURE_SEARCH_KEY` | `xxxxxxxxxxxxx` | Azure AI Search API key. Leave blank to use `DefaultAzureCredential`. | No | Secret |

### .NET API — Azure AI Foundry Serverless (Chat)

> These variables are read by `AzureOpenAIClientWrapper` in `4-Persistence/MotorcycleRAG.Persistence`.

| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_FOUNDRY_ENDPOINT` | `https://<project>.services.ai.azure.com/models` | Azure AI Foundry serverless endpoint for chat completions. | Yes | Non-Secret |
| `MCR_API_AZURE_FOUNDRY_ENDPOINT` | `https://my-foundry.cognitiveservices.azure.com` | Alternative/legacy name used by some components & tests. | No | Non-Secret |
| `MCR_API_FOUNDRY_CHAT_MODEL` | `gpt-4o-mini` | Chat model deployment name on Azure AI Foundry (used when deploymentName param is blank). | Yes (unless passed as deploymentName) | Non-Secret |

### .NET API — DeepInfra Embeddings

> These variables are read by `AzureOpenAIClientWrapper` in `4-Persistence/MotorcycleRAG.Persistence`.

| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `DEEPINFRA_API_KEY` | `xxxxxxxxxxxxx` | DeepInfra API key (shared with Python service). | Yes | Secret |
| `DEEPINFRA_BASE_URL` | `https://api.deepinfra.com/v1/openai` | DeepInfra OpenAI-compatible endpoint. | Yes | Non-Secret |
| `DEEPINFRA_EMBEDDING_MODEL` | `Qwen/Qwen3-Embedding-4B` | DeepInfra embedding model name (used when GetEmbeddingsAsync model param is blank). | Yes (unless passed as method arg) | Non-Secret |
