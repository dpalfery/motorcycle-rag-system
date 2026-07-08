SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- =============================================================================
-- Migration: BikeModelCategory table (motorcycle category classifier cache)
-- Target: SQL Server 2012+ / Azure SQL Database (no Graph feature required)
-- Run: Execute once against the target database. Idempotent (uses IF NOT EXISTS).
--
-- Purpose:
--   Authoritative read/write cache for the D7 motorcycle category classifier.
--   The classifier keys on (Make, Model) -- year is deliberately NOT part of the
--   key -- so this table's natural key is (Make, Model). Reads are a deterministic
--   equality seek on UQ_BikeModelCategory_Make_Model (no LIKE, no year, no graph
--   traversal). This resolves the R5 make-vs-name impedance: existing Motorcycle
--   graph nodes are named "{model} {year}" with no make, which cannot serve a
--   (make, model) lookup. The graph still holds BELONGS_TO relationships for
--   entity/relationship queries; this table is the classifier cache.
--
-- Provenance:
--   [Source] distinguishes LLM-derived classifications ('Classifier') from
--   deterministic ones ('CSV', 'Manual') so operators can audit/retrain.
--
-- Integrity:
--   A CHECK constraint enforces the 4 valid category tokens at the DB layer
--   (defense-in-depth alongside the Domain MotorcycleCategory value-object from T2).
--
-- Collation note:
--   Callers MUST normalize (Make, Model) via BikeModel.NormalizeName (Domain)
--   before calling Get/Upsert so that "honda CBR1000RR" and "Honda CBR1000RR"
--   resolve to the same cache row regardless of database collation.
-- =============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = N'BikeModelCategory'
)
BEGIN
    CREATE TABLE [dbo].[BikeModelCategory] (
        [Id]           UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        -- Widths match [dbo].[BikeModels].[Make]/[Model] for join compatibility.
        [Make]         NVARCHAR(256)    NOT NULL,
        [Model]        NVARCHAR(512)    NOT NULL,
        [Category]     NVARCHAR(50)     NOT NULL,
        [Confidence]   FLOAT                NULL,   -- optional LLM confidence (0.0-1.0)
        [Source]       NVARCHAR(32)     NOT NULL CONSTRAINT [DF_BikeModelCategory_Source] DEFAULT N'Classifier',
        [CreatedAtUtc] DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] DATETIME2(7)         NULL,

        CONSTRAINT [PK_BikeModelCategory] PRIMARY KEY CLUSTERED ([Id]),

        -- The 4 valid categories per D4/D7. Keep in sync with the Domain
        -- MotorcycleCategory value-object (T2); the DB enforces its own invariant.
        CONSTRAINT [CK_BikeModelCategory_Category] CHECK (
            [Category] IN (N'Dirt', N'Touring', N'Sport', N'Cruiser'))
    );

    -- Natural key == classifier cache key. Unique: one (Make, Model) -> one Category.
    -- Also backs the MERGE upsert in BikeModelCategoryRepository.UpsertAsync.
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_BikeModelCategory_Make_Model]
        ON [dbo].[BikeModelCategory] ([Make], [Model]);

    -- Supports "list/count bikes per category" and partition accounting.
    CREATE NONCLUSTERED INDEX [IX_BikeModelCategory_Category]
        ON [dbo].[BikeModelCategory] ([Category]);
END;
GO
