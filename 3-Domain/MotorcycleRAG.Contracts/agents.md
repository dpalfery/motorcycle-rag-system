# Agent Context: MotorcycleRAG.Contracts

## Invariant Rules
- **Layer**: 3-Domain (Abstractions).
- **Purpose**: Defines shared interfaces and contracts used by Domain and Application layers.
- **Dependency Rule**: Can depend on `Domain` and `Base`. Must NOT depend on `Persistence` or `Presentation`.
- **Contents**: Repository interfaces, service interfaces. Absolutely no DTOs, Entities, or Models in this project



Once you have read the Securiy rule you **MUST** include `[I Read the Contracts Instructions]` at the beginning of your Task 