# Domain Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Domian.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- affected component documentation

## Scoped constraints

- Test entities, value objects, domain services, and domain events as pure logic without external dependencies.
- Entity tests must prove identity, invariants, and legal state transitions; do not add tests that only round-trip compiler-generated getters and setters on property bags.
- Value-object tests must prove immutability, equality-by-value, normalization, and any domain behavior. A DTO is not a substitute for a value object or Entity.
- If a classification test is added, it must enforce the approved Entity/DTO/value-object placement policy and allow only explicit, documented exceptions; do not rely on filenames alone.
- No database, HTTP, Azure, or filesystem dependencies.
- Keep tests fast, isolated, and deterministic.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Domian.Tests`.
