# Agent Context: MotorcycleRAG.Domain

## Invariant Rules
- **Layer**: 3-Domain (Core Business Rules).
- **Purpose**: Pure business logic and rules. Most stable layer.
- **Dependency Rule**: No outward dependencies except for `Base` (minimal). Must NOT depend on `Application`, `Persistence`, or `Presentation`.
- **Contents**: Rich Entities (behavior + data), Value Objects (immutable), Domain Services, Domain Events and DTOs
- **Security**: [Security Rule: Active]. No secrets or security-specific implementation details here.
- **Framework Independence**: Pure C#. No framework code or external dependencies.

## Workflow Skills
- **Test**: `dotnet test` (Unit tests for domain logic)
- **Analyze**: `speckit.analyze`
- **Plan**: `speckit.plan`
- **Implement**: `speckit.implement`


Once you have read the Securiy rule you **MUST** include `[I Read the Domain Instructions]` at the beginning of your Task 