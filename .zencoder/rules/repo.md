---
description: Repository Information Overview
alwaysApply: true
---

# Motorcycle RAG System Information

## Summary
A sophisticated multi-agent RAG (Retrieval-Augmented Generation) system built on Azure AI Foundry for intelligent motorcycle information retrieval. Implements Clean Architecture with multiple specialized agents (Query Planner, Vector Search, Web Search) orchestrated via Semantic Kernel, supporting heterogeneous data sources including CSV specifications, PDF manuals, and real-time web information.

## Repository Structure

### Main Components
- **1-Presentation**: ASP.NET Core Web API (`MotorcycleRAG.API`) with minimal APIs and OpenAPI documentation
- **2-Application**: Business logic layer with agents, services, and Semantic Kernel orchestration
- **3-Domain**: Core domain models, contracts, and interfaces
- **4-Persistence**: Azure service clients, data processors, resilience patterns, and telemetry
- **5-Test**: Comprehensive testing suite (unit, integration, performance, load, end-to-end)
- **6-Docs**: Project documentation and specifications
- **7-Deployment**: Infrastructure as Code with Pulumi and Docker configuration

### Key Directories
- `1-Presentation/MotorcycleRAG.API/`: API entry point, Program.cs, configuration setup
- `2-Application/MotorcycleRAG.Application/`: Agents (QueryPlannerAgent, VectorSearchAgent, WebSearchAgent), services
- `3-Domain/`: Domain models (MotorcycleSpecification, SearchResult), interfaces (ISearchAgent, IMotorcycleRAGService)
- `4-Persistence/Azure/`: Azure client wrappers (OpenAI, Search, Document Intelligence)
- `4-Persistence/DataProcessing/`: CSV and PDF processors with semantic chunking
- `4-Persistence/Resilience/`: Polly-based circuit breakers and retry policies
- `5-Test/tests/`: Separate projects for unit, integration, performance, load, and end-to-end tests

## Language & Runtime

**Language**: C# (.NET 9.0)  
**Runtime Version**: .NET 9.0  
**Build System**: .NET CLI (dotnet build/publish)  
**Package Manager**: NuGet

## Dependencies

### Core Framework
- **ASP.NET Core 109.0**: Web API framework with minimal APIs
- **Microsoft.SemanticKernel 1.2.0**: Agent orchestration and AI services integration
- **Azure.AI.OpenAI 2.1.0**: GPT-4o, GPT-4o-mini, text-embedding-3-large integration
- **Azure.Search.Documents 11.6.0/11.7.0**: Hybrid vector/keyword search
- **Azure.AI.DocumentIntelligence 1.0.0**: PDF processing and OCR

### Infrastructure & Resilience
- **Polly 8.4.2 & Polly.Extensions.Http 3.0.0**: Circuit breakers, retries, fallback policies
- **Azure.Identity 1.12.1/1.13.1**: Managed Identity and DefaultAzureCredential
- **Microsoft.ApplicationInsights 2.23.0**: Telemetry and monitoring
- **Microsoft.AspNetCore.Diagnostics.HealthChecks 2.2.0**: Health check endpoints

### Data Processing
- **CsvHelper 33.0.1**: CSV parsing and processing
- **HtmlAgilityPack 1.11.71**: HTML parsing for web content

### Testing
- **xUnit 2.9.2**: Unit and integration testing framework
- **Moq 4.20.72**: Mocking library for testing
- **FluentAssertions 6.12.2**: Assertion library
- **BenchmarkDotNet 0.14.0**: Performance benchmarking
- **NBomber 5.11.1 & NBomber.Http 5.11.1**: Load testing
- **Microsoft.AspNetCore.Mvc.Testing 9.0.7**: Integration test support

### Configuration
- **Azure.Data.AppConfiguration 1.5.1**: Azure App Configuration client
- **Azure.Security.KeyVault.Secrets 4.6.0**: Azure Key Vault integration
- **Microsoft.Extensions.Configuration.AzureAppConfiguration 6.0.0**: Configuration provider

## Build & Installation

### Build Commands
```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build 2-Application/MotorcycleRAG.Application/MotorcycleRAG.Application.csproj

# Publish for deployment
dotnet publish "1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj" -c Release -o ./publish
```

### Installation
1. Clone repository
2. Configure Azure services (OpenAI, AI Search, Document Intelligence endpoints)
3. Update `appsettings.json` or set environment variables with Azure service credentials
4. Restore dependencies: `dotnet restore`
5. Build: `dotnet build`

## Docker

**Dockerfile**: `7-Deployment/Dockerfile`  
**Base Image**: `mcr.microsoft.com/dotnet/aspnet:8.0` (runtime), `mcr.microsoft.com/dotnet/sdk:8.0` (build)  
**Port**: 80  

**Build and Run**:
```bash
docker build -t motorcyclerag-api -f 7-Deployment/Dockerfile .
docker run -p 8080:80 motorcyclerag-api
```

Note: Dockerfile references `src/` directory structure which differs from actual project layout.

