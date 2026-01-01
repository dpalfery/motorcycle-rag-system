# Agent Context: MotorcycleRAG.Application

## Invariant Rules
- **Layer**: 2-Application (Use Cases).
- **Stack**: .NET 10.0, C# 13.
- **Pattern**: CQRS (Commands & Queries).
- **Dependency Rule**: Can depend on `Domain`, `Contracts`, and `Base`. Must NOT depend on `Persistence` or `Presentation`.
- **Logic**: Orchestrates domain entities. Contains application-specific business rules.
- **Abstractions**: Defines interfaces for external dependencies (repositories, services).
- **Security**: [Security Rule: Active]. Enforce application-level authorization policies.
- **No Infrastructure**: No references to SQL, HTTP, or specific frameworks (e.g., EF Core).

## Workflow Skills
- **Test**: `dotnet test`
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`

Once you have read the Securiy rule you **MUST** include `[I Read the Application Instructions]` at the beginning of your Task 