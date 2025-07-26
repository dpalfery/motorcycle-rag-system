# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Build and Test
```bash
# Build the entire solution
dotnet build

# Run all tests (unit + integration)
dotnet test

# Run only unit tests
dotnet test tests/MotorcycleRAG.UnitTests

# Run only integration tests
dotnet test tests/MotorcycleRAG.IntegrationTests

# Run a specific test class
dotnet test --filter "FullyQualifiedName~QueryPlannerAgentTests"

# Run with detailed output
dotnet test --verbosity normal
```

### Development Server
```bash
# Run the API (development mode)
dotnet run --project src/MotorcycleRAG.API

# Run with specific environment
dotnet run --project src/MotorcycleRAG.API --environment Development
```

### Project Structure
```bash
# Clean build artifacts
dotnet clean

# Restore NuGet packages
dotnet restore

# Build specific project
dotnet build src/MotorcycleRAG.Core
```

## Architecture Overview

This is a **multi-agent RAG system** built on Azure AI Foundry using **.NET 9** and **Semantic Kernel**. The system orchestrates multiple specialized agents to search heterogeneous data sources.

### Core Architecture Patterns

**Multi-Agent Coordination**: The system uses a sequential search pattern where:
1. **QueryPlannerAgent** (GPT-4o) analyzes user queries and creates search plans
2. **VectorSearchAgent** performs hybrid vector/keyword search on Azure AI Search  
3. **WebSearchAgent** augments with real-time web data
4. **PDF Search Agent** handles technical documentation
5. **AgentOrchestrator** coordinates the sequential flow

**Resilience Patterns**: Centralized resilience via `ResilienceService` at `/src/MotorcycleRAG.Infrastructure/Resilience/ResilienceService.cs:14` provides:
- Circuit breaker patterns for external service calls
- Retry policies with exponential backoff  
- Fallback mechanisms for graceful degradation
- Correlation tracking for distributed tracing

**Clean Architecture Layers**:
- **API Layer** (`MotorcycleRAG.API`): ASP.NET Core Web API with health checks
- **Core Layer** (`MotorcycleRAG.Core`): Business logic, agents, and interfaces
- **Infrastructure Layer** (`MotorcycleRAG.Infrastructure`): Azure service implementations, data processing
- **Shared Layer** (`MotorcycleRAG.Shared`): Common utilities and constants

### Key Implementation Details

**Agent Framework**: Uses Semantic Kernel for agent orchestration with dependency injection pattern. Each agent implements `ISearchAgent` interface.

**Azure Integration**: All Azure services (OpenAI, Search, Document Intelligence) use wrapper classes in `/src/MotorcycleRAG.Infrastructure/Azure/` with built-in resilience.

**Data Processing**: Supports heterogeneous data sources:
- CSV motorcycle specifications via `MotorcycleCSVProcessor`
- PDF technical manuals via `MotorcyclePDFProcessor` + Azure Document Intelligence
- Real-time web data via `WebSearchAgent`

**Search Strategy**: Hybrid approach combining vector embeddings (text-embedding-3-large) with keyword search for optimal relevance.

**Configuration**: Environment-specific settings in `appsettings.{Environment}.json` with strongly-typed configuration classes.

### Testing Strategy

- **Unit Tests**: Comprehensive coverage with Moq for mocking Azure services
- **Integration Tests**: End-to-end testing with real Azure services  
- **Resilience Tests**: Circuit breaker and retry pattern validation

## Project Specifications (from .kiro/specs)

### Product Vision
A sophisticated multi-agent RAG system for motorcycle information retrieval providing:
- **Unified Search Interface**: Single point for motorcycle specifications, maintenance procedures, and technical documentation
- **Sequential Search Pattern**: Vector DB → Web Augmentation → PDF Fallback ensures comprehensive coverage
- **Multi-Agent Intelligence**: GPT-4o query planning with specialized search agents

### Target Users & Use Cases
- **Motorcycle Enthusiasts**: Detailed specifications and technical information queries
- **Mechanics**: Maintenance procedures from PDF manuals with section/page citations
- **System Administrators**: CSV/PDF data ingestion and search index management

### Core Requirements Implementation

**Data Processing Requirements**:
- CSV files: Up to 100 columns with `delimitedText` parsing and `firstLineContainsHeaders`
- Row-based chunking preserves relational integrity between motorcycle specifications
- PDF manuals: Azure Document Intelligence Layout model with multimodal GPT-4 Vision processing
- Semantic chunking with embedding-based boundary detection for content preservation

**Search Flow Requirements**:
1. **Vector Search**: Hybrid vector/keyword search on indexed motorcycle data using text-embedding-3-large
2. **Web Augmentation**: Real-time web sources for additional context when vector search insufficient
3. **PDF Fallback**: Technical manual search when other sources lack comprehensive information
4. **Result Fusion**: Semantic ranking combines results from all sources into unified response

**Performance & Cost Requirements**:
- GPT-4o-mini for standard chat completion (cost optimization)
- GPT-4o for complex query planning and conversation analysis
- 100-1000 documents per batch processing for efficiency
- Query caching for common motorcycle information requests
- Vector compression for storage efficiency

**Resilience Requirements**:
- Circuit breaker patterns for Azure service rate limits
- Exponential backoff retry logic for transient failures
- Graceful degradation with fallback to cached/partial results
- Correlation IDs for distributed tracing across agents

### Implementation Status (from tasks.md)
✅ **Completed Tasks (1-8)**:
- Project structure and core interfaces
- Azure service clients with authentication
- Data models with validation
- CSV and PDF processors with chunking strategies
- Azure AI Search indexing service
- Vector Search Agent and Web Search Agent

🚧 **In Progress/Remaining (9-20)**:
- PDF Search Agent implementation
- Query Planner Agent with GPT-4o integration  
- Agent Orchestrator using Semantic Kernel Agent Framework
- Resilience patterns and comprehensive error handling
- API endpoints with validation and documentation
- Caching, monitoring, and deployment configuration

### Development Guidelines

**Service Registration**: Use `ServiceCollectionExtensions` in Infrastructure layer for DI setup.

**Error Handling**: All external calls go through `ResilienceService` with correlation IDs for tracing.

**Async Patterns**: Consistent async/await throughout with proper cancellation token usage.

**Configuration**: Use `IOptions<T>` pattern for strongly-typed configuration injection.

**Agent Architecture**: Each agent implements `ISearchAgent` with `SearchAgentType` enum for identification.

**Data Processing**: All processors implement `IDataProcessor<T>` with `ProcessAsync` and `IndexAsync` methods.

**Model Configuration**: Use environment-specific settings for Azure service endpoints and model selection.

### Key Performance Targets
- Response time: < 3 seconds for 95th percentile queries
- Concurrent users: 100+ concurrent users supported
- Batch processing: 100-1000 documents per operation
- Cost optimization: GPT-4o-mini for 80%+ of operations