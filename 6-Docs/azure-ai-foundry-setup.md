# Azure AI Foundry Setup Guide

This guide provides step-by-step instructions for setting up Azure AI Foundry and all required Azure AI services for the Motorcycle RAG System.

## Prerequisites

- Azure subscription with sufficient permissions
- Azure CLI installed (`az` command)
- .NET 9.0 SDK
- Git

## 1. Create Azure AI Foundry Project

### Option A: Azure Portal

1. **Navigate to Azure AI Foundry**
   - Go to [Azure AI Foundry](https://ai.azure.com)
   - Sign in with your Azure account

2. **Create New Project**
   - Click "Create new project"
   - Project name: `motorcycle-rag-project`
   - Hub name: `motorcycle-rag-hub`
   - Location: Select your preferred region (e.g., East US)
   - Resource group: Create new or select existing

3. **Configure Project Settings**
   - Subscription: Select your Azure subscription
   - Connect Azure OpenAI: Enable
   - Connect Azure AI Search: Enable
   - Connect other AI services as needed

### Option B: Azure CLI (PowerShell)

```powershell
# Login to Azure
az login

# Set subscription
az account set --subscription "your-subscription-id"

# Create resource group
az group create --name "DP-AI-Foundry-2" --location "eastus"

# Create Azure AI Foundry project (requires Azure CLI AI extension)
az extension add --name ai

# Create AI Foundry project
az ai project create `
  --name "motorcycle-rag-project" `
  --resource-group "DP-AI-Foundry-2" `
  --location "eastus"
```

## 2. Deploy Azure OpenAI Service

### Create Azure OpenAI Resource

```powershell
# Create Azure OpenAI resource
az cognitiveservices account create `
  --name "david-mevy1vm0-eastus2" `
  --resource-group "DP-AI-Foundry-2" `
  --kind "OpenAI" `
  --sku "S0" `
  --location "eastus"
```

### Deploy Required Models

Deploy the following models in your Azure OpenAI resource:

1. **GPT-4o** (for query planning)
   - Model: `gpt-4o`
   - Model version: `2024-05-13`
   - Deployment name: `gpt-4o`

2. **GPT-4o-mini** (for chat completion)
   - Model: `gpt-4o-mini`
   - Model version: `2024-07-18`
   - Deployment name: `gpt-4o-mini`

3. **text-embedding-3-large** (for embeddings)
   - Model: `text-embedding-3-large`
   - Model version: `1`
   - Deployment name: `text-embedding-3-large`

### Get OpenAI Endpoint and Keys

```powershell
# Get OpenAI endpoint
$env:OPENAI_ENDPOINT = (az cognitiveservices account show `
  --name "david-mevy1vm0-eastus2" `
  --resource-group "DP-AI-Foundry-2" `
  --query "properties.endpoint" `
  --output tsv)

# Get OpenAI keys
$env:OPENAI_KEY = (az cognitiveservices account keys list `
  --name "david-mevy1vm0-eastus2" `
  --resource-group "DP-AI-Foundry-2" `
  --query "key1" `
  --output tsv)

Write-Host "OpenAI Endpoint: $env:OPENAI_ENDPOINT"
Write-Host "OpenAI Key: $env:OPENAI_KEY"
```

## 3. Create Azure AI Search Service

```powershell
# Create Azure AI Search service
az search service create `
  --name "motorcyclerag-search" `
  --resource-group "DP-AI-Foundry-2" `
  --sku "standard" `
  --location "eastus" `
  --partition-count 1 `
  --replica-count 1

# Get search service endpoint and keys
$env:SEARCH_ENDPOINT = (az search service show `
  --name "motorcyclerag-search" `
  --resource-group "DP-AI-Foundry-2" `
  --query "url" `
  --output tsv)

$env:SEARCH_KEY = (az search admin-key show `
  --service-name "motorcyclerag-search" `
  --resource-group "DP-AI-Foundry-2" `
  --query "primaryKey" `
  --output tsv)

Write-Host "Search Endpoint: $env:SEARCH_ENDPOINT"
Write-Host "Search Admin Key: $env:SEARCH_KEY"
```

### Create Search Index

The system expects an index named `motorcycle-index`. You can create it manually or let the application create it on first run.

## 4. Create Azure Document Intelligence

```powershell
# Create Document Intelligence resource
az cognitiveservices account create `
  --name "motorcyclerag-docintel" `
  --resource-group "DP-AI-Foundry-2" `
  --kind "FormRecognizer" `
  --sku "S0" `
  --location "eastus"

# Get Document Intelligence endpoint and key
$env:DOCINTEL_ENDPOINT = (az cognitiveservices account show `
  --name "motorcyclerag-docintel" `
  --resource-group "DP-AI-Foundry-2" `
  --query "properties.endpoint" `
  --output tsv)

$env:DOCINTEL_KEY = (az cognitiveservices account keys list `
  --name "motorcyclerag-docintel" `
  --resource-group "DP-AI-Foundry-2" `
  --query "key1" `
  --output tsv)

Write-Host "Document Intelligence Endpoint: $env:DOCINTEL_ENDPOINT"
Write-Host "Document Intelligence Key: $env:DOCINTEL_KEY"
```

## 5. Set Up Azure App Configuration (Optional)

For centralized configuration management:

```powershell
# Create App Configuration store
az appconfig create `
  --name "motorcyclerag-appconfig" `
  --resource-group "DP-AI-Foundry-2" `
  --location "eastus" `
  --sku "free"

# Get App Configuration endpoint
$env:APPCONFIG_ENDPOINT = (az appconfig show `
  --name "motorcyclerag-appconfig" `
  --resource-group "DP-AI-Foundry-2" `
  --query "endpoint" `
  --output tsv)

Write-Host "App Configuration Endpoint: $env:APPCONFIG_ENDPOINT"
```

## 6. Set Up Azure Key Vault (Optional)

For secure secrets management:

```powershell
# Create Key Vault
az keyvault create `
  --name "motorcyclerag-kv" `
  --resource-group "DP-AI-Foundry-2" `
  --location "eastus" `
  --enabled-for-deployment true `
  --enabled-for-template-deployment true

# Store secrets in Key Vault
az keyvault secret set `
  --vault-name "motorcyclerag-kv" `
  --name "AzureOpenAIKey" `
  --value "$env:OPENAI_KEY"

az keyvault secret set `
  --vault-name "motorcyclerag-kv" `
  --name "AzureSearchKey" `
  --value "$env:SEARCH_KEY"

az keyvault secret set `
  --vault-name "motorcyclerag-kv" `
  --name "DocumentIntelligenceKey" `
  --value "$env:DOCINTEL_KEY"
```

## 7. Configure Application Settings

Update your `appsettings.json` or environment variables with the service endpoints:

```json
{
  "AzureAI": {
    "FoundryEndpoint": "https://your-foundry-endpoint.cognitiveservices.azure.com/",
    "OpenAIEndpoint": "https://david-mevy1vm0-eastus2.openai.azure.com/",
    "SearchServiceEndpoint": "https://motorcyclerag-search.search.windows.net/",
    "DocumentIntelligenceEndpoint": "https://motorcyclerag-docintel.cognitiveservices.azure.com/",
    "Models": {
      "ChatModel": "gpt-4o-mini",
      "EmbeddingModel": "text-embedding-3-large",
      "QueryPlannerModel": "gpt-4o",
      "VisionModel": "gpt-4-vision-preview"
    }
  },
  "Search": {
    "IndexName": "motorcycle-index"
  },
  "AppConfig": {
    "Endpoint": "https://motorcyclerag-appconfig.azconfig.io"
  }
}
```

## 8. Set Up Managed Identity (Recommended)

For production deployments, use Managed Identity instead of access keys:

```powershell
# Create user-assigned managed identity
az identity create `
  --name "motorcyclerag-identity" `
  --resource-group "DP-AI-Foundry-2" `
  --location "eastus"

# Assign roles to the managed identity
$env:IDENTITY_PRINCIPAL_ID = (az identity show `
  --name "motorcyclerag-identity" `
  --resource-group "DP-AI-Foundry-2" `
  --query "principalId" `
  --output tsv)

# Assign Cognitive Services User role for OpenAI
az role assignment create `
  --assignee "$env:IDENTITY_PRINCIPAL_ID" `
  --role "Cognitive Services User" `
  --scope "/subscriptions/your-subscription-id/resourceGroups/DP-AI-Foundry-2/providers/Microsoft.CognitiveServices/accounts/david-mevy1vm0-eastus2"

# Assign Search Service Contributor role
az role assignment create `
  --assignee "$env:IDENTITY_PRINCIPAL_ID" `
  --role "Search Service Contributor" `
  --scope "/subscriptions/your-subscription-id/resourceGroups/DP-AI-Foundry-2/providers/Microsoft.Search/searchServices/motorcyclerag-search"

# Assign Key Vault Secrets User role (if using Key Vault)
az keyvault set-policy `
  --name "motorcyclerag-kv" `
  --object-id "$env:IDENTITY_PRINCIPAL_ID" `
  --secret-permissions get list
```

## 9. Test the Setup

### Verify Service Connectivity

```powershell
# Test OpenAI connectivity (using Invoke-RestMethod)
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $env:OPENAI_KEY
}
$body = @{
    messages = @(@{role = "user"; content = "Hello"})
    max_tokens = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri "$env:OPENAI_ENDPOINT/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-02-15" -Method POST -Headers $headers -Body $body

# Test Search service
$searchHeaders = @{
    "api-key" = $env:SEARCH_KEY
}
Invoke-RestMethod -Uri "$env:SEARCH_ENDPOINT/indexes?api-version=2023-11-01" -Method GET -Headers $searchHeaders

# Test Document Intelligence
$docHeaders = @{
    "Content-Type" = "application/json"
    "Ocp-Apim-Subscription-Key" = $env:DOCINTEL_KEY
}
$docBody = @{
    urlSource = "https://example.com/sample.pdf"
} | ConvertTo-Json

Invoke-RestMethod -Uri "$env:DOCINTEL_ENDPOINT/formrecognizer/documentModels/prebuilt-read:analyze?api-version=2023-07-31" -Method POST -Headers $docHeaders -Body $docBody
```

### Run the Application

```powershell
# Navigate to the API project
Set-Location "1-Presentation/MotorcycleRAG.API"

# Run the application
dotnet run

# Test the health endpoint
Invoke-RestMethod -Uri "http://localhost:5000/health" -Method GET

# Test the API with a sample query
$apiHeaders = @{
    "Content-Type" = "application/json"
}
$queryBody = @{
    query = "What are the specifications for a Honda CBR600RR?"
    maxResults = 5
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/api/motorcycles/query" -Method POST -Headers $apiHeaders -Body $queryBody
```

## 10. Monitoring and Troubleshooting

### Enable Application Insights

```powershell
# Create Application Insights resource
az monitor app-insights component create `
  --app "motorcyclerag-appinsights" `
  --location "eastus" `
  --resource-group "DP-AI-Foundry-2" `
  --application-type "web"

# Get connection string
$env:APPINSIGHTS_CONNECTION = (az monitor app-insights component show `
  --app "motorcyclerag-appinsights" `
  --resource-group "DP-AI-Foundry-2" `
  --query "connectionString" `
  --output tsv)

Write-Host "Application Insights Connection String: $env:APPINSIGHTS_CONNECTION"
```

### Common Issues

1. **Authentication Errors**
   - Verify API keys are correct
   - Check if Managed Identity has proper role assignments
   - Ensure Key Vault policies are configured

2. **Service Quotas**
   - Check Azure subscription quotas for each service
   - Monitor usage in Azure Portal

3. **Network Issues**
   - Verify endpoints are accessible
   - Check firewall and NSG rules
   - Ensure proper VNet configuration

## Cost Optimization

- Use GPT-4o-mini for most operations to reduce costs
- Implement caching for frequent queries
- Monitor usage with Application Insights
- Set up budget alerts in Azure Cost Management

## Next Steps

1. Deploy the application to Azure Container Apps or App Service
2. Set up CI/CD pipelines with GitHub Actions
3. Configure monitoring and alerting
4. Implement data ingestion pipelines for motorcycle data
5. Set up automated testing and deployment validation

For detailed deployment instructions, see [`6-Docs/deployment.md`](deployment.md).