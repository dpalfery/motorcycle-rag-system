# Azure Configuration Migration Guide

## Overview

This document describes the changes made to fix placeholder Azure endpoint URLs in the Motorcycle RAG application. The application now enforces strict validation of Azure service endpoints and loads them from environment variables at runtime rather than relying on placeholder values in configuration files.

## Problem Statement

**Previous Issue:**
All `appsettings.json` files contained placeholder Azure endpoint URLs that looked like valid URLs but didn't actually work:

```json
"AzureAI": {
  "FoundryEndpoint": "https://your-foundry-endpoint.cognitiveservices.azure.com/",
  "OpenAIEndpoint": "https://your-openai-endpoint.openai.azure.com/",
  "SearchServiceEndpoint": "https://your-search-service.search.windows.net/",
  "DocumentIntelligenceEndpoint": "https://your-document-intelligence.cognitiveservices.azure.com/"
}
```

These placeholders would cause runtime failures when the application tried to call Azure services with invalid endpoints.

## Changes Made

### 1. Configuration Files Updated

All `appsettings.json` files now have **empty** Azure endpoint values:

**File:** `1-Presentation/MotorcycleRAG.API/appsettings.json`
```json
"AzureAI": {
  "FoundryEndpoint": "",
  "OpenAIEndpoint": "",
  "SearchServiceEndpoint": "",
  "DocumentIntelligenceEndpoint": ""
}
```

**File:** `1-Presentation/MotorcycleRAG.API/appsettings.Development.json`
```json
"AzureAI": {
  "FoundryEndpoint": "",
  "OpenAIEndpoint": "",
  "SearchServiceEndpoint": "",
  "DocumentIntelligenceEndpoint": ""
}
```

### 2. Program.cs Configuration Validation

Added a new `ValidateAndPopulateAzureAIConfiguration()` method in `Program.cs` that:

1. **Loads from environment variables** at startup
2. **Validates endpoint format** (must be HTTPS URLs)
3. **Validates URI structure** (must be parseable as valid URLs)
4. **Fails fast** with clear error messages if configuration is invalid

```csharp
private static void ValidateAndPopulateAzureAIConfiguration(
    IConfiguration configuration,
    IHostEnvironment environment)
{
    // Load Azure AI endpoints from environment variables
    var openAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? (configuration["AzureAI:OpenAIEndpoint"] != "" ? configuration["AzureAI:OpenAIEndpoint"] : null);

    // ... similar for Search, DocumentIntelligence, Foundry endpoints

    // Validate each endpoint
    ValidateEndpoint("OpenAI", openAIEndpoint, "AZURE_OPENAI_ENDPOINT", environment.IsProduction());
    // ... validate other endpoints
}
```

### 3. Endpoint Validation Logic

The `ValidateEndpoint()` method performs three checks:

1. **Not Empty:** Endpoint must be configured
   ```
   Azure OpenAI endpoint is not configured. Set the AZURE_OPENAI_ENDPOINT environment variable.
   ```

2. **HTTPS Only:** Must use HTTPS protocol
   ```
   Azure OpenAI endpoint must use HTTPS protocol. Current endpoint: http://my-endpoint.com/
   ```

3. **Valid URI:** Must be a properly formatted URL
   ```
   Azure OpenAI endpoint is not a valid HTTPS URL. Current endpoint: not-a-url
   ```

### 4. Documentation Updated

**File:** `specs/001-system-spec/quickstart.md`

Complete rewrite of the "API Environment Variables" section with:

- Clear explanation of required vs optional variables
- Grouped by purpose (Endpoints, Keys, Authentication)
- Three methods for setting environment variables:
  - User Secrets (recommended)
  - Environment file (.env)
  - Direct environment variables
- Example error messages and troubleshooting
- Validation rules explained

## Environment Variables Required

### Critical - Must Be Set

These environment variables are **required** for the API to start:

```powershell
# Azure OpenAI Service endpoints (HTTPS only)
AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com/"
AZURE_SEARCH_ENDPOINT="https://<resource>.search.windows.net/"
AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT="https://<resource>.cognitiveservices.azure.com/"
AZURE_FOUNDRY_ENDPOINT="https://<resource>.cognitiveservices.azure.com/"

# API Keys
AZURE_OPENAI_API_KEY="<key>"
AZURE_SEARCH_API_KEY="<key>"
AZURE_DOCUMENT_INTELLIGENCE_API_KEY="<key>"

# Azure AD
AZURE_AD_TENANT_ID="<tenant-id>"
AZURE_AD_CLIENT_ID="<api-client-id>"
```

