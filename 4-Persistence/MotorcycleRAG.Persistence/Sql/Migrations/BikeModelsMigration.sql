-- =============================================================================
-- Migration: BikeModels table
-- Target: SQL Server 2012+ (uses OFFSET/FETCH, MERGE)
-- Run: Execute once against the target database. Idempotent (uses IF NOT EXISTS).
-- =============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = N'BikeModels'
)
BEGIN
    CREATE TABLE [dbo].[BikeModels] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Make]         NVARCHAR(256)    NOT NULL,
        [Model]        NVARCHAR(512)    NOT NULL,
        [Year]         INT              NOT NULL,
        [Aliases]      NVARCHAR(MAX)        NULL,
        [CreatedAtUtc] DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] DATETIME2(7)         NULL
    );

    ALTER TABLE [dbo].[BikeModels]
        ADD CONSTRAINT [PK_BikeModels] PRIMARY KEY CLUSTERED ([Id]);

    -- Unique constraint used as the MERGE key in BikeModelRepository
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_BikeModels_Make_Model_Year]
        ON [dbo].[BikeModels] ([Make], [Model], [Year]);

    -- Index for sorted list queries (ORDER BY Make, Model, Year)
    CREATE NONCLUSTERED INDEX [IX_BikeModels_Make_Model_Year]
        ON [dbo].[BikeModels] ([Make], [Model], [Year]);
END;
GO
