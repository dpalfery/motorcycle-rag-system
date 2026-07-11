# MotorcycleRAG API

MotorcycleRAG API is the ASP.NET Core 10 edge service for the MotorcycleRAG platform. It exposes authenticated query, ingestion, administration, and health endpoints; composes the application's use cases and infrastructure; and enforces cross-cutting security, rate limiting, telemetry, and error-handling policy.

Controllers remain HTTP adapters: application and persistence behavior belongs to the referenced layers, not in the API project. The API is consumed directly by trusted clients such as Admin Desktop and the Local Processing Service, and through the Web UI BFF for browser clients.

## Documentation

- [Developer onboarding](../../6-Docs/MotorcycleRAG.API/onboarding.md)
- [Architecture](../../6-Docs/MotorcycleRAG.API/architecture.md)
- [Requirements](../../6-Docs/MotorcycleRAG.API/requirements.md)
