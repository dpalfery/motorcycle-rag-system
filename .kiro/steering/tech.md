# Technology Stack

## Platform & Framework

- **Primary Platform**: Azure AI Foundry
- **Application Framework**: ASP.NET Core Web API (.NET)
- **Agent Framework**: Semantic Kernel Agent Framework
- **Containerization**: Docker with Azure Container Apps

## Azure Services

- **Azure OpenAI**: GPT-4o (query planning), GPT-4o-mini (chat completion), text-embedding-3-large (embeddings), GPT-4 Vision (multimodal)
- **Azure AI Search**: Hybrid vector/keyword search with indexing
- **Azure Document Intelligence**: PDF text extraction with Layout model
- **Application Insights**: Monitoring and telemetry
- **Azure Cost Management**: Resource usage tracking

## Key Libraries & Dependencies

- **Semantic Kernel**: Multi-agent orchestration and coordination
- **Azure SDK for .NET**: Service client implementations
- **Polly**: Resilience patterns (circuit breaker, retry policies)
- **DefaultAzureCredential**: Managed identity authentication

## Architecture Patterns

- **Multi-Agent Architecture**: Specialized agents for different search types
- **Sequential Search Pattern**: Vector DB → Web Augmentation → PDF Fallback
- **Circuit Breaker Pattern**: Resilience for Azure service calls
- **Retry with Exponential Backoff**: Error handling for rate limits
- **Microservices Pattern**: Component separation with clear interfaces

## Data Processing

- **CSV Processing**: Row-based chunking, up to 100 columns, header detection
- **PDF Processing**: Semantic chunking with embedding-based boundaries
- **Vector Operations**: Embedding generation and compression
- **Batch Processing**: 100-1000 documents per batch for efficiency

## Common Commands

### Development
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