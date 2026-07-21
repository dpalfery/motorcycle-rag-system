---
id: adr/2026-07-16-schema-deployment-idempotent-sql
title: "ADR-2026-07-16: Schema Deployment — Idempotent SQL"
doc-type: adr
status: current
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# ADR-2026-07-16: Schema Deployment — Idempotent SQL

**Status:** Accepted  
**Date:** 2026-07-16

## Decision

Database schema is deployed via a single idempotent SQL file
(`4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql`) applied by
`sqlcmd` in CI/CD and by the Database Setup CLI (`.NET 10` console app) in
local development. No migration framework is used today.

## Rationale

- A single-team, single-database development environment does not yet
  require ordered migration scripts or rollback tracking.
- Idempotent guards (`IF NOT EXISTS`, `IF COL_LENGTH(...) IS NULL`) make
  re-running the file safe without version tracking.
- Both deployment paths execute the same file, eliminating drift between
  local and production schema states.
- Wrapping every construct in conditional checks is a simple, low-ceremony
  pattern that every developer on the team already understands.

## Consequences

- There is no version-tracking table and no automated rollback. Reverting a
  schema change requires a manual idempotent `ALTER` wrapped in a
  `COL_LENGTH` guard.
- Stored procedure body changes require an explicit `DROP`/`ALTER` because
  the `IF NOT EXISTS` guard skips existing procedures. This must be enforced
  by code review.
- The concurrent [domain-entity plan Task 4](../archive/plans/2026-07-16-domain-entity-setter-encapsulation-and-inventory-gate.md)
  delivered its DDL cleanup via an idempotent T-SQL script
  (`7-Deployment/DbSetup/sql/IngestionJobs_cleanup_d3.sql`) using the same
  `COL_LENGTH` guarded pattern — no migration framework was introduced.
  Richer migration needs (reversible `Down` migrations, column-schema
  auditing) are deferred until a concrete requirement arises.
