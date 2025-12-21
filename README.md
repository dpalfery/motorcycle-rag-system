# Motorcycle RAG System

A sophisticated multi-agent RAG (Retrieval-Augmented Generation) system for intelligent motorcycle information retrieval, built on Azure AI Foundry platform using Clean Architecture principles.

## Overview

This system implements a multi-agent architecture to orchestrate intelligent search across heterogeneous data sources including CSV specifications, PDF manuals, and real-time web sources. It uses a sequential search pattern with intelligent query planning, hybrid vector/keyword search, and comprehensive resilience patterns.

## Architecture

### Multi-Agent System
- **Query Planner Agent**: Uses GPT-4o to analyze user queries and generate optimal search strategies
- **Vector Search Agent**: Performs hybrid vector/keyword search on indexed motorcycle data
- **Web Search Agent**: Augments results with real-time web information and current data
- **Agent Orchestrator**: Coordinates the sequential search flow and result aggregation

### Clean Architecture Layers
```
1-Presentation/     # ASP.NET Core Web API
2-Application/      # Business logic and agents
3-Domain/          # Core models and interfaces
4-Persistence/     # Azure services and data access
5-Test/           # Unit and integration tests
6-Docs/           # Documentation
7-Deployment/     # Infrastructure as Code
```

## Technology Stack

- **Platform**: Azure AI Foundry (unified AI services platform)
- **Framework**: ASP.NET Core Web API (.NET 10.0)
- **AI Services**: Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
- **Search**: Azure AI Search (hybrid vector/keyword with semantic ranking)
- **Document Processing**: Azure Document Intelligence (OCR and PDF processing)
- **Configuration**: Azure App Configuration + Azure Key Vault
- **Infrastructure**: Pulumi (Infrastructure as Code)
- **Monitoring**: Application Insights with comprehensive telemetry
- **Resilience**: Polly (circuit breakers, retries, fallbacks)
- **Testing**: xUnit, Moq, Integration Tests
- **Deployment**: Azure Container Apps / App Service

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Azure subscription with the following services:
  - Azure AI Foundry project
  - Azure OpenAI service (GPT-4o, GPT-4o-mini, text-embedding-3-large)
  - Azure AI Search service
  - Azure Document Intelligence service
  - Azure App Configuration (optional, for centralized config)
  - Azure Key Vault (optional, for secrets management)
  - Application Insights (optional, for monitoring)

### Quick Start

1. **Clone the repository**
   ```powershell
   git clone <repository-url>
   Set-Location motorcycle-rag-system
   ```

2. **Configure Azure services**
   - Set up Azure AI Foundry project
   - Deploy Azure OpenAI, AI Search, and Document Intelligence services
   - Configure authentication (Managed Identity recommended)

3. **Update configuration**
   ```powershell
   # Copy and modify configuration files
   Copy-Item "1-Presentation/MotorcycleRAG.API/appsettings.json" "1-Presentation/MotorcycleRAG.API/appsettings.Development.json"

   # Edit appsettings.Development.json with your Azure service endpoints
   # For production, use Azure App Configuration and Key Vault
   ```

4. **Run the application**
   ```powershell
   # Build and run
   dotnet build
   dotnet test
   dotnet run --project 1-Presentation/MotorcycleRAG.API
   ```

### API Endpoints

- **POST** `/api/motorcycles/query` - Process motorcycle queries with RAG
- **GET** `/api/motorcycles/health` - Health check endpoint
- **GET** `/health` - General health checks
- **GET** `/swagger` - API documentation (development only)

## Development

### Build and Test

```powershell
# Build the entire solution
dotnet build MotorcycleRAG.sln

# Run all tests
dotnet test MotorcycleRAG.sln

# Run specific test projects
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

### Development Workflow

1. **Local Development**
   ```powershell
   # Hot reload during development
   dotnet watch --project 1-Presentation/MotorcycleRAG.API

   # Debug with Visual Studio
   # Open MotorcycleRAG.sln and start debugging
   ```

2. **Configuration Management**
   - Development: `appsettings.Development.json`
   - Production: Azure App Configuration + Key Vault
   - Environment-specific overrides supported

3. **Testing Strategy**
   - Unit tests for all components
   - Integration tests for Azure services
   - Resilience testing with Polly patterns

### Architecture Principles

- **Clean Architecture**: Strict separation of concerns across layers
- **Interface-based design**: Dependency injection throughout
- **Async/await patterns**: All I/O operations are asynchronous
- **Resilience patterns**: Circuit breakers, retries, and fallbacks with Polly
- **Comprehensive observability**: Structured logging, metrics, and tracing
- **Security-first**: Authentication, authorization, and input validation
- **Cost optimization**: Intelligent caching and resource management

## Deployment

### Infrastructure as Code

The project uses Pulumi for infrastructure provisioning:

```powershell
# Navigate to infrastructure directory
Set-Location "7-Deployment/infrastructure"

# Install dependencies
pulumi plugin install resource azure-native 2.0.0

# Set configuration (or use GitHub secrets for CI/CD)
pulumi config set azure-native:location eastus
pulumi config set azureOpenAIEndpoint "https://your-openai.openai.azure.com"

# Preview deployment
pulumi preview

# Deploy infrastructure
pulumi up
```

### Azure Services Setup

See [`6-Docs/azure-ai-foundry-setup.md`](6-Docs/azure-ai-foundry-setup.md) for detailed Azure AI Foundry configuration.

### Container Deployment

```powershell
# Build Docker image
docker build -t motorcyclerag-api -f 7-Deployment/Dockerfile .

# Run locally
docker run -p 8080:80 motorcyclerag-api

# Push to Azure Container Registry (ACR)
az acr build --registry myregistry --image motorcyclerag-api .
```

## Configuration

### Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `AzureAI__FoundryEndpoint` | Azure AI Foundry endpoint | Yes |
| `AzureAI__OpenAIEndpoint` | Azure OpenAI service endpoint | Yes |
| `AzureAI__SearchServiceEndpoint` | Azure AI Search endpoint | Yes |
| `AzureAI__DocumentIntelligenceEndpoint` | Document Intelligence endpoint | Yes |
| `ApplicationInsights__ConnectionString` | App Insights connection string | No |

### Azure App Configuration

For production deployments, use Azure App Configuration:

```json
{
  "AppConfig:Endpoint": "https://your-appconfig.azconfig.io",
  "Settings:Sentinel": "v1.0"
}
```

## Contributing

1. Follow Clean Architecture principles and the established layer structure
2. Ensure all tests pass before submitting PRs
3. Add comprehensive unit and integration tests for new functionality
4. Update API documentation and OpenAPI specifications
5. Follow the established coding standards and async patterns
6. Add appropriate logging and telemetry for new features

## License

This project is licensed under the MIT License - see the LICENSE file for details.
