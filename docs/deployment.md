# Deployment Configuration

This project uses **Pulumi** for Infrastructure-as-Code and **GitHub Actions** for the CI/CD pipeline.
All sensitive values are supplied at runtime through **repository secrets / variables** – **no secrets are stored in source control**.

---
## 1. GitHub Secrets
Create the following secrets in the repository or organisation **Settings → Secrets and variables → Actions**:

| Secret | Purpose |
| --- | --- |
| `AZURE_CLIENT_ID` | Service-principal client ID used by Pulumi's Azure provider and the `azure/login` action. |
| `AZURE_CLIENT_SECRET` | Service-principal client secret. |
| `AZURE_TENANT_ID` | Azure Active Directory tenant ID. |
| `AZURE_SUBSCRIPTION_ID` | Subscription that will contain the resources. |
| `PULUMI_ACCESS_TOKEN` | Personal access token for the Pulumi SaaS backend (https://app.pulumi.com). |
| `AZURE_OPENAI_KEY` | Primary/secondary key for the Azure OpenAI resource. |
| `AZURE_SEARCH_KEY` | Admin/query key for the Azure AI Search service. |
| `DOCUMENT_INTELLIGENCE_KEY` | Key for the Azure Document Intelligence (Form Recognizer) resource. |
| `APP_INSIGHTS_CONNECTION_STRING` | Application Insights connection string. |
| `LOG_ANALYTICS_CUSTOMER_ID` | Workspace ID for Log Analytics (used by Container Apps logs). |
| `LOG_ANALYTICS_SHARED_KEY` | Primary key for the Log Analytics workspace. |

> 📝 If you need additional services (Cosmos DB, Storage, etc.) add their keys here and reference them inside `infrastructure/Program.cs`.

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

```bash
pulumi config set azureOpenAIEndpoint    "https://..."   # non-secret
pulumi config set azureOpenAIKey         "..." --secret
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
## 5. Rotating Secrets
Secrets can be rotated at any time by updating them in GitHub → **Settings → Secrets** and re-running the workflow. No code changes are required.