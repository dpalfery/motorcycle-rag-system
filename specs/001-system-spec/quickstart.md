# Quickstart — 001-system-spec

This quickstart describes how to run the system locally for development.

## Prereqs
- .NET SDK 10
- Node.js (LTS)
- (Optional) Azure credentials for real cloud integrations

## Backend API
1) From repo root:
- Build: `dotnet build MotorcycleRAG.sln`
- Run API: `dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj`

2) Health:
- `GET /health`

## Web Application (React 19)
1) From `1-Presentation/motorcycle-rag-ui/`:
- Install: `npm install`
- Dev server: `npm run dev`

## Admin Ingestion App (.NET MAUI)
- Planned as a .NET MAUI application targeting .NET 10 (Windows-first).
- It will support local chunking + vectorization (local models) and then push processed artifacts to the ingestion API.
- It will also manage MCP server/tool configuration and ship configuration updates to the API.

## Environment Variables (local)
- Use environment variables for secrets in local development.
- Do not commit secrets.

## Testing
- Run unit tests: `dotnet test MotorcycleRAG.sln`
