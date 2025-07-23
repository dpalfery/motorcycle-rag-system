# Motorcycle RAG System

A sophisticated multi-agent RAG system for motorcycle information retrieval, built on Azure AI Foundry platform.

## Overview

This system implements a multi-agent architecture using Semantic Kernel to orchestrate intelligent search across heterogeneous data sources (CSV specifications, PDF manuals, web sources) using a sequential search pattern.

## Architecture

- **Query Planner Agent**: Analyzes user queries and determines optimal search strategy
- **Vector Search Agent**: Performs hybrid vector/keyword search on indexed motorcycle data
- **Web Search Agent**: Augments results with real-time web information
- **PDF Search Agent**: Searches technical manuals and documentation
- **Agent Orchestrator**: Coordinates the sequential search flow

## Technology Stack

- **Platform**: Azure AI Foundry
- **Framework**: ASP.NET Core Web API (.NET 9.0)
- **AI Services**: Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
- **Search**: Azure AI Search (hybrid vector/keyword)
- **Document Processing**: Azure Document Intelligence
- **Agent Framework**: Semantic Kernel
- **Testing**: xUnit, Moq
- **Monitoring**: Application Insights

## Project Structure

```
src/
├── MotorcycleRAG.API/          # Web API layer
├── MotorcycleRAG.Core/         # Business logic and interfaces
├── MotorcycleRAG.Infrastructure/ # Azure service implementations
└── MotorcycleRAG.Shared/       # Common utilities

tests/
├── MotorcycleRAG.UnitTests/        # Unit tests
└── MotorcycleRAG.IntegrationTests/ # Integration tests
```

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Azure subscription with:
  - Azure OpenAI service
  - Azure AI Search service
  - Azure Document Intelligence service
  - Application Insights

### Configuration

1. Update `appsettings.json` with your Azure service endpoints and keys
2. Configure Azure services according to the deployment guide
3. Run database migrations if applicable

### Running the Application

```bash
dotnet build
dotnet test
dotnet run --project src/MotorcycleRAG.API
```

## Development

### Build and Test

```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/MotorcycleRAG.UnitTests
```

### Architecture Principles

- Interface-based design with dependency injection
- Async/await patterns for all I/O operations
- Circuit breaker and retry patterns for resilience
- Comprehensive error handling and logging
- Cost optimization through intelligent caching

## Contributing

1. Follow the existing code structure and naming conventions
2. Ensure all tests pass before submitting PRs
3. Add unit tests for new functionality
4. Update documentation for API changes

## Custom Cursor Commands (Developer Tools)

This project includes custom Cursor commands for streamlined git workflows:

### 🚀 Quick Setup
1. **Global Commands**: See [`CURSOR_COMMANDS_SETUP.md`](CURSOR_COMMANDS_SETUP.md) to add `/push`, `/status`, and `/commit` commands to your global Cursor settings
2. **Workspace Tools**: The `.vscode/` folder contains tasks and keyboard shortcuts for this project

### Available Commands
- **`/push`** - Complete git workflow: save, stage, commit with smart message, and push
- **`/status`** - Quick git status and recent commits  
- **`/commit`** - Stage and commit with descriptive message (no push)
- **`Ctrl+Alt+P`** - Keyboard shortcut for complete push workflow

### Benefits
- **One-command workflow**: Type `/push` instead of multiple git commands
- **Smart commit messages**: Auto-generated based on actual changes
- **Works everywhere**: Available on any machine once configured
- **Consistent workflow**: Same commands across all projects

See [`CURSOR_COMMANDS.md`](CURSOR_COMMANDS.md) for complete documentation.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