## Testing

### Test Frameworks & Structure

**Unit Tests**: `5-Test/tests/MotorcycleRAG.UnitTests/`
- Framework: xUnit with Moq
- Coverage targets: >80%
- Tests agent logic, processors, resilience patterns

**Integration Tests**: `5-Test/tests/MotorcycleRAG.IntegrationTests/`
- Uses `Microsoft.AspNetCore.Mvc.Testing` (TestServer)
- Real Azure service mocking via interfaces
- Configuration: `appsettings.Test.json`

**Performance Tests**: `5-Test/tests/MotorcycleRAG.PerformanceTests/`
- Framework: BenchmarkDotNet
- Tests agent and processor performance
- Configuration: `appsettings.Performance.json`

**Load Tests**: `5-Test/tests/MotorcycleRAG.LoadTests/`
- Framework: NBomber HTTP load testing
- Simulates concurrent queries and concurrent users
- Configuration: `appsettings.Load.json`

**End-to-End Tests**: `5-Test/tests/MotorcycleRAG.EndToEndTests/`
- Full application flow testing
- Real Azure service integration
- Configuration: `appsettings.EndToEnd.json`

### Test Configuration Files
- `appsettings.Test.json`: Integration test environment settings
- `appsettings.Performance.json`: Performance test configuration
- `appsettings.Load.json`: Load test configuration with endpoints
- `appsettings.EndToEnd.json`: E2E test configuration

### Run Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test 5-Test/tests/MotorcycleRAG.UnitTests
dotnet test 5-Test/tests/MotorcycleRAG.IntegrationTests

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Run specific test class
dotnet test --filter "FullyQualifiedName~QueryPlannerAgentTests"
```

## Main Files & Resources

### Application Entry Points
- **API Entry Point**: `1-Presentation/MotorcycleRAG.API/Program.cs`
  - Configures Azure App Configuration, Key Vault, Application Insights
  - DI setup via ServiceCollectionExtensions
  - OpenAPI/Swagger configuration
  - Health checks setup

### Key Configuration Files
- **appsettings.json**: Default configuration template
- **appsettings.Development.json**: Development environment overrides
- **appsettings.Production.json**: Production environment settings
- **Program.cs**: Full service registration and middleware pipeline

### API Endpoints
- **POST** `/api/motorcycles/query`: Process motorcycle queries with RAG
- **GET** `/health`: Health check endpoint
- **GET** `/swagger`: OpenAPI documentation (dev only)

### Domain Models
- `MotorcycleSpecification`: Core motorcycle data entity
- `SearchResult`: Unified search result from all agents
- `QueryModels`: Request/response DTOs

### Configuration Models (from AppConfiguration)
- `AzureAIConfiguration`: OpenAI, Search, Document Intelligence endpoints
- `SearchConfiguration`: Hybrid search settings
- `ResilienceConfiguration`: Polly policy settings

## Infrastructure & Deployment

### Infrastructure as Code
**Location**: `7-Deployment/infrastructure/`  
**Framework**: Pulumi (C#)  
**Deploys**: Azure Container Apps, Container Environment, networking

### Infrastructure Project
```bash
cd 7-Deployment/infrastructure
pulumi preview
pulumi up
```

### Environment Variables
- `AzureAI__OpenAIEndpoint`: Azure OpenAI service endpoint
- `AzureAI__SearchServiceEndpoint`: Azure AI Search endpoint
- `AzureAI__DocumentIntelligenceEndpoint`: Document Intelligence endpoint
- `ApplicationInsights__ConnectionString`: Application Insights telemetry
- `AppConfig:Endpoint`: Azure App Configuration endpoint (optional)

## Development Workflow

### Hot Reload Development
```bash
dotnet watch --project 1-Presentation/MotorcycleRAG.API
```

### Architecture Principles
- **Clean Architecture**: Strict layer separation (Presentation → Application → Domain ← Persistence)
- **Interface-based Design**: Dependency injection throughout
- **Async/Await Patterns**: All I/O operations asynchronous with CancellationToken
- **Resilience Patterns**: Polly circuit breakers, retries (exponential backoff), fallbacks
- **Observability**: Structured logging, Application Insights metrics, correlation IDs
- **Security-First**: Authentication via Managed Identity, input validation, no hardcoded secrets

### Multi-Agent System
1. **QueryPlannerAgent**: Analyzes user queries with GPT-4o, creates search strategies
2. **VectorSearchAgent**: Hybrid vector/keyword search on motorcycle index
3. **WebSearchAgent**: Augments with real-time web data
4. **PDF Search Agent**: Fallback technical documentation search
5. **AgentOrchestrator**: Coordinates sequential flow and result fusion

### Data Processing Pipeline
- **CSV Processor**: Row-based chunking preserving specification relationships
- **PDF Processor**: Azure Document Intelligence Layout model with semantic chunking
- **Hybrid Search**: Vector embeddings (text-embedding-3-large) + keyword search
- **Semantic Ranking**: Combines results from all sources by relevance

