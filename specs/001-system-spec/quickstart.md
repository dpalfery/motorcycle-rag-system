# Quickstart — 001-system-spec

This quickstart describes how to run the system locally for development.

## Prereqs
- .NET SDK 10
- Node.js (LTS)
- (Optional) Azure credentials for real cloud integrations

## Backend API
1) From repo root:
- Build: `dotnet build MotorcycleRAG.sln`
- Run API: `dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj`

2) Health:
- `GET /health`

## Web Application (React 19)
1) From `1-Presentation/MotorcycleRag.WebUI/`:
- Install: `npm install`
- Dev server: `npm run dev`

## Web UI BFF (recommended when using OIDC)
The repo includes a YARP-based BFF that performs OIDC sign-in and forwards the user access token to the API.

- Run BFF: `dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF/MotorcycleRag.WebUI.BFF.csproj`

## Admin Ingestion App (.NET MAUI)

The Motorcycle RAG System includes a .NET MAUI admin application for data ingestion and system management.

### Features

- **Windows-first** .NET MAUI application targeting .NET 10
- **Local processing** capabilities including:
  - PDF chunking and text extraction
  - CSV parsing and structured data processing
  - ONNX Runtime embedding generation using local models
- **Data ingestion workflow**:
  - File picker with validation (type/size constraints)
  - Local chunking + vectorization
  - Upload processed artifacts to ingestion API
- **MCP Server/Tool Management**:
  - Configure MCP servers and tools
  - Enable/disable tools without redeploy
  - Ship configuration updates to the API
- **Job Monitoring**:
  - View ingestion job status
  - Cancel running jobs
  - View processing metrics

### Running the MAUI Admin App

**Prerequisites:**
- .NET 10 SDK with MAUI workload installed
- Windows 10/11 development environment
- Visual Studio 2022 with .NET MAUI workload

**Build and Run:**
```bash
# Navigate to MAUI project directory
cd 1-Presentation/MotorcycleRAG.Admin

# Build the MAUI application
dotnet build MotorcycleRAG.Admin.csproj -t:Run -f net10.0-windows10.0.19041.0

# Or use Visual Studio to build and debug
```

### Authentication

The MAUI admin app uses Entra ID authentication with device code flow:

```bash
# Set required environment variables for authentication
$env:MAUI_AUTH_AUTHORITY="https://login.microsoftonline.com/your-tenant-id"
$env:MAUI_AUTH_CLIENT_ID="your-maui-client-id"
$env:MAUI_AUTH_REDIRECT_URI="msalyour-maui-client-id://auth"
$env:MAUI_AUTH_SCOPES="api://your-api-client-id/.default"
```

### Configuration

The app requires connection to the API endpoints:

```bash
# API Configuration
$env:MAUI_API_BASE_URL="http://localhost:5028"
$env:MAUI_API_RESOURCE="your-api-resource-id"
```

### Local Processing

The MAUI app includes embedded ONNX models for local processing:

- **Embedding Model**: `Resources/Raw/embedding-model.onnx`
- **Chunking Algorithms**: PDF and CSV specific processors
- **Validation**: File type, size, and content validation

### Role-Based Access

The admin app enforces role-based access control:

- **Admin Role**: Full access to all features
- **Data Manager Role**: Access to ingestion features only
- **Viewer Role**: Read-only access to job monitoring

### Development Notes

- The MAUI app is currently in planning phase and will be implemented in later phases
- UI/UX design follows Windows 11 Fluent Design principles
- App configuration is managed through Azure App Configuration
- Audit logging is implemented for all administrative actions

## Environment Variables (local)

### API Environment Variables

For local development, set these environment variables for the API:

```bash
# Azure AI Services
AZURE_OPENAI_ENDPOINT="https://your-dev-openai-endpoint.openai.azure.com/"
AZURE_OPENAI_API_KEY="your-openai-api-key"
AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"
AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME="text-embedding-3-large"

AZURE_SEARCH_ENDPOINT="https://your-dev-search-service.search.windows.net/"
AZURE_SEARCH_API_KEY="your-search-api-key"
AZURE_SEARCH_INDEX_NAME="motorcycle-rag-index"

AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT="https://your-dev-document-intelligence.cognitiveservices.azure.com/"
AZURE_DOCUMENT_INTELLIGENCE_API_KEY="your-document-intelligence-api-key"

# Application Insights
APPLICATION_INSIGHTS_CONNECTION_STRING="your-application-insights-connection-string"

# Database
CONNECTION_STRINGS__DEFAULT="Server=localhost;Database=MotorcycleRAG;User Id=sa;Password=your-password;TrustServerCertificate=True"

# Authentication
AUTHENTICATION__ENTRA_ID__TENANT_ID="your-tenant-id"
AUTHENTICATION__ENTRA_ID__CLIENT_ID="your-client-id"
AUTHENTICATION__ENTRA_ID__CLIENT_SECRET="your-client-secret"

# JWT Settings
JWT__ISSUER="https://your-issuer.com"
JWT__AUDIENCE="your-audience"
JWT__SIGNING_KEY="your-signing-key-with-at-least-32-characters"

# CORS
CORS__ALLOWED_ORIGINS="http://localhost:5173,https://localhost:5001"

# Rate Limiting
RATE_LIMITING__ENABLED=true
RATE_LIMITING__PERIOD=1m
RATE_LIMITING__LIMIT=100
```

### BFF Environment Variables

For the BFF (Backend for Frontend), set these environment variables:

```bash
# OIDC Configuration
OIDC__AUTHORITY="https://your-identity-provider.com"
OIDC__CLIENT_ID="your-bff-client-id"
OIDC__CLIENT_SECRET="your-bff-client-secret"
OIDC__RESPONSE_TYPE="code"
OIDC__SCOPE="openid profile email api-access"

# API Configuration
API__BASE_URL="http://localhost:5028"
API__RESOURCE="your-api-resource-id"

# Session Configuration
SESSION__SECRET="your-session-secret-with-at-least-32-characters"
SESSION__TIMEOUT_MINUTES=60

# CORS for BFF
CORS__ALLOWED_ORIGINS="http://localhost:5173"
```

### Running with Environment Variables

**Windows (PowerShell):**
```powershell
# Set variables and run API
$env:AZURE_OPENAI_ENDPOINT="https://your-dev-openai-endpoint.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY="your-openai-api-key"
# ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Windows (Command Prompt):**
```cmd
set AZURE_OPENAI_ENDPOINT=https://your-dev-openai-endpoint.openai.azure.com/
set AZURE_OPENAI_API_KEY=your-openai-api-key
rem ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Using .env file (recommended for development):**

1. Create a `.env` file in the project root
2. Add your environment variables
3. Use a tool like `dotnet-user-secrets` or `env-cmd` to load them

### Development Secrets Management

- Use `dotnet user-secrets` for development secrets:
  ```bash
  cd 1-Presentation/MotorcycleRAG.API
  dotnet user-secrets init
  dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "https://your-dev-openai-endpoint.openai.azure.com/"
  ```

- For production, use Azure Key Vault or other secure secret management solutions
- Never commit secrets to version control

## Testing
- Run unit tests: `dotnet test MotorcycleRAG.sln`
