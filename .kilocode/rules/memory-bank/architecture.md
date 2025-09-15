# Architecture

The Motorcycle RAG System follows Clean Architecture principles with a layered design:

## Layer Structure

### 1-Presentation Layer
- **MotorcycleRAG.API**: ASP.NET Core Web API
- RESTful endpoints for search queries
- Swagger/OpenAPI documentation
- CORS configuration for web clients
- Health checks and monitoring

### 2-Application Layer
- **Agents**: Specialized AI agents for different search types
  - QueryPlannerAgent: Analyzes and plans search strategy
  - VectorSearchAgent: Hybrid vector/keyword search
  - WebSearchAgent: Real-time web augmentation
- **Services**: Business logic orchestration
  - AgentOrchestrator: Coordinates sequential search flow
  - ModelValidationService: Ensures data quality
  - MotorcycleRAGService: Main service interface

### 3-Domain Layer
- **Models**: Core business entities
  - MotorcycleSpecification: Detailed bike specs
  - SearchResult: Unified search response format
  - QueryModels: Request/response structures
- **Interfaces**: Contracts for services and repositories

### 4-Persistence Layer
- **Azure Services**: Cloud infrastructure integration
  - AzureOpenAIClientWrapper: GPT model interactions
  - AzureSearchClientWrapper: Vector search operations
  - DocumentIntelligenceClientWrapper: PDF/OCR processing
- **Data Processing**: Document ingestion pipelines
  - MotorcycleCSVProcessor: CSV specification parsing
  - MotorcyclePDFProcessor: PDF document extraction
- **Resilience**: Circuit breakers and retry patterns
- **Telemetry**: Application Insights integration

### 5-Test Layer
- Unit tests for all components
- Integration tests for end-to-end scenarios
- Test coverage for Azure service mocks

### 6-Docs Layer
- Deployment guides and configuration
- Azure naming standards
- API documentation

### 7-Deployment Layer
- Infrastructure-as-Code with Pulumi
- Docker containerization
- CI/CD with GitHub Actions

## Data Flow

1. User submits query to API
2. QueryPlannerAgent analyzes intent and determines strategy
3. VectorSearchAgent searches indexed motorcycle data
4. WebSearchAgent augments with real-time information
5. AgentOrchestrator combines and ranks results
6. API returns formatted response with sources

## Technology Integration

- **Azure AI Foundry**: Unified AI service platform
- **Semantic Kernel**: Agent orchestration framework
- **Azure AI Search**: Hybrid vector/keyword search
- **Azure Document Intelligence**: Document processing
- **Azure OpenAI**: GPT models for generation
- **Application Insights**: Monitoring and telemetry