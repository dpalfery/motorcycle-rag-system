# Motorcycle RAG System

An AI-powered information retrieval system that intelligently searches across multiple data sources to provide comprehensive motorcycle information. The system combines structured CSV specifications, PDF maintenance manuals, and web sources through a sophisticated multi-agent architecture.

## Overview

The Motorcycle RAG System is built on Azure AI Foundry platform and uses a multi-agent architecture to provide expert-level responses for motorcycle enthusiasts and mechanics. It features hybrid vector/keyword search across structured and unstructured data with intelligent query planning.

## Architecture

- **Platform**: Azure AI Foundry
- **Framework**: ASP.NET Core Web API (.NET 9.0)
- **Agent Framework**: Semantic Kernel Agent Framework
- **Containerization**: Docker with Azure Container Apps

## Key Features

- **Unified Search**: Single interface to query motorcycle specifications, maintenance procedures, and technical documentation
- **Multi-Source Intelligence**: Sequential search pattern (Vector DB → Web Augmentation → PDF Fallback)
- **Expert-Level Responses**: Multi-agent coordination provides contextual, accurate answers
- **Hybrid Search**: Vector/keyword search across structured and unstructured data
- **Cost-Optimized**: Uses GPT-4o-mini for standard operations, GPT-4o for complex planning

## Azure Services

- **Azure OpenAI**: GPT-4o (query planning), GPT-4o-mini (chat completion), text-embedding-3-large (embeddings)
- **Azure AI Search**: Hybrid vector/keyword search with indexing
- **Azure Document Intelligence**: PDF text extraction with Layout model
- **Application Insights**: Monitoring and telemetry
- **Azure Cost Management**: Resource usage tracking

## Project Structure

```
/
├── .kiro/                          # Kiro configuration and specs
│   ├── specs/                      # Project specifications
│   └── steering/                   # AI assistant guidance rules
├── src/                            # Source code
│   ├── MotorcycleRAG.API/          # Web API project (entry point)
│   ├── MotorcycleRAG.Core/         # Core business logic
│   ├── MotorcycleRAG.Infrastructure/ # External service integrations
│   └── MotorcycleRAG.Shared/       # Shared utilities
├── tests/                          # Test projects
│   └── MotorcycleRAG.UnitTests/    # Unit tests
├── docs/                           # Documentation
└── scripts/                        # Build and deployment scripts
```

## Key Components

### Data Processing

- **CSV Processing**: Row-based chunking with relational integrity preservation
- **PDF Processing**: Semantic chunking with embedding-based boundaries
- **Vector Operations**: Embedding generation and compression
- **Batch Processing**: 100-1000 documents per batch for efficiency

### Multi-Agent Architecture

- **Query Planner Agent**: Analyzes queries and plans search strategy
- **Vector Search Agent**: Searches vector database for relevant content
- **Web Search Agent**: Augments results with web sources
- **PDF Search Agent**: Searches PDF manuals as fallback

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Azure subscription with AI services
- Docker (for containerization)

### Development Commands

```bash
# Build the solution
dotnet build

# Run the application
dotnet run

# Run tests
dotnet test

# Restore packages
dotnet restore
```

### Docker Operations

```bash
# Build container
docker build -t motorcycle-rag-system .

# Run container locally
docker run -p 8080:80 motorcycle-rag-system
```

### Azure Deployment

```bash
# Deploy to Azure Container Apps
az containerapp up --name motorcycle-rag --resource-group rg-motorcycle-rag

# Monitor application logs
az containerapp logs show --name motorcycle-rag --resource-group rg-motorcycle-rag
```

## Configuration

The system uses environment-specific configuration for Azure services:

- Azure OpenAI endpoints and API keys
- Azure AI Search service configuration
- Azure Document Intelligence settings
- Application Insights instrumentation key

## Target Users

- **Motorcycle Enthusiasts**: Seeking detailed specifications and technical information
- **Mechanics**: Requiring access to maintenance procedures and technical documentation
- **System Administrators**: Managing motorcycle data ingestion and system operations

## Development Status

This project is currently in active development. Key implemented features:

✅ Core project structure and configuration
✅ Azure service client implementations with resilience patterns
✅ CSV data processor with intelligent chunking
✅ Comprehensive unit test coverage
🚧 PDF processing implementation (in progress)
🚧 Multi-agent orchestration (in progress)
🚧 Web API endpoints (in progress)

## Contributing

This project follows standard .NET development practices:

- Interface-based design for testability
- Dependency injection throughout
- Comprehensive unit testing
- Azure SDK best practices
- Async/await patterns for scalability

## License

[License information to be added]

## Support

[Support information to be added]
