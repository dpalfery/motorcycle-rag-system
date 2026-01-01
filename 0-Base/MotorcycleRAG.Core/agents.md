# Agent Context: MotorcycleRAG.Core

## Invariant Rules
- **Layer**: 0-Base (Shared Kernel).
- **Purpose**: Cross-cutting concerns, shared abstractions, and utilities used across all layers.
- **Dependency Rule**: Must NOT depend on any other project layers (Application, Domain, Persistence, Presentation).
- **Framework Independence**: No implementations that depend on external frameworks (EF Core, Azure SDK, etc.). Only abstractions and pure utilities.
- **Security**: [Security Rule: Active]. Never hardcode secrets. Use environment variables.
- **Clean Architecture**: 1 class or interface per file. SRP, DRY, and SOLID principles.

## Workflow Skills
- **Build**: `dotnet build`
- **Test**: `dotnet test`
- **Analyze**: `speckit.analyze` - Analyze requirements and architecture.
- **Plan**: `speckit.plan` - Formulate implementation plans.
- **Implement**: `speckit.implement` - Execute code changes.
