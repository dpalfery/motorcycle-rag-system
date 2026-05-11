-- Migration: Manual Ingestion Tracking
-- Adds tables for tracking manual documents, processing runs, and stages.

-- Create ManualDocuments table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ManualDocuments')
BEGIN
    CREATE TABLE [dbo].[ManualDocuments] (
        [DocumentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [SourceFileName] NVARCHAR(512) NOT NULL,
        [CanonicalBlobContainer] NVARCHAR(256) NOT NULL,
        [CanonicalBlobPath] NVARCHAR(1024) NOT NULL,
        [CanonicalBlobUri] NVARCHAR(2048) NULL,
        [SourceContentHash] NVARCHAR(128) NULL,
        [DocumentType] NVARCHAR(100) NOT NULL,
        [Make] NVARCHAR(100) NULL,
        [Model] NVARCHAR(100) NULL,
        [Year] INT NULL,
        [UploadedAtUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CanonicalizedAtUtc] DATETIME2 NULL,
        [LastProcessedAtUtc] DATETIME2 NULL,
        [CurrentStatus] NVARCHAR(50) NOT NULL,
        [CurrentStage] NVARCHAR(100) NULL,
        [LastSuccessfulRunId] UNIQUEIDENTIFIER NULL,
        [LastFailure] NVARCHAR(MAX) NULL
    );
    
    CREATE INDEX [IX_ManualDocuments_CurrentStatus] ON [dbo].[ManualDocuments]([CurrentStatus]);
    CREATE INDEX [IX_ManualDocuments_Make_Model_Year] ON [dbo].[ManualDocuments]([Make], [Model], [Year]);
END
GO

-- Create ManualProcessingRuns table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ManualProcessingRuns')
BEGIN
    CREATE TABLE [dbo].[ManualProcessingRuns] (
        [RunId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [DocumentId] UNIQUEIDENTIFIER NOT NULL,
        [RunType] NVARCHAR(50) NOT NULL,
        [StartedAtUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CompletedAtUtc] DATETIME2 NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [StartedFromStage] NVARCHAR(100) NULL,
        [CompletedStage] NVARCHAR(100) NULL,
        [LocalWorkingFolder] NVARCHAR(1024) NULL,
        [ProcessorHost] NVARCHAR(256) NULL,
        [ErrorSummary] NVARCHAR(MAX) NULL,
        [ChunkCount] INT NULL,
        [GraphEntityCount] INT NULL,
        [GraphRelationCount] INT NULL,
        [VectorCount] INT NULL,
        CONSTRAINT [FK_ManualProcessingRuns_ManualDocuments] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[ManualDocuments]([DocumentId])
    );
    
    CREATE INDEX [IX_ManualProcessingRuns_DocumentId] ON [dbo].[ManualProcessingRuns]([DocumentId]);
    CREATE INDEX [IX_ManualProcessingRuns_Status] ON [dbo].[ManualProcessingRuns]([Status]);
    CREATE INDEX [IX_ManualProcessingRuns_StartedAtUtc] ON [dbo].[ManualProcessingRuns]([StartedAtUtc]);
END
GO

-- Create ManualProcessingStages table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ManualProcessingStages')
BEGIN
    CREATE TABLE [dbo].[ManualProcessingStages] (
        [StageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [RunId] UNIQUEIDENTIFIER NOT NULL,
        [StageName] NVARCHAR(100) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [StartedAtUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CompletedAtUtc] DATETIME2 NULL,
        [ArtifactPath] NVARCHAR(1024) NULL,
        [ArtifactHash] NVARCHAR(128) NULL,
        [MetadataJson] NVARCHAR(MAX) NULL,
        [ErrorDetail] NVARCHAR(MAX) NULL,
        CONSTRAINT [FK_ManualProcessingStages_ManualProcessingRuns] FOREIGN KEY ([RunId]) REFERENCES [dbo].[ManualProcessingRuns]([RunId])
    );
    
    CREATE INDEX [IX_ManualProcessingStages_RunId] ON [dbo].[ManualProcessingStages]([RunId]);
    CREATE INDEX [IX_ManualProcessingStages_StageName] ON [dbo].[ManualProcessingStages]([StageName]);
END
GO
