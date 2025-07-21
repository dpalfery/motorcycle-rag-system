# Project Structure

## Root Directory Organization

```
/
├── .kiro/                          # Kiro configuration and specs
│   ├── specs/                      # Project specifications
│   └── steering/                   # AI assistant guidance rules
├── src/                            # Source code
├── tests/                          # Test projects
├── docs/                           # Documentation
├── scripts/                        # Build and deployment scripts
└── docker/                         # Docker configuration files
```

## Source Code Structure

```
src/
├── MotorcycleRAG.API/              # Web API project (entry point)
│   ├── Controllers/                # API controllers
│   ├── Models/                     # Request/response models
│   ├── Configuration/              # Startup and DI configuration
│   └── Program.cs                  # Application entry point
├── MotorcycleRAG.Core/             # Core business logic
│   ├── Interfaces/                 # Service contracts
│   ├── Models/                     # Domain models
│   ├── Services/                   # Core service implementations
│   └── Agents/                     # Multi-agent implementations
├── MotorcycleRAG.Infrastructure/   # External service integrations
│   ├── Azure/                      # Azure service clients
│   ├── DataProcessing/             # CSV and PDF processors
│   ├── Search/                     # Search service implementations
│   └── Configuration/              # Service configuration
└── MotorcycleRAG.Shared/           # Shared utilities
    ├── Extensions/                 # Extension methods
    ├── Helpers/                    # Utility classes
    └── Constants/                  # Application constants
```

## Key Interface Locations

- **Core Service Interfaces**: `src/MotorcycleRAG.Core/Interfaces/`
  - `IMotorcycleRAGService.cs` - Main service contract
  - `IAgentOrchestrator.cs` - Multi-agent coordination
  - `ISearchAgent.cs` - Search agent contract
  - `IDataProcessor<T>.cs` - Data processing contract

- **Agent Implementations**: `src/MotorcycleRAG.Core/Agents/`
  - `QueryPlannerAgent.cs` - Query analysis and planning
  - `VectorSearchAgent.cs` - Vector database search
  - `WebSearchAgent.cs` - Web source augmentation
  - `PDFSearchAgent.cs` - PDF manual search

- **Data Processors**: `src/MotorcycleRAG.Infrastructure/DataProcessing/`
  - `MotorcycleCSVProcessor.cs` - CSV specification processing
  - `MotorcyclePDFProcessor.cs` - PDF manual processing

## Test Structure

```
tests/
├── MotorcycleRAG.UnitTests/        # Unit tests
│   ├── Agents/                     # Agent unit tests
│   ├── Services/                   # Service unit tests
│   └── DataProcessing/             # Data processor tests
├── MotorcycleRAG.IntegrationTests/ # Integration tests
│   ├── API/                        # API endpoint tests
│   ├── Azure/                      # Azure service integration tests
│   └── EndToEnd/                   # Complete workflow tests
└── MotorcycleRAG.PerformanceTests/ # Load and performance tests
```

## Configuration Files

- **API Configuration**: `src/MotorcycleRAG.API/appsettings.json`
- **Azure Service Config**: Environment-specific settings for Azure endpoints
- **Docker Configuration**: `docker/Dockerfile` and `docker-compose.yml`
- **CI/CD Pipeline**: `.github/workflows/` or Azure DevOps YAML

## Naming Conventions

- **Interfaces**: Prefix with `I` (e.g., `IMotorcycleRAGService`)
- **Implementations**: Descriptive names matching interface (e.g., `MotorcycleRAGService`)
- **Agents**: Suffix with `Agent` (e.g., `VectorSearchAgent`)
- **Models**: Clear domain names (e.g., `MotorcycleSpecification`, `SearchResult`)
- **Processors**: Suffix with `Processor` (e.g., `MotorcycleCSVProcessor`)

## Key Design Principles

- **Separation of Concerns**: Clear boundaries between API, Core, and Infrastructure layers
- **Dependency Injection**: All services registered in DI container
- **Interface-Based Design**: All major components implement interfaces for testability
- **Configuration-Driven**: Azure service endpoints and settings externalized
- **Async/Await Pattern**: All I/O operations use async patterns for scalability