# Motorcycle RAG System - Deployment Guide

## Prerequisites

### Azure Services Required
- Azure AI Foundry workspace
- Azure OpenAI service
- Azure AI Search service
- Azure Document Intelligence service
- Azure Container Registry
- Azure Container Apps environment
- Application Insights
- Azure Key Vault (recommended)

### Development Tools
- .NET 10.0 SDK
- Docker Desktop
- Azure CLI
- Pulumi CLI (for infrastructure as code)

## Step 1: Azure Resource Setup

### 1.1 Create Resource Group
```bash
az group create --name rg-motorcycle-rag --location eastus
```

### 1.2 Create Azure AI Foundry Workspace
```bash
az cognitiveservices account create \
  --name motorcycle-rag-foundry \
  --resource-group rg-motorcycle-rag \
  --kind AIServices \
  --sku S0 \
  --location eastus
```

### 1.3 Create Azure OpenAI Service
```bash
az cognitiveservices account create \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --kind OpenAI \
  --sku S0 \
  --location eastus
```

### 1.4 Deploy Required Models
```bash
# Deploy GPT-4o-mini for chat completion
az cognitiveservices account deployment create \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini \
  --model-version "2024-07-18" \
  --sku-capacity 10 \
  --sku-name "Standard"

# Deploy GPT-4o for query planning
az cognitiveservices account deployment create \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --deployment-name gpt-4o \
  --model-name gpt-4o \
  --model-version "2024-08-06" \
  --sku-capacity 5 \
  --sku-name "Standard"

# Deploy text-embedding-3-large for embeddings
az cognitiveservices account deployment create \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --deployment-name text-embedding-3-large \
  --model-name text-embedding-3-large \
  --model-version "1" \
  --sku-capacity 10 \
  --sku-name "Standard"

# Deploy GPT-4 Vision for multimodal processing
az cognitiveservices account deployment create \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --deployment-name gpt-4-vision-preview \
  --model-name gpt-4-vision-preview \
  --model-version "vision-preview" \
  --sku-capacity 5 \
  --sku-name "Standard"
```

### 1.5 Create Azure AI Search Service
```bash
az search service create \
  --name motorcycle-rag-search \
  --resource-group rg-motorcycle-rag \
  --sku Standard \
  --location eastus
```

### 1.6 Create Document Intelligence Service
```bash
az cognitiveservices account create \
  --name motorcycle-rag-docint \
  --resource-group rg-motorcycle-rag \
  --kind FormRecognizer \
  --sku S0 \
  --location eastus
```

### 1.7 Create Application Insights
```bash
az monitor app-insights component create \
  --app motorcycle-rag-insights \
  --location eastus \
  --resource-group rg-motorcycle-rag \
  --application-type web
```

### 1.8 Create Container Registry
```bash
az acr create \
  --name motorcycleragregistry \
  --resource-group rg-motorcycle-rag \
  --sku Basic \
  --admin-enabled true
```

### 1.9 Create Container Apps Environment
```bash
az containerapp env create \
  --name motorcycle-rag-env \
  --resource-group rg-motorcycle-rag \
  --location eastus
```

## Step 2: Configuration Setup

### 2.1 Get Service Endpoints and Keys
```bash
# Get Azure OpenAI endpoint and key
OPENAI_ENDPOINT=$(az cognitiveservices account show \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --query "properties.endpoint" -o tsv)

OPENAI_KEY=$(az cognitiveservices account keys list \
  --name motorcycle-rag-openai \
  --resource-group rg-motorcycle-rag \
  --query "key1" -o tsv)

# Get Search service endpoint and key
SEARCH_ENDPOINT=$(az search service show \
  --name motorcycle-rag-search \
  --resource-group rg-motorcycle-rag \
  --query "hostName" -o tsv)

SEARCH_KEY=$(az search admin-key show \
  --service-name motorcycle-rag-search \
  --resource-group rg-motorcycle-rag \
  --query "primaryKey" -o tsv)

# Get Document Intelligence endpoint and key
DOCINT_ENDPOINT=$(az cognitiveservices account show \
  --name motorcycle-rag-docint \
  --resource-group rg-motorcycle-rag \
  --query "properties.endpoint" -o tsv)

DOCINT_KEY=$(az cognitiveservices account keys list \
  --name motorcycle-rag-docint \
  --resource-group rg-motorcycle-rag \
  --query "key1" -o tsv)

# Get Application Insights connection string
APPINSIGHTS_CONNECTION=$(az monitor app-insights component show \
  --app motorcycle-rag-insights \
  --resource-group rg-motorcycle-rag \
  --query "connectionString" -o tsv)
```

### 2.2 Update Configuration Files

Update `1-Presentation/MotorcycleRAG.API/appsettings.Production.json`:

```json
{
  "AzureAI": {
    "FoundryEndpoint": "https://motorcycle-rag-foundry.cognitiveservices.azure.com/",
    "OpenAIEndpoint": "https://motorcycle-rag-openai.openai.azure.com/",
    "SearchServiceEndpoint": "https://motorcycle-rag-search.search.windows.net/",
    "DocumentIntelligenceEndpoint": "https://motorcycle-rag-docint.cognitiveservices.azure.com/",
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
  "ApplicationInsights": {
    "ConnectionString": "[WILL BE SET VIA ENVIRONMENT VARIABLE]",
    "EnableTelemetry": true
  }
}
```

## Step 3: Build and Deploy Application

### 3.1 Build Docker Image
```bash
# Navigate to solution root
cd /path/to/MotorcycleRAG

# Build the Docker image
docker build -t motorcycle-rag-system -f 7-Deployment/Dockerfile .

# Tag for Azure Container Registry
docker tag motorcycle-rag-system motorcycleragregistry.azurecr.io/motorcycle-rag-system:latest
```

### 3.2 Push to Container Registry
```bash
# Login to Azure Container Registry
az acr login --name motorcycleragregistry

# Push the image
docker push motorcycleragregistry.azurecr.io/motorcycle-rag-system:latest
```

### 3.3 Deploy to Container Apps
```bash
az containerapp create \
  --name motorcycle-rag-app \
  --resource-group rg-motorcycle-rag \
  --environment motorcycle-rag-env \
  --image motorcycleragregistry.azurecr.io/motorcycle-rag-system:latest \
  --target-port 80 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 10 \
  --cpu 1.0 \
  --memory 2.0Gi \
  --registry-server motorcycleragregistry.azurecr.io \
  --env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    APPLICATIONINSIGHTS_CONNECTION_STRING="$APPINSIGHTS_CONNECTION" \
    AZURE_CLIENT_ID="" \
    AZURE_CLIENT_SECRET="" \
    AZURE_TENANT_ID=""
```

### 3.4 Configure Managed Identity (Recommended)

Instead of using client secrets, configure managed identity:

```bash
# Create managed identity
az identity create \
  --name motorcycle-rag-identity \
  --resource-group rg-motorcycle-rag

# Get identity details
IDENTITY_ID=$(az identity show \
  --name motorcycle-rag-identity \
  --resource-group rg-motorcycle-rag \
  --query "id" -o tsv)

IDENTITY_CLIENT_ID=$(az identity show \
  --name motorcycle-rag-identity \
  --resource-group rg-motorcycle-rag \
  --query "clientId" -o tsv)

# Assign identity to container app
az containerapp identity assign \
  --name motorcycle-rag-app \
  --resource-group rg-motorcycle-rag \
  --user-assigned $IDENTITY_ID

# Grant permissions to Azure services
az role assignment create \
  --assignee $IDENTITY_CLIENT_ID \
  --role "Cognitive Services User" \
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-motorcycle-rag"

az role assignment create \
  --assignee $IDENTITY_CLIENT_ID \
  --role "Search Index Data Contributor" \
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-motorcycle-rag"
```

## Step 4: Infrastructure as Code (Optional)

### 4.1 Deploy with Pulumi
```bash
cd 7-Deployment/infrastructure

# Install dependencies
dotnet restore

# Configure Pulumi stack
pulumi stack init production
pulumi config set azure-native:location eastus
pulumi config set motorcyclerag:resourceGroupName rg-motorcycle-rag

# Deploy infrastructure
pulumi up
```

## Step 5: Post-Deployment Configuration

### 5.1 Create Search Index
The application will automatically create the search index on first run, but you can also create it manually:

```bash
# The application includes automatic index creation
# Check the logs to verify index creation:
az containerapp logs show \
  --name motorcycle-rag-app \
  --resource-group rg-motorcycle-rag \
  --follow
```

### 5.2 Upload Initial Data
Use the API to upload initial motorcycle data:

```bash
# Get the application URL
APP_URL=$(az containerapp show \
  --name motorcycle-rag-app \
  --resource-group rg-motorcycle-rag \
  --query "properties.configuration.ingress.fqdn" -o tsv)

# Upload sample data (prepare CSV and PDF files)
curl -X POST "https://$APP_URL/api/datapipeline/upload" \
  -F "files=@sample-motorcycles.csv" \
  -F "processImmediately=true"
```

### 5.3 Verify Deployment
```bash
# Check application health
curl "https://$APP_URL/health"

# Test a query
curl -X POST "https://$APP_URL/api/motorcycle/query" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "What are Honda motorcycle models?",
    "userId": "test-user"
  }'
```

## Step 6: Monitoring and Alerting

### 6.1 Configure Application Insights Alerts
```bash
# Create alert for high response times
az monitor metrics alert create \
  --name "High Response Time" \
  --resource-group rg-motorcycle-rag \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-motorcycle-rag/providers/Microsoft.App/containerApps/motorcycle-rag-app" \
  --condition "avg requests/duration > 3000" \
  --description "Alert when average response time exceeds 3 seconds"

# Create alert for high error rate
az monitor metrics alert create \
  --name "High Error Rate" \
  --resource-group rg-motorcycle-rag \
  --scopes "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-motorcycle-rag/providers/Microsoft.App/containerApps/motorcycle-rag-app" \
  --condition "avg requests/failed > 5" \
  --description "Alert when error rate exceeds 5%"
```

### 6.2 Set Up Log Analytics Queries
Create custom queries in Application Insights for monitoring:

```kusto
// Query performance monitoring
requests
| where timestamp > ago(1h)
| summarize avg(duration), percentile(duration, 95) by bin(timestamp, 5m)
| render timechart

// Error analysis
exceptions
| where timestamp > ago(24h)
| summarize count() by type, outerMessage
| order by count_ desc

// Pipeline monitoring
customEvents
| where name == "PipelineCompleted"
| extend status = tostring(customDimensions.Status)
| summarize count() by status, bin(timestamp, 1h)
| render columnchart
```

## Step 7: Security Hardening

### 7.1 Network Security
```bash
# Configure private endpoints (if required)
az network private-endpoint create \
  --name motorcycle-rag-pe \
  --resource-group rg-motorcycle-rag \
  --vnet-name motorcycle-rag-vnet \
  --subnet private-endpoints \
  --private-connection-resource-id "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-motorcycle-rag/providers/Microsoft.CognitiveServices/accounts/motorcycle-rag-openai" \
  --connection-name openai-connection \
  --group-id account
```

### 7.2 Key Vault Integration
```bash
# Create Key Vault
az keyvault create \
  --name motorcycle-rag-kv \
  --resource-group rg-motorcycle-rag \
  --location eastus

# Store secrets
az keyvault secret set \
  --vault-name motorcycle-rag-kv \
  --name "OpenAI-Key" \
  --value "$OPENAI_KEY"

az keyvault secret set \
  --vault-name motorcycle-rag-kv \
  --name "Search-Key" \
  --value "$SEARCH_KEY"

# Grant access to managed identity
az keyvault set-policy \
  --name motorcycle-rag-kv \
  --object-id $IDENTITY_CLIENT_ID \
  --secret-permissions get list
```

## Step 8: Backup and Disaster Recovery

### 8.1 Configure Backup
```bash
# Enable backup for Key Vault
az backup vault create \
  --name motorcycle-rag-backup \
  --resource-group rg-motorcycle-rag \
  --location eastus

# Configure Application Insights data export
az monitor app-insights component continues-export create \
  --app motorcycle-rag-insights \
  --resource-group rg-motorcycle-rag \
  --record-types Requests,Exceptions,CustomEvents \
  --dest-account motorcycleragbackup \
  --dest-container appinsights-export
```

### 8.2 Document Recovery Procedures
Create runbooks for:
- Service restoration procedures
- Data recovery from backups
- Configuration restoration
- Rollback procedures

## Troubleshooting Deployment Issues

### Common Issues

1. **Container App Won't Start**
   - Check container logs: `az containerapp logs show --name motorcycle-rag-app --resource-group rg-motorcycle-rag`
   - Verify environment variables are set correctly
   - Check image exists in container registry

2. **Azure Service Authentication Failures**
   - Verify managed identity is assigned and has correct permissions
   - Check service endpoints are correct
   - Verify API keys if using key-based authentication

3. **High Memory Usage**
   - Increase memory allocation in container app
   - Check for memory leaks in application logs
   - Review caching configuration

4. **Performance Issues**
   - Check Azure service quotas and limits
   - Review Application Insights performance data
   - Verify connection pooling configuration

### Support Resources
- Azure Container Apps documentation
- Azure AI services documentation
- Application Insights troubleshooting guide
- Azure support tickets for service-specific issues

## Maintenance

### Regular Tasks
- Monitor Application Insights for performance and errors
- Review and update Azure service quotas
- Update container images with security patches
- Review and optimize costs using Azure Cost Management
- Test backup and recovery procedures

### Updates
- Use blue-green deployment for zero-downtime updates
- Test updates in staging environment first
- Monitor metrics during and after deployment
- Have rollback plan ready

This deployment guide provides a comprehensive approach to deploying the Motorcycle RAG System to Azure. Follow the steps in order and customize as needed for your specific environment and requirements.