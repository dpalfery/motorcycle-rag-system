---
id: adr/2026-07-13-domain-entity-dto-exceptions
title: "ADR-2026-07-13: Domain Entity and DTO classification exceptions"
doc-type: adr
status: current
owner: Architecture maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# ADR-2026-07-13: Domain Entity and DTO classification exceptions

**Status:** Accepted  
**Date:** 2026-07-13

## Decision

The model classification policy is enforced without per-type coverage or placement
exemptions. A temporary exception is permitted only when a migration or compatibility
boundary is explicitly recorded against this ADR key, includes a non-empty rationale,
and has a documented removal condition.

Shared property bags belong in `MotorcycleRAG.Contracts.Models` and use the `Dto` suffix;
use-case-local property bags belong in Application. Domain Entities must own an invariant,
legal state transition, or other recognized domain behavior and must not expose public
setters that bypass those rules.

## Rationale

The current Entity-to-DTO migration is staged across work packages 2-5. The classification
test therefore keeps a post-migration inventory gate ready but disabled until those packages
remove the existing property bags. Once enabled, the gate has no per-type allowlist and fails
any new property bag or publicly mutable type under `MotorcycleRAG.Domain.Entities`.

## Consequences

- New exceptions require an ADR key in the canonical registry and a focused behavior test.
- The exception rationale must explain the boundary and when the exception will be removed.
- The post-migration inventory gate is removed from its temporary skipped state during plan
  closeout and becomes part of the normal Domain/Contracts test run.
