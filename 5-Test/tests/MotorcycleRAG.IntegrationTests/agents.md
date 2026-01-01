# Agent Context: MotorcycleRAG.IntegrationTests

## Invariant Rules
- **Type**: Integration Tests.
- **Stack**: .NET 10.0, xUnit, WebApplicationFactory.
- **Scope**: Test full slices (Application → Domain → Persistence).
- **Infrastructure**: Uses real database (or TestContainers).
- **Security**: [Security Rule: Active]. Verify authentication and authorization flows.

## Workflow Skills
- **Test**: `dotnet test`
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`
