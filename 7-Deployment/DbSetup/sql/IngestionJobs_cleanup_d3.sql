-- Motorcycle RAG System - IngestionJobs D3 cleanup
-- Drops the thirteen legacy [dbo].[IngestionJobs] columns plus the stored
-- procedures, indexes, and constraints that reference them.
--
-- Legacy columns removed (all dead per the Domain Entity Setter plan, D3):
--   JobId, JobType, SourceFilePath, UserId, UserEmail, StartTime, EndTime,
--   CreatedAt, UpdatedAt, TotalRecordsProcessed, RecordsIndexed,
--   RecordsFailed, RecordsWithWarnings.
--
-- This script is forward-only and idempotent. Every destructive statement is
-- guarded by a COL_LENGTH / object-existence check, so it is safe to run
-- against a database regardless of which legacy columns physically exist.
--
-- Operation order is significant: the [CreatedAtUtc] backfill (Phase 3) MUST
-- run before [CreatedAt] / [StartTime] are dropped (Phase 5), otherwise rows
-- that relied on the ORDER BY COALESCE fallback would lose their ordering
-- source. Defaults and indexes on the legacy columns are dropped (Phase 2)
-- before the columns themselves, since SQL Server refuses to drop a column
-- that still owns a default constraint or index.

USE [MotorcycleRAG];
GO

-- =====================================================================
-- Phase 1 - Drop stored procedures that reference legacy columns.
-- None of these procs are invoked by application code; they exist only in
-- the bootstrap schema and all filter/order on dead columns.
-- =====================================================================

IF OBJECT_ID(N'[dbo].[sp_GetIngestionJobsByStatus]', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[sp_GetIngestionJobsByStatus];
    PRINT 'Dropped procedure sp_GetIngestionJobsByStatus';
END
GO

IF OBJECT_ID(N'[dbo].[sp_GetIngestionJobsByJobType]', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[sp_GetIngestionJobsByJobType];
    PRINT 'Dropped procedure sp_GetIngestionJobsByJobType';
END
GO

IF OBJECT_ID(N'[dbo].[sp_GetRecentIngestionJobs]', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[sp_GetRecentIngestionJobs];
    PRINT 'Dropped procedure sp_GetRecentIngestionJobs';
END
GO

IF OBJECT_ID(N'[dbo].[sp_GetIngestionJobsByUser]', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[sp_GetIngestionJobsByUser];
    PRINT 'Dropped procedure sp_GetIngestionJobsByUser';
END
GO

IF OBJECT_ID(N'[dbo].[sp_GetIngestionJobsByDateRange]', N'P') IS NOT NULL
BEGIN
    DROP PROCEDURE [dbo].[sp_GetIngestionJobsByDateRange];
    PRINT 'Dropped procedure sp_GetIngestionJobsByDateRange';
END
GO

-- =====================================================================
-- Phase 2 - Drop indexes, unique constraints, and default constraints
-- that depend on the legacy columns. SQL Server will not allow a column
-- drop while these objects still reference it.
-- =====================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    -- Named unique constraint on [JobId] (declared inline at table creation).
    IF EXISTS (
        SELECT 1
        FROM sys.objects
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'UQ_IngestionJobs_JobId'
          AND type IN (N'UQ', N'C'))
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP CONSTRAINT [UQ_IngestionJobs_JobId];
        PRINT 'Dropped constraint UQ_IngestionJobs_JobId';
    END

    -- Named indexes on legacy columns.
    DECLARE @IndexNames TABLE (Name SYSNAME NOT NULL);
    INSERT INTO @IndexNames (Name) VALUES
        (N'IX_IngestionJobs_JobId'),
        (N'IX_IngestionJobs_JobType'),
        (N'IX_IngestionJobs_StartTime'),
        (N'IX_IngestionJobs_UserId');

    DECLARE @IndexName SYSNAME;
    DECLARE index_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT i.name
        FROM @IndexNames n
        INNER JOIN sys.indexes i
            ON i.object_id = OBJECT_ID(N'dbo.IngestionJobs')
           AND i.name = n.Name
           AND i.is_primary_key = 0
           AND i.is_unique_constraint = 0;

    OPEN index_cursor;
    FETCH NEXT FROM index_cursor INTO @IndexName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC (N'DROP INDEX [' + @IndexName + N'] ON [dbo].[IngestionJobs]');
        PRINT 'Dropped index ' + @IndexName;
        FETCH NEXT FROM index_cursor INTO @IndexName;
    END

    CLOSE index_cursor;
    DEALLOCATE index_cursor;

    -- Default constraints bound to any of the legacy columns. Constraint
    -- names are not guaranteed (bootstrap names them DF_IngestionJobs_* but
    -- other deployments may differ), so resolve them dynamically from the
    -- column list.
    DECLARE @ColumnName SYSNAME;
    DECLARE @ConstraintName SYSNAME;
    DECLARE default_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT c.name, dc.name
        FROM sys.columns c
        INNER JOIN sys.default_constraints dc
            ON dc.parent_object_id = c.object_id
           AND dc.object_id = c.default_object_id
        WHERE c.object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND c.name IN (
                N'JobId', N'JobType', N'SourceFilePath', N'UserId',
                N'UserEmail', N'StartTime', N'EndTime', N'CreatedAt',
                N'UpdatedAt', N'TotalRecordsProcessed', N'RecordsIndexed',
                N'RecordsFailed', N'RecordsWithWarnings');

    OPEN default_cursor;
    FETCH NEXT FROM default_cursor INTO @ColumnName, @ConstraintName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC (N'ALTER TABLE [dbo].[IngestionJobs] DROP CONSTRAINT [' + @ConstraintName + N']');
        PRINT 'Dropped default constraint ' + @ConstraintName + N' on column ' + @ColumnName;
        FETCH NEXT FROM default_cursor INTO @ColumnName, @ConstraintName;
    END

    CLOSE default_cursor;
    DEALLOCATE default_cursor;
END
GO

-- =====================================================================
-- Phase 3 - Safety backfill of [CreatedAtUtc] from [CreatedAt] / [StartTime]
-- for any row that still lacks it. This preserves chronological ordering
-- once the fallback columns are gone (Phase 5). Runs only when the source
-- columns physically exist; otherwise [CreatedAtUtc] is already authoritative.
-- =====================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
   AND COL_LENGTH('dbo.IngestionJobs', 'CreatedAtUtc') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.IngestionJobs', 'CreatedAt') IS NOT NULL
    BEGIN
        UPDATE [dbo].[IngestionJobs]
        SET [CreatedAtUtc] = [CreatedAt]
        WHERE [CreatedAtUtc] IS NULL
          AND [CreatedAt] IS NOT NULL;
        PRINT 'Backfilled [CreatedAtUtc] from [CreatedAt]';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'StartTime') IS NOT NULL
    BEGIN
        UPDATE [dbo].[IngestionJobs]
        SET [CreatedAtUtc] = [StartTime]
        WHERE [CreatedAtUtc] IS NULL
          AND [StartTime] IS NOT NULL;
        PRINT 'Backfilled [CreatedAtUtc] from [StartTime]';
    END

    -- Final guarantee: any remaining NULL becomes now, so ordering and the
    -- NOT NULL contract on [CreatedAtUtc] hold after the legacy columns drop.
    UPDATE [dbo].[IngestionJobs]
    SET [CreatedAtUtc] = SYSUTCDATETIME()
    WHERE [CreatedAtUtc] IS NULL;
    PRINT 'Backfilled remaining NULL [CreatedAtUtc] with SYSUTCDATETIME()';

    -- Verification: surface (but do not fail on) any residual NULLs so an
    -- operator can decide before the column drop. With the backfill above
    -- this count should always be zero.
    DECLARE @NullCreatedAtUtc INT;
    SELECT @NullCreatedAtUtc = COUNT(*)
    FROM [dbo].[IngestionJobs]
    WHERE [CreatedAtUtc] IS NULL;

    PRINT 'Residual NULL [CreatedAtUtc] rows after backfill: '
          + CONVERT(NVARCHAR(20), @NullCreatedAtUtc);
END
GO

-- =====================================================================
-- Phase 4 - No DDL. The ORDER BY COALESCE([CreatedAtUtc], [CreatedAt],
-- [StartTime]) -> [CreatedAtUtc] simplification is a C# repository change
-- (IngestionJobRepository) covered by the Row+Map migration task.
-- =====================================================================

-- =====================================================================
-- Phase 5 - Drop the thirteen legacy columns. Each drop is individually
-- guarded by COL_LENGTH so re-running the script is a no-op.
-- =====================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    IF COL_LENGTH('dbo.IngestionJobs', 'JobId') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [JobId];
        PRINT 'Dropped column JobId';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'JobType') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [JobType];
        PRINT 'Dropped column JobType';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'SourceFilePath') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [SourceFilePath];
        PRINT 'Dropped column SourceFilePath';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'UserId') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [UserId];
        PRINT 'Dropped column UserId';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'UserEmail') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [UserEmail];
        PRINT 'Dropped column UserEmail';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'StartTime') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [StartTime];
        PRINT 'Dropped column StartTime';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'EndTime') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [EndTime];
        PRINT 'Dropped column EndTime';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'CreatedAt') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [CreatedAt];
        PRINT 'Dropped column CreatedAt';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'UpdatedAt') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [UpdatedAt];
        PRINT 'Dropped column UpdatedAt';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'TotalRecordsProcessed') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [TotalRecordsProcessed];
        PRINT 'Dropped column TotalRecordsProcessed';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'RecordsIndexed') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [RecordsIndexed];
        PRINT 'Dropped column RecordsIndexed';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'RecordsFailed') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [RecordsFailed];
        PRINT 'Dropped column RecordsFailed';
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'RecordsWithWarnings') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs] DROP COLUMN [RecordsWithWarnings];
        PRINT 'Dropped column RecordsWithWarnings';
    END
END
GO

PRINT 'IngestionJobs D3 cleanup completed successfully!';
GO
