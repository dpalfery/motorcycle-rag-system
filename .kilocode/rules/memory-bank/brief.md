# Brief

The Motorcycle RAG System is a sophisticated multi-agent RAG (Retrieval-Augmented Generation) system built on Azure AI Foundry platform for intelligent motorcycle information retrieval. It uses a sequential search pattern across heterogeneous data sources including CSV specifications, PDF manuals, and web sources.

The system implements clean architecture with .NET 10.0, Semantic Kernel for agent orchestration, and Azure AI services for search, document processing, and AI models.

## Key Characteristics
- Multi-agent architecture with specialized search capabilities
- Hybrid vector/keyword search on indexed motorcycle data
- Real-time web search augmentation for comprehensive results
- PDF document processing for technical manuals and specifications
- RESTful API with comprehensive Swagger documentation
- Production-ready deployment using Azure Container Apps
- Comprehensive monitoring and telemetry via Application Insights
- Resilient design with circuit breakers and retry patterns
- Cost-optimized through intelligent caching and resource management

## Current Status
- Fully implemented with layered architecture (Presentation, Application, Domain, Persistence)
- Comprehensive test coverage (unit and integration tests)
- Infrastructure-as-Code deployment with Pulumi
- CI/CD pipeline configured with GitHub Actions
- Azure naming standards and deployment guides documented