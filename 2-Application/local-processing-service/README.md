# MotorcycleRAG Local Processing Service

The Local Processing Service is a Python/FastAPI application that performs local-first motorcycle document ingestion. It processes PDF manuals, CSV specifications, and deterministic bike-graph imports; produces chunks, embeddings, and optional graph data; writes artifacts to configured storage; and reports status to MotorcycleRAG API.

Admin Desktop normally supervises this service and hands it local work through a file-and-manifest watch folder. The service may also receive authenticated HTTP processing requests for supported input sources.

## Documentation

- [Developer onboarding](../../6-Docs/local-processing-service/onboarding.md)
- [Architecture](../../6-Docs/local-processing-service/architecture.md)
- [Requirements](../../6-Docs/local-processing-service/requirements.md)
