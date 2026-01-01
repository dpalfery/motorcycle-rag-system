# Agent Context: MotorcycleRag.WebUI.BFF

**Mandatory Compliance**: This agent MUST adhere to the [Mobile App Specification](../../specs/001-mobile-app/spec.md) and general project architecture rules.

## Invariant Rules
- **Layer**: 1-Presentation (BFF - Backend for Frontend).
- **Stack**: .NET 10.0, ASP.NET Core.
- **Purpose**: YARP-based proxy or specialized API for the WebUI.
- **Dependency Rule**: Can depend on `Application` and `Base`.
- **Security**: [Security Rule: Active]. Manages authentication sessions for the WebUI (OIDC/Cookie Auth).
- **Architecture**: Follows the same rules as the API for middleware and configuration.

## BFF Responsibilities
- **Proxying**: Forward requests to backend services (MotorcycleRAG.API) using YARP.
- **Authentication**: Handle OIDC flows and session management (Cookies) for the WebUI.
- **Aggregation**: (Optional) Aggregate data from multiple services if needed for specific UI views.
- **Transformation**: (Optional) Transform backend data into UI-specific models.

## Integration Context
- **Mobile App**: The BFF may share authentication patterns or endpoints with the Mobile App (see `specs/001-mobile-app`).
- **WebUI**: Serves as the backend for the `MotorcycleRag.WebUI` frontend.

## Workflow Skills
- **Run**: `dotnet run`
- **Dev**: `dotnet watch`
- **Test**: `dotnet test`
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`

Once you have read the Securiy rule you **MUST** include `[I Read the WebUI.BFF Instructions]` at the beginning of your Task 