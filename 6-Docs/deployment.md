# Deployment Configuration

This project uses **Pulumi** for Infrastructure-as-Code and **GitHub Actions** for the CI/CD pipeline.
All sensitive values are supplied at runtime through **repository secrets / variables** – **no secrets are stored in source control**.

## Environment Variables Naming Convention

**IMPORTANT**: All environment variables in this project follow a strict naming convention. See [`environment-variables.md`](environment-variables.md) for the **canonical reference** on all environment variable names across all applications (API, BFF, Admin App, Mobile).

### Quick Reference
- **Pattern**: `MCR_<APP>_<VARIABLE>` (e.g., `MCR_API_AZURE_AD_TENANT_ID`)
- **Apps**: `API`, `BFF`, `ADMIN`, `MOBILE`
- **Usage**: Set these via GitHub Secrets, User Secrets (development), or Azure Key Vault (production)

Refer to [`environment-variables.md`](environment-variables.md) for complete variable listings, validation requirements, and setup instructions.

---
## 1. GitHub Secrets

Create the following secrets in the repository or organisation **Settings → Secrets and variables → Actions**:

### Infrastructure & Deployment Secrets
These are used by Pulumi for deploying Azure resources:

| Secret | Purpose |
| --- | --- |
| `AZURE_CLIENT_ID` | Service-principal client ID used by Pulumi's Azure provider and the `azure/login` action. |
| `AZURE_CLIENT_SECRET` | Service-principal client secret. |
| `AZURE_TENANT_ID` | Azure Active Directory tenant ID. |
| `AZURE_SUBSCRIPTION_ID` | Subscription that will contain the resources. |
| `PULUMI_ACCESS_TOKEN` | Personal access token for the Pulumi SaaS backend (https://app.pulumi.com). |

### Application Runtime Secrets
These are injected into the deployed applications at runtime. See [`environment-variables.md`](environment-variables.md) for complete details.

| Secret | MCR Variable | Purpose |
| --- | --- | --- |
| `MCR_API_AZURE_OPENAI_API_KEY` | `MCR_API_AZURE_OPENAI_API_KEY` | Primary/secondary key for the Azure OpenAI resource. |
| `MCR_API_AZURE_SEARCH_API_KEY` | `MCR_API_AZURE_SEARCH_API_KEY` | Admin/query key for the Azure AI Search service. |
| `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_API_KEY` | `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_API_KEY` | Key for the Azure Document Intelligence resource. |
| `MCR_API_AZURE_AD_TENANT_ID` | `MCR_API_AZURE_AD_TENANT_ID` | Azure AD tenant ID for API authentication. |
| `MCR_API_AZURE_AD_CLIENT_ID` | `MCR_API_AZURE_AD_CLIENT_ID` | API app registration client ID. |
| `MCR_API_SQL_CONNECTION_STRING` | `MCR_API_SQL_CONNECTION_STRING` | SQL Server connection string (Azure AD auth required). |
| `MCR_API_APPINSIGHTS_CONNECTION_STRING` | `MCR_API_APPINSIGHTS_CONNECTION_STRING` | Application Insights connection string. |
| `MCR_BFF_CLIENT_SECRET` | `MCR_BFF_CLIENT_SECRET` | BFF app registration client secret. |

> 📝 Additional services (Cosmos DB, Storage, etc.) can be added. See [`environment-variables.md`](environment-variables.md) to define new variables following the `MCR_<APP>_*` pattern.

---
## 2. GitHub Repository Variables (non-secret)

| Variable | Example | Purpose |
| --- | --- | --- |
| `AZURE_OPENAI_ENDPOINT` | `https://my-openai.openai.azure.com` | Base URL for the Azure OpenAI resource. |
| `AZURE_SEARCH_ENDPOINT` | `https://my-search.search.windows.net` | Base URL for the Azure AI Search service. |
| `DOCUMENT_INTELLIGENCE_ENDPOINT` | `https://my-doc-intel.cognitiveservices.azure.com` | Endpoint for Document Intelligence. |

These values are **publicly safe** (they reveal resource names but not keys) and therefore stored as _repository variables_ instead of secrets.

---
## 3. Local Pulumi Config

For local development you can configure the same values with Pulumi CLI:

```powershell
pulumi config set azureOpenAIEndpoint "https://..."   # non-secret
pulumi config set azureOpenAIKey "..." --secret
# ...etc.
```

The GitHub Actions workflow automatically injects all required config values via environment variables, so you do **not** need any `Pulumi.<stack>.yaml` files in the repo.

---
## 4. CI/CD Flow
1. On every push to `main` or `develop` the workflow **builds** the .NET solution and **runs tests**.
2. Pulumi performs a **preview** followed by an **update** (`pulumi up`) against the stack defined in `PULUMI_STACK` (defaults to `dev`).
3. The Pulumi program will:
   • create / update the Azure Resource Group, Container Registry and Container App Environment.
   • build the Docker image for `MotorcycleRAG.API`, push it to the registry, and deploy it to Azure Container Apps.
4. When the update completes, Pulumi outputs the public API URL (see `endpoint` stack output).

---
## 5. Application Environment Variables

**ALL environment variables must follow the `MCR_<APP>_<VARIABLE>` naming convention.**

See [`environment-variables.md`](environment-variables.md) for the **complete, authoritative reference** including:
- All variable names by application (API, BFF, Admin, Mobile)
- Detailed descriptions and examples
- Type classification (Secret vs. Non-Secret)
- Validation requirements
- Setup instructions for development and production

### Security & Setup Instructions

All secrets must be stored securely:

**Development**: Use User Secrets
```powershell
dotnet user-secrets set "MCR_API_AZURE_AD_TENANT_ID" "your-tenant-id" --project 1-Presentation/MotorcycleRAG.API
dotnet user-secrets set "MCR_ADMIN_CLIENT_ID" "your-id" --project 1-Presentation/MotorcycleRAG.Admin
# ... etc
```

**Production**: Use Azure Key Vault or Azure App Configuration
- Set environment variables via Azure App Service "Application Settings"
- Or use Azure Key Vault with Azure App Configuration integration
- Never commit secrets to source control or store in configuration files

## 6. Rotating Secrets
Secrets can be rotated at any time by updating them in GitHub → **Settings → Secrets** and re-running the workflow. No code changes are required.