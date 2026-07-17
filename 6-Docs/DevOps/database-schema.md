# Database Schema Deployment

This document describes the MotorcycleRAG project's database schema deployment strategy, the canonical schema file, idempotency patterns, deployment paths, known limitations, and the planned future direction.

## Overview / Strategy Summary

The project uses a **single-file, idempotent, upgrade-in-place** approach to database schema management. All schema state — tables, columns, indexes, stored procedures, and data backfills — lives in one monolithic SQL file:

- `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql`

Every construct is guarded by `IF NOT EXISTS` or `IF COL_LENGTH(...) IS NULL` checks, making the file safe to run repeatedly against the same database. There is no migration framework, no version-tracking table, and no ordered migration runner today (see [Planned direction](#planned-direction-fluentmigrator) below).

This approach is pragmatic for a single-team, single-database development environment where the same file must execute identically in local Docker SQL Server and Azure SQL.

## Canonical Schema File

| Attribute | Value |
| --- | --- |
| **Path** | `4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql` |
| **Size** | ~1,124 lines |
| **Role** | Single source of truth for all database schema |
| **Reentrancy** | Fully idempotent — guarded by existence/column checks |

Both deployment paths (local CLI and CI/CD) execute this same file. There is no secondary schema source.

## Idempotency Patterns

Every DDL construct in `schema.sql` is guarded. The following patterns are used throughout.

### Tables

```sql
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TableName')
BEGIN
    CREATE TABLE [dbo].[TableName] (
        [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        ...
    );
    CREATE INDEX [IX_TableName_Column] ON [dbo].[TableName]([Column]);
END
```

### New columns on existing tables

```sql
IF COL_LENGTH('dbo.TableName', 'NewColumn') IS NULL
    ALTER TABLE [dbo].[TableName] ADD [NewColumn] NVARCHAR(50) NULL;
```

### Indexes

```sql
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_TableName_Column' AND object_id = OBJECT_ID('dbo.TableName')
)
    CREATE INDEX [IX_TableName_Column] ON [dbo].[TableName]([Column]);
```

### Stored procedures

Because `CREATE PROCEDURE` must be the first statement in a batch, the guard wraps the DDL in `EXEC()`:

```sql
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_ProcedureName')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_ProcedureName]
        @Param NVARCHAR(128)
    AS
    BEGIN
        -- body
    END
    ')
END
```

### Data backfills

Safe-to-re-run updates that populate new columns from legacy columns:

```sql
UPDATE [dbo].[TableName]
SET [NewColumn] = COALESCE([OldColumn], SYSUTCDATETIME())
WHERE [NewColumn] IS NULL;
```

---

### Upgrade-in-place sections

Several tables have dedicated **"Upgrade legacy deployments in place"** blocks that run unconditionally (not behind a table `IF NOT EXISTS`) and add columns, fix default constraints, drop/recreate renamed indexes, and normalize legacy data values via `CASE` expressions. Examples: `Users` (line ~41), `IngestionJobs` (line ~419).

## Known Limitation: Stored Procedure Updates

Because stored procedures are guarded by `IF NOT EXISTS`, modifying the body of an **existing** stored procedure will NOT propagate on re-run — the guard evaluates to `false` and skips the `CREATE PROCEDURE` block entirely.

**Authoring rule:** To update an existing stored procedure, the author **must** add an explicit `DROP PROCEDURE IF EXISTS` / `ALTER PROCEDURE` statement *before* the `CREATE PROCEDURE` guard, or replace the guard with an `ALTER PROCEDURE` statement that is not conditional.

This is the single most important caveat for developers evolving the schema — test your procedure changes on a fresh database or by adding the explicit DROP.

## Deployment Paths

Both paths execute the exact same `schema.sql` file.

### Local Development — Database Setup CLI

- **Tool:** `7-Deployment/DbSetup/MotorcycleRAG.DbSetup/` (.NET 10 console app)
- **Command reference:** [`7-Deployment/DbSetup/README.md`](../../7-Deployment/DbSetup/README.md)
- **Detailed docs:** [`database-setup.md`](database-setup.md)

The CLI's `SqlScriptExecutor.cs` reads `schema.sql`, splits it into batches on standalone `GO` statements (regex `^\s*GO\s*$`, case-insensitive), and executes each batch via `SqlCommand.ExecuteNonQueryAsync` with a 5-minute timeout. It targets a local Docker SQL Server container (`hotshot_sqlserver` on `localhost:1433`). The CLI also provisions the database, login, and application user.

### CI/CD — GitHub Actions deploy.yml

The pipeline step "Run Database Schema Migrations" (`.github/workflows/deploy.yml`, around lines 150–177) runs after Pulumi has provisioned the Azure SQL Server and Database:

```bash
sqlcmd -S <server>.database.windows.net,1433 -d <db> \
  -U $SQL_ADMIN_LOGIN -P $SQL_ADMIN_PASSWORD -N -C \
  -i 4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql -b
```

- `SQL_ADMIN_LOGIN` and `SQL_ADMIN_PASSWORD` are stored as GitHub repository secrets.
- The `-b` flag causes `sqlcmd` to abort on error.
- `mssql-tools18` is installed on the runner if not already present.

See [`overview.md`](overview.md) for the full CI/CD flow.

## Infrastructure vs. Schema Boundary

| Concern | Owned by | What it creates |
| --- | --- | --- |
| **Infrastructure** | Pulumi (`7-Deployment/infrastructure/Program.cs`) | Azure SQL Server, SQL Database, firewall rules, connection strings in Key Vault |
| **Schema** | `schema.sql` (via `sqlcmd`/DbSetup CLI) | Tables, columns, indexes, stored procedures, data backfills |

Pulumi provisions the server and database but does **not** deploy schema. The schema step is a separate, later phase in the CI/CD pipeline. This separation means schema changes can be reviewed independently and do not require a full Pulumi deployment.

## Versioning and Rollback

- There is **no version-tracking table** (no `__MigrationHistory`, `SchemaVersions`, or equivalent).
- There are **no numbered, ordered migration scripts** with `Up`/`Down` methods.
- There is **no automated rollback mechanism**.
- Idempotent guards make re-running the full file safe, which is the substitute for version tracking.
- Reverting a schema change (e.g., dropping a column) must be done manually by writing a new idempotent `ALTER TABLE ... DROP COLUMN` statement guarded by `IF COL_LENGTH(...) IS NOT NULL`.

## Historical Standalone Migration Scripts

Six idempotent SQL files exist at `4-Persistence/MotorcycleRAG.Persistence/Sql/Migrations/`:

- `GraphTablesMigration.sql`
- `BikeModelsMigration.sql`
- `BikeModelCategoryMigration.sql`
- `IngestionJobStageTrackingMigration.sql`
- `ManualIngestionTrackingMigration.sql`
- `UserOnboardingApprovalMigration.sql`

Each is individually idempotent (uses `IF NOT EXISTS` guards). Their content has been absorbed into `schema.sql`. **Neither deployment path executes these files.** They are historical artifacts from earlier development iterations.

> Do not delete these files without explicit approval — they serve as a record of incremental additions.

## How to Evolve the Schema

When adding or modifying database objects, follow this checklist:

### Adding a new table
1. Add a `CREATE TABLE` block wrapped in `IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'N')`.
2. Add associated indexes and foreign keys inside the same `BEGIN ... END`.
3. Ensure the table is preceded and followed by a `GO` batch separator.

### Adding a new column to an existing table
1. Add `IF COL_LENGTH('dbo.TableName', 'ColumnName') IS NULL ALTER TABLE ... ADD ...`.
2. If the column should not be nullable in production, include a `DEFAULT` constraint so existing rows are populated.
3. Add a data backfill `UPDATE ... SET col = ... WHERE col IS NULL` after the `ALTER` (outside the `IF` block so it runs unconditionally).

### Adding a new index
1. Add `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'N' AND object_id = OBJECT_ID('dbo.T')) CREATE INDEX ...`.

### Adding a new stored procedure
1. Wrap `CREATE PROCEDURE` inside `EXEC('...')` and guard with `IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'N')`.

### Modifying an existing stored procedure
1. **This is the most important rule:** The `IF NOT EXISTS` guard will skip an existing procedure. You must either:
   - Add `DROP PROCEDURE IF EXISTS [dbo].[sp_Name];` before the `CREATE PROCEDURE` block, **or**
   - Replace the `IF NOT EXISTS ... CREATE` pattern with `ALTER PROCEDURE [dbo].[sp_Name] ...` (which works regardless of whether the procedure exists).

### Bringing the change to a database
- **Local:** Run the DbSetup CLI (`cd 7-Deployment/DbSetup/MotorcycleRAG.DbSetup && dotnet run`). The CLI reads `schema.sql` as-is, so any change to that file is picked up immediately.
- **Azure:** Push to `main` or `develop`. The `deploy.yml` pipeline runs the `sqlcmd` step automatically.

## Planned Direction: FluentMigrator

> ⚠️ **Not yet implemented.** This section describes an intended future direction, not current guidance.

The domain-entity setter-encapsulation plan ([`2026-07-16-domain-entity-setter-encapsulation-and-inventory-gate.md`](../plans/2026-07-16-domain-entity-setter-encapsulation-and-inventory-gate.md), Task 4) calls for introducing **FluentMigrator** for a targeted schema migration on the `[dbo].[IngestionJobs]` table — backfilling `[CreatedAtUtc]`, dropping legacy columns, and simplifying ordering expressions. The migration must be reversible (include a `Down` migration).

When FluentMigrator is adopted:
- The `dal-dev` agent owns FluentMigrator migration scripts (per its agent definition).
- New ordered migrations would coexist with, and eventually supplement, the monolithic `schema.sql`.
- The deployed-schema-approval gate governance concept (referenced in the plan) would ensure migration scripts are reviewed before application.

Until then, all schema changes go through `schema.sql` using the idempotent patterns described above.

---

**See also:**
- [Database Setup CLI](database-setup.md) — local development CLI reference
- [Deployment overview](overview.md) — CI/CD flow and infrastructure
- [ADR-2026-07-16: Schema Deployment — Idempotent SQL](../adr/ADR-2026-07-16-schema-deployment-idempotent-sql.md) — architecture decision record
