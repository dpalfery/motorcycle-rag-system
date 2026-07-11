# MotorcycleRAG API Developer Onboarding

## Prerequisites

- .NET 10 SDK and a supported C# IDE.
- The dependencies configured for the selected environment: SQL persistence, blob storage, Azure AI/Search services, App Configuration, Key Vault, and Application Insights as applicable.
- Azure CLI or another authenticated development identity for read access and local credential acquisition. Never place cloud secrets in checked-in settings.
- Local ingestion development also needs the Python Local Processing Service and its own configuration when exercising ingestion end to end.

## Run locally

1. Restore from the repository root with `dotnet restore MotorcycleRAG.sln`.
2. Configure local values through .NET user secrets and approved development configuration. In Development, user secrets and environment variables override App Configuration; outside Development, configuration is sourced through the configured Azure App Configuration and Key Vault integration.
3. Start with `dotnet run --project 1-Presentation/MotorcycleRAG.API`.
4. Use the configured local HTTPS URL (the Local Processing Service example uses `https://localhost:7215`). Open the API documentation endpoint when enabled by the environment.
5. For manual ingestion, use the current two-step contract: upload a source through `POST /api/ingestion/jobs/upload`, then create the job through `POST /api/ingestion/jobs`. Do not use retired file-upload routes.

## Debugging

- Set breakpoints in controllers only to verify HTTP binding, authorization, and response mapping; follow the injected application service for use-case behavior.
- Inspect structured logs and correlation IDs rather than logging raw prompts, query text, tokens, or other sensitive data.
- Start with `/health` when a dependent service is suspected. For ingestion, compare API job state with the local processor health and its `processorRunId` correlation identifier.
- Run the focused project build with `dotnet build 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj`, then the relevant test project under `5-Test/tests/`.

## Non-standard procedures

- The API aligns Kestrel and multipart request limits with `Ingestion:MaxInputBytes`; update and test the single ingestion policy rather than adding a controller-local size exception.
- Admin endpoints require explicit authorization and client isolation. Test them with the Admin client and the appropriate Entra role, not merely any valid bearer token.
- Browser clients should reach `/api/*` through `MotorcycleRag.WebUI.BFF`; CORS must be configured only for trusted origins, including the Admin Desktop development webview origin where required.