### Setting Environment Variables - Three Options

#### Option A: User Secrets (Recommended for Development)

```powershell
cd 1-Presentation/MotorcycleRAG.API
dotnet user-secrets init
dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "https://your-endpoint.openai.azure.com/"
dotnet user-secrets set "AZURE_OPENAI_API_KEY" "your-key"
# ... set other variables
```

User Secrets are stored securely outside the repository and override `appsettings.json` during development.

#### Option B: Environment File

Create a `.env` file (not committed to git):

```bash
AZURE_OPENAI_ENDPOINT=https://your-endpoint.openai.azure.com/
AZURE_OPENAI_API_KEY=your-key
AZURE_SEARCH_ENDPOINT=https://your-search.search.windows.net/
# ... other variables
```

Load before running:

```powershell
Get-Content .env | ForEach-Object {
  if (-not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#')) {
    $name, $value = $_.Split('=')
    [Environment]::SetEnvironmentVariable($name, $value, "Process")
  }
}
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

#### Option C: Direct Environment Variables

```powershell
$env:AZURE_OPENAI_ENDPOINT="https://your-endpoint.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY="your-key"
# ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

## Migration Steps

If you were using the old placeholder configuration:

1. **Do NOT update** the placeholders in `appsettings.json` - they're now empty
2. **Set environment variables** using one of the three methods above
3. **Run the application** - it will validate configuration on startup
4. **If validation fails**, you'll get a clear error message showing:
   - Which endpoint is missing/invalid
   - The environment variable name to set
   - Example of correct format

## Backward Compatibility

- Old placeholder values in config files are **no longer used**
- Environment variables **take precedence** over config file values
- For development: Use User Secrets or .env files
- For production: Set environment variables in deployment platform
  - Azure App Service: Use Application Settings
  - Kubernetes: Use ConfigMap or Secrets
  - Docker: Use environment variables in compose file
  - Container apps: Use Azure Container Instances environment variables

## Validation Examples

### Success Case

```powershell
$env:AZURE_OPENAI_ENDPOINT="https://my-openai.openai.azure.com/"
$env:AZURE_SEARCH_ENDPOINT="https://my-search.search.windows.net/"
$env:AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT="https://my-di.cognitiveservices.azure.com/"
$env:AZURE_FOUNDRY_ENDPOINT="https://my-foundry.cognitiveservices.azure.com/"
$env:AZURE_AD_TENANT_ID="12345678-1234-1234-1234-123456789012"
$env:AZURE_AD_CLIENT_ID="87654321-4321-4321-4321-210987654321"
dotnet run --project 1-Presentation/MotorcycleRAG.API
# Application starts successfully
```

### Failure Case - Missing Endpoint

```powershell
# Only set some variables, missing AZURE_OPENAI_ENDPOINT
$env:AZURE_SEARCH_ENDPOINT="https://my-search.search.windows.net/"
# ... other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Error output:
# Azure OpenAI endpoint is not configured.
# Set the AZURE_OPENAI_ENDPOINT environment variable.
# For local development, use 'dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "..."'
```

### Failure Case - Invalid Protocol

```powershell
$env:AZURE_OPENAI_ENDPOINT="http://my-openai.openai.azure.com/"  # HTTP not allowed
# ... other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Error output:
# Azure OpenAI endpoint must use HTTPS protocol.
# Current endpoint: http://my-openai.openai.azure.com/
# Set a valid HTTPS URL in the AZURE_OPENAI_ENDPOINT environment variable.
```

## Related Files

- **Configuration validation:** `1-Presentation/MotorcycleRAG.API/Program.cs` (lines 152-153, 373-452)
- **Configuration files:**
  - `1-Presentation/MotorcycleRAG.API/appsettings.json`
  - `1-Presentation/MotorcycleRAG.API/appsettings.Development.json`
- **Documentation:** `specs/001-system-spec/quickstart.md`

## Additional Notes

- **Mobile App & BFF:** These applications don't directly call Azure services, so they don't have Azure endpoint configuration
- **Azure Keys:** API keys are loaded from environment variables separately through Azure SDK mechanisms
- **User Secrets:** Stored in `%APPDATA%\Microsoft\UserSecrets\<user-secrets-id>\secrets.json` on Windows
- **Application Insights:** Also validated at startup if telemetry is enabled
- **Deployment:** CI/CD pipelines must inject these environment variables from secure storage (Key Vault, Secrets Manager, etc.)
