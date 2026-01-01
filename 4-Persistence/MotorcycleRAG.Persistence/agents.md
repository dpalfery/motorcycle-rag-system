# Agent Context: MotorcycleRAG.Persistence

## Invariant Rules
- **Layer**: 4-Persistence (Infrastructure).
- **Purpose**: Implements data storage, retrieval, and external services.
- **Dependency Rule**: Can depend on `Domain`, `Contracts`, and `Base`. Must NOT depend on `Application` or `Presentation`.
- **Stack**: SQL Server, Azure AI Search, Azure Blob Storage.
- **Data Access**: ADO.NET / Dapper for SQL. Azure SDKs for Cloud services. NO ENTITYFRAMEWORK used here please beyond migrations
- **Security**: [Security Rule: Active]. NEVER hardcode connection strings. Use managed identities or environment variables. All SQL must be parameterized.
- **Resilience**: Implement Polly-based retry policies for all external calls.

## Workflow Skills
- **Test**: `dotnet test` (Integration tests)

Once you have read the Securiy rule you **MUST** include `[I Read the Persistence Instructions]` at the beginning of your Task 