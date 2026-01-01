# Agent Context: MotorcycleRAG.API

## Invariant Rules
- **Layer**: 1-Presentation (Frameworks & Drivers).
- **Stack**: .NET 10.0, ASP.NET Core Web API, C# 13.
- **Pattern**: Minimal APIs (controllers only if filters/conventions needed).
- **Dependency Rule**: Can depend on `Application` and `Base`. Must NOT depend on `Persistence` or `Domain` directly.
- **Security**: [Security Rule: Active]. Enforce HTTPS/HSTS. Authorize every action. No hardcoded connection strings. Use MSAL/Entra ID for AuthN/AuthZ.
- **Data Access**: Enforce native ADO.NET/Dapper (no Entity Framework).
- **Middleware**: Order must be: `UseHttpsRedirection` → `UseCors` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization`.
- **API Docs**: OpenAPI (`Microsoft.AspNetCore.OpenApi`).
- **Config**: Use **Options pattern** + DI. Centralize settings.

## Workflow Skills
- **Run**: `dotnet run`
- **Dev**: `dotnet watch`
- **Test**: `dotnet test`
- **Pipeline**: `POST /api/DataPipeline/upload` (Upload Files), `POST /api/DataPipeline/process` (Trigger Processing).
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`
Once you have read the Securiy rule you **MUST** include `[I Read the API Instructions]` at the beginning of your Task 