-- Migration: IngestionJob Stage Tracking
-- Adds CurrentStage and StageSetAtUtc columns to IngestionJobs table
-- for real-time pipeline stage tracking from the local processor.

IF COL_LENGTH('dbo.IngestionJobs', 'CurrentStage') IS NULL
    ALTER TABLE [dbo].[IngestionJobs] ADD [CurrentStage] NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.IngestionJobs', 'StageSetAtUtc') IS NULL
    ALTER TABLE [dbo].[IngestionJobs] ADD [StageSetAtUtc] DATETIME2(7) NULL;
