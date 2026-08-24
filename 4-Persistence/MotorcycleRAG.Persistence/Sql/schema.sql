SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Motorcycle RAG System - SQL Schema
-- This script creates the core database tables for the Motorcycle RAG System.
-- Connect to the target database before running (no USE/CREATE DATABASE - supports Azure SQL).

-- Create Users table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] NVARCHAR(128) NOT NULL PRIMARY KEY,
        [Email] NVARCHAR(256) NOT NULL,
        [DisplayName] NVARCHAR(100) NULL,
        [FirstName] NVARCHAR(50) NULL,
        [LastName] NVARCHAR(50) NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [CreatedDate] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [LastUpdatedDate] DATETIME2 NULL,
        [PlanId] NVARCHAR(128) NULL,
        [TierLabel] NVARCHAR(50) NULL,
        [AccessState] NVARCHAR(50) NOT NULL DEFAULT N'None',
        [CancelledAtUtc] DATETIME2 NULL,
        [CancelledByUserId] NVARCHAR(128) NULL,
        [CancelReason] NVARCHAR(500) NULL,
        [AuthProvider] NVARCHAR(50) NULL,
        [ProviderUserId] NVARCHAR(128) NULL,
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [UQ_Users_Email] UNIQUE ([Email])
    );
    
    CREATE INDEX [IX_Users_Email] ON [dbo].[Users]([Email]);
    CREATE INDEX [IX_Users_IsEnabled] ON [dbo].[Users]([IsEnabled]);
    CREATE INDEX [IX_Users_PlanId] ON [dbo].[Users]([PlanId]);
    CREATE UNIQUE INDEX [UX_Users_ProviderUserId_NotNull] ON [dbo].[Users]([ProviderUserId]) WHERE [ProviderUserId] IS NOT NULL;
END
GO

-- Upgrade legacy Users deployments in place so onboarding state and access revocation metadata exist.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
BEGIN
    IF COL_LENGTH('dbo.Users', 'TierLabel') IS NULL
        ALTER TABLE [dbo].[Users] ADD [TierLabel] NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.Users', 'AccessState') IS NULL
        ALTER TABLE [dbo].[Users] ADD [AccessState] NVARCHAR(50) NOT NULL CONSTRAINT [DF_Users_AccessState] DEFAULT N'None';

    IF COL_LENGTH('dbo.Users', 'CancelledAtUtc') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelledAtUtc] DATETIME2 NULL;

    IF COL_LENGTH('dbo.Users', 'CancelledByUserId') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelledByUserId] NVARCHAR(128) NULL;

    IF COL_LENGTH('dbo.Users', 'CancelReason') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelReason] NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.Users', 'RowVersion') IS NULL
        ALTER TABLE [dbo].[Users] ADD [RowVersion] ROWVERSION;

    IF EXISTS (
        SELECT 1
        FROM sys.key_constraints
        WHERE [name] = 'UQ_Users_ProviderUserId'
          AND [parent_object_id] = OBJECT_ID('dbo.Users')
    )
        ALTER TABLE [dbo].[Users] DROP CONSTRAINT [UQ_Users_ProviderUserId];

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_AccessState' AND object_id = OBJECT_ID('dbo.Users'))
        CREATE INDEX [IX_Users_AccessState] ON [dbo].[Users]([AccessState]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_TierLabel' AND object_id = OBJECT_ID('dbo.Users'))
        CREATE INDEX [IX_Users_TierLabel] ON [dbo].[Users]([TierLabel]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_ProviderUserId_NotNull' AND object_id = OBJECT_ID('dbo.Users'))
        CREATE UNIQUE INDEX [UX_Users_ProviderUserId_NotNull] ON [dbo].[Users]([ProviderUserId]) WHERE [ProviderUserId] IS NOT NULL;
END
GO

-- Create AccessRequests table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AccessRequests')
BEGIN
    CREATE TABLE [dbo].[AccessRequests] (
        [AccessRequestId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [RequestedEmail] NVARCHAR(256) NOT NULL,
        [RequestedProvider] NVARCHAR(50) NOT NULL,
        [RequestDecisionState] NVARCHAR(50) NOT NULL DEFAULT N'Pending',
        [OnboardingExecutionState] NVARCHAR(50) NOT NULL DEFAULT N'NotStarted',
        [RequestedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        [NotificationSentAtUtc] DATETIME2(7) NULL,
        [AssignedTier] NVARCHAR(50) NULL,
        [ApprovedByUserId] NVARCHAR(128) NULL,
        [ApprovedAtUtc] DATETIME2(7) NULL,
        [CancelledByUserId] NVARCHAR(128) NULL,
        [CancelledAtUtc] DATETIME2(7) NULL,
        [CancelReason] NVARCHAR(500) NULL,
        [OnboardingAttemptCount] INT NOT NULL DEFAULT 0,
        [LastFailureCode] NVARCHAR(100) NULL,
        [LastFailureMessage] NVARCHAR(1000) NULL,
        [ManagedUserId] NVARCHAR(128) NULL,
        [ExternalDirectoryObjectId] NVARCHAR(128) NULL,
        [CorrelationId] NVARCHAR(128) NOT NULL,
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [PK_AccessRequests] PRIMARY KEY CLUSTERED ([AccessRequestId]),
        CONSTRAINT [FK_AccessRequests_Users_ManagedUser] FOREIGN KEY ([ManagedUserId]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_AccessRequests_Users_ApprovedBy] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_AccessRequests_Users_CancelledBy] FOREIGN KEY ([CancelledByUserId]) REFERENCES [dbo].[Users]([Id])
    );

    CREATE INDEX [IX_AccessRequests_RequestedAtUtc] ON [dbo].[AccessRequests]([RequestedAtUtc]);
    CREATE INDEX [IX_AccessRequests_ManagedUserId] ON [dbo].[AccessRequests]([ManagedUserId]);
    CREATE INDEX [IX_AccessRequests_RowState] ON [dbo].[AccessRequests]([RequestDecisionState], [OnboardingExecutionState]);
    CREATE UNIQUE INDEX [UX_AccessRequests_PendingProviderEmail]
        ON [dbo].[AccessRequests]([RequestedProvider], [RequestedEmail])
        WHERE [RequestDecisionState] = N'Pending';
END
GO

-- Create UserIdentities table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserIdentities')
BEGIN
    CREATE TABLE [dbo].[UserIdentities] (
        [UserIdentityId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [ManagedUserId] NVARCHAR(128) NOT NULL,
        [Provider] NVARCHAR(50) NOT NULL,
        [ProviderEmail] NVARCHAR(256) NOT NULL,
        [Issuer] NVARCHAR(256) NULL,
        [Subject] NVARCHAR(256) NULL,
        [ProviderUserId] NVARCHAR(256) NULL,
        [ExternalDirectoryObjectId] NVARCHAR(128) NULL,
        [InvitationStatus] NVARCHAR(50) NOT NULL DEFAULT N'NotCreated',
        [InvitationCreatedAtUtc] DATETIME2(7) NULL,
        [InvitationRedeemedAtUtc] DATETIME2(7) NULL,
        [AccessRevokedAtUtc] DATETIME2(7) NULL,
        [LastSyncedAtUtc] DATETIME2(7) NULL,
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [PK_UserIdentities] PRIMARY KEY CLUSTERED ([UserIdentityId]),
        CONSTRAINT [FK_UserIdentities_Users] FOREIGN KEY ([ManagedUserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_UserIdentities_ManagedUserId] ON [dbo].[UserIdentities]([ManagedUserId]);
    CREATE INDEX [IX_UserIdentities_Issuer_Subject] ON [dbo].[UserIdentities]([Issuer], [Subject]);
    CREATE UNIQUE INDEX [UX_UserIdentities_ActiveProviderEmail]
        ON [dbo].[UserIdentities]([Provider], [ProviderEmail])
        WHERE [AccessRevokedAtUtc] IS NULL;
END
GO

-- Create OnboardingAttempts table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'OnboardingAttempts')
BEGIN
    CREATE TABLE [dbo].[OnboardingAttempts] (
        [OnboardingAttemptId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [AccessRequestId] UNIQUEIDENTIFIER NOT NULL,
        [AttemptNumber] INT NOT NULL,
        [StartedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        [CompletedAtUtc] DATETIME2(7) NULL,
        [Result] NVARCHAR(50) NOT NULL,
        [FailureStage] NVARCHAR(100) NULL,
        [FailureMessage] NVARCHAR(1000) NULL,
        CONSTRAINT [PK_OnboardingAttempts] PRIMARY KEY CLUSTERED ([OnboardingAttemptId]),
        CONSTRAINT [FK_OnboardingAttempts_AccessRequests] FOREIGN KEY ([AccessRequestId]) REFERENCES [dbo].[AccessRequests]([AccessRequestId]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [UX_OnboardingAttempts_Request_Attempt]
        ON [dbo].[OnboardingAttempts]([AccessRequestId], [AttemptNumber]);
    CREATE INDEX [IX_OnboardingAttempts_Result] ON [dbo].[OnboardingAttempts]([Result]);
END
GO

-- Create UserPlans table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserPlans')
BEGIN
    CREATE TABLE [dbo].[UserPlans] (
        [Id] NVARCHAR(128) NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [DailyRequestLimit] INT NOT NULL DEFAULT 100,
        [IsPaid] BIT NOT NULL DEFAULT 0,
        [CreatedDate] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [UQ_UserPlans_Name] UNIQUE ([Name])
    );
    
    CREATE INDEX [IX_UserPlans_IsPaid] ON [dbo].[UserPlans]([IsPaid]);
END
GO

-- Create Usage table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Usage')
BEGIN
    CREATE TABLE [dbo].[Usage] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] NVARCHAR(128) NOT NULL,
        [Endpoint] NVARCHAR(100) NOT NULL,
        [HttpMethod] NVARCHAR(10) NOT NULL,
        [QueryId] NVARCHAR(128) NULL,
        [RequestTime] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [DurationMs] BIGINT NOT NULL DEFAULT 0,
        [StatusCode] INT NOT NULL,
        [IsSuccess] BIT NOT NULL DEFAULT 0,
        [CallerIp] NVARCHAR(50) NULL,
        [UserAgent] NVARCHAR(500) NULL,
        CONSTRAINT [FK_Usage_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE
    );
    
    CREATE INDEX [IX_Usage_UserId] ON [dbo].[Usage]([UserId]);
    CREATE INDEX [IX_Usage_RequestTime] ON [dbo].[Usage]([RequestTime]);
    CREATE INDEX [IX_Usage_QueryId] ON [dbo].[Usage]([QueryId]);
    CREATE INDEX [IX_Usage_IsSuccess] ON [dbo].[Usage]([IsSuccess]);
END
GO

-- Create WebSources table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebSources')
BEGIN
    CREATE TABLE [dbo].[WebSources] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Url] NVARCHAR(1000) NOT NULL,
        [Name] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(1000) NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [TrustTier] INT NOT NULL DEFAULT 3,
        [CreatedDate] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [LastUpdatedDate] DATETIME2 NULL,
        [LastCrawledDate] DATETIME2 NULL,
        [CrawlFrequencyHours] INT NOT NULL DEFAULT 24,
        [IncludeInSearch] BIT NOT NULL DEFAULT 1,
        [MaxCrawlDepth] INT NOT NULL DEFAULT 2,
        CONSTRAINT [UQ_WebSources_Url] UNIQUE ([Url])
    );
    
    CREATE INDEX [IX_WebSources_IsEnabled] ON [dbo].[WebSources]([IsEnabled]);
    CREATE INDEX [IX_WebSources_TrustTier] ON [dbo].[WebSources]([TrustTier]);
    CREATE INDEX [IX_WebSources_IncludeInSearch] ON [dbo].[WebSources]([IncludeInSearch]);
END
GO

-- Create WebSourceCrawlResults table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebSourceCrawlResults')
BEGIN
    CREATE TABLE [dbo].[WebSourceCrawlResults] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [WebSourceId] INT NOT NULL,
        [CrawlStartTime] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CrawlEndTime] DATETIME2 NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        [PagesCrawled] INT NOT NULL DEFAULT 0,
        [PagesIndexed] INT NOT NULL DEFAULT 0,
        [Errors] INT NOT NULL DEFAULT 0,
        [ErrorMessage] NVARCHAR(1000) NULL,
        CONSTRAINT [FK_WebSourceCrawlResults_WebSources] FOREIGN KEY ([WebSourceId]) REFERENCES [dbo].[WebSources]([Id]) ON DELETE CASCADE
    );
    
    CREATE INDEX [IX_WebSourceCrawlResults_WebSourceId] ON [dbo].[WebSourceCrawlResults]([WebSourceId]);
    CREATE INDEX [IX_WebSourceCrawlResults_Status] ON [dbo].[WebSourceCrawlResults]([Status]);
    CREATE INDEX [IX_WebSourceCrawlResults_CrawlStartTime] ON [dbo].[WebSourceCrawlResults]([CrawlStartTime]);
END
GO

-- Create AuditLogs table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
BEGIN
    CREATE TABLE [dbo].[AuditLogs] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] NVARCHAR(128) NULL,
        [UserEmail] NVARCHAR(256) NULL,
        [Action] NVARCHAR(100) NOT NULL,
        [EntityType] NVARCHAR(100) NOT NULL,
        [EntityId] NVARCHAR(128) NOT NULL,
        [OldValue] NVARCHAR(MAX) NULL,
        [NewValue] NVARCHAR(MAX) NULL,
        [ActionDate] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [IpAddress] NVARCHAR(50) NULL,
        [UserAgent] NVARCHAR(500) NULL,
        [Metadata] NVARCHAR(1000) NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'Success',
        [ErrorMessage] NVARCHAR(1000) NULL
    );
    
    CREATE INDEX [IX_AuditLogs_UserId] ON [dbo].[AuditLogs]([UserId]);
    CREATE INDEX [IX_AuditLogs_EntityType] ON [dbo].[AuditLogs]([EntityType]);
    CREATE INDEX [IX_AuditLogs_EntityId] ON [dbo].[AuditLogs]([EntityId]);
    CREATE INDEX [IX_AuditLogs_ActionDate] ON [dbo].[AuditLogs]([ActionDate]);
    CREATE INDEX [IX_AuditLogs_Action] ON [dbo].[AuditLogs]([Action]);
    CREATE INDEX [IX_AuditLogs_Status] ON [dbo].[AuditLogs]([Status]);
END
GO

-- Create stored procedure for getting daily usage count
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetDailyUsageCount')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetDailyUsageCount]
        @UserId NVARCHAR(128),
        @Date DATE
    AS
    BEGIN
        SELECT COUNT(*) AS DailyCount
        FROM [dbo].[Usage]
        WHERE [UserId] = @UserId
        AND CONVERT(DATE, [RequestTime]) = @Date
    END
    ')
END
GO

-- Create stored procedure for getting usage by date range
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetUsageByDateRange')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetUsageByDateRange]
        @UserId NVARCHAR(128),
        @StartDate DATETIME2,
        @EndDate DATETIME2
    AS
    BEGIN
        SELECT *
        FROM [dbo].[Usage]
        WHERE [UserId] = @UserId
        AND [RequestTime] BETWEEN @StartDate AND @EndDate
        ORDER BY [RequestTime] DESC
    END
    ')
END
GO

-- Create stored procedure for getting audit logs by entity
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetAuditLogsByEntity')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetAuditLogsByEntity]
        @EntityType NVARCHAR(100),
        @EntityId NVARCHAR(128)
    AS
    BEGIN
        SELECT *
        FROM [dbo].[AuditLogs]
        WHERE [EntityType] = @EntityType
        AND [EntityId] = @EntityId
        ORDER BY [ActionDate] DESC
    END
    ')
END
GO

-- Create stored procedure for getting recent audit logs
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetRecentAuditLogs')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetRecentAuditLogs]
        @Limit INT = 100
    AS
    BEGIN
        SELECT TOP (@Limit) *
        FROM [dbo].[AuditLogs]
        ORDER BY [ActionDate] DESC
    END
    ')
END
GO

-- Create IngestionJobs table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    CREATE TABLE [dbo].[IngestionJobs] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [IngestionJobId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        [StartedAtUtc] DATETIME2(7) NULL,
        [CompletedAtUtc] DATETIME2(7) NULL,
        [CreatedBySubject] NVARCHAR(128) NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT N'Queued',
        [FailureReason] NVARCHAR(2000) NULL,
        [InputType] NVARCHAR(50) NOT NULL DEFAULT N'StructuredSpecification',
        [InputRef] NVARCHAR(500) NOT NULL DEFAULT N'',
        [ComputeProvider] NVARCHAR(128) NOT NULL DEFAULT N'Unknown',
        [DocIngestionRunId] NVARCHAR(128) NULL,
        [ManualDocumentId] UNIQUEIDENTIFIER NULL,
        [TotalPages] INT NULL,
        [PagesCapturedViewableCount] INT NULL,
        [PagesWithSearchableTextCount] INT NULL,
        [PagesWithOcrTextCount] INT NULL,
        [PagesWithNativeTextCount] INT NULL,
        [MissingPagesJson] NVARCHAR(MAX) NULL,
        [MetricsJson] NVARCHAR(MAX) NULL,
        [JobId] NVARCHAR(128) NOT NULL DEFAULT CONVERT(NVARCHAR(36), NEWID()),
        [JobType] NVARCHAR(50) NOT NULL DEFAULT N'unknown',
        [SourceFilePath] NVARCHAR(500) NOT NULL DEFAULT N'',
        [SourceFileName] NVARCHAR(500) NULL,
        [StartTime] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [EndTime] DATETIME2 NULL,
        [UserId] NVARCHAR(128) NULL,
        [UserEmail] NVARCHAR(256) NULL,
        [TotalRecordsProcessed] INT NOT NULL DEFAULT 0,
        [RecordsIndexed] INT NOT NULL DEFAULT 0,
        [RecordsFailed] INT NOT NULL DEFAULT 0,
        [RecordsWithWarnings] INT NOT NULL DEFAULT 0,
        [ErrorsJson] NVARCHAR(MAX) NULL,
        [ErrorMessage] NVARCHAR(2000) NULL,
        [MetadataJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [UQ_IngestionJobs_JobId] UNIQUE ([JobId])
    );
    
    CREATE UNIQUE INDEX [IX_IngestionJobs_IngestionJobId] ON [dbo].[IngestionJobs]([IngestionJobId]);
    CREATE INDEX [IX_IngestionJobs_JobId] ON [dbo].[IngestionJobs]([JobId]);
    CREATE INDEX [IX_IngestionJobs_Status] ON [dbo].[IngestionJobs]([Status]);
    CREATE INDEX [IX_IngestionJobs_InputType] ON [dbo].[IngestionJobs]([InputType]);
    CREATE INDEX [IX_IngestionJobs_InputRef_InputType] ON [dbo].[IngestionJobs]([InputRef], [InputType]);
    CREATE INDEX [IX_IngestionJobs_CreatedAtUtc] ON [dbo].[IngestionJobs]([CreatedAtUtc]);
    CREATE INDEX [IX_IngestionJobs_JobType] ON [dbo].[IngestionJobs]([JobType]);
    CREATE INDEX [IX_IngestionJobs_StartTime] ON [dbo].[IngestionJobs]([StartTime]);
    CREATE INDEX [IX_IngestionJobs_UserId] ON [dbo].[IngestionJobs]([UserId]);
END
GO

-- Upgrade legacy IngestionJobs deployments in place so the API and schema stay aligned.
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    IF COL_LENGTH('dbo.IngestionJobs', 'Id') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [Id] BIGINT IDENTITY(1,1) NOT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'IngestionJobId') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [IngestionJobId] UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'CreatedAtUtc') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [CreatedAtUtc] DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'StartedAtUtc') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [StartedAtUtc] DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'CompletedAtUtc') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [CompletedAtUtc] DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'CreatedBySubject') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [CreatedBySubject] NVARCHAR(128) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'FailureReason') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [FailureReason] NVARCHAR(2000) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'InputType') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [InputType] NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'InputRef') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [InputRef] NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'ComputeProvider') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [ComputeProvider] NVARCHAR(128) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'DocIngestionRunId') IS NULL
    BEGIN
        IF COL_LENGTH('dbo.IngestionJobs', 'FabricRunId') IS NOT NULL
        BEGIN
            EXEC sp_rename 'dbo.IngestionJobs.FabricRunId', 'DocIngestionRunId', 'COLUMN';
        END
        ELSE
        BEGIN
            ALTER TABLE [dbo].[IngestionJobs] ADD [DocIngestionRunId] NVARCHAR(128) NULL;
        END
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'ManualDocumentId') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [ManualDocumentId] UNIQUEIDENTIFIER NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'TotalPages') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [TotalPages] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'PagesCapturedViewableCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [PagesCapturedViewableCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'PagesWithSearchableTextCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [PagesWithSearchableTextCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'PagesWithOcrTextCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [PagesWithOcrTextCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'PagesWithNativeTextCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [PagesWithNativeTextCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'MissingPagesJson') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [MissingPagesJson] NVARCHAR(MAX) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'ExpectedChunkCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [ExpectedChunkCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'IndexedChunkCount') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [IndexedChunkCount] INT NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'CurrentStage') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [CurrentStage] NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'StageSetAtUtc') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [StageSetAtUtc] DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.IngestionJobs', 'SourceFileName') IS NULL
        ALTER TABLE [dbo].[IngestionJobs] ADD [SourceFileName] NVARCHAR(500) NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    IF COL_LENGTH('dbo.IngestionJobs', 'JobId') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.default_constraints dc
           INNER JOIN sys.columns c
               ON c.default_object_id = dc.object_id
           WHERE c.object_id = OBJECT_ID(N'dbo.IngestionJobs')
             AND c.name = N'JobId')
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs]
            ADD CONSTRAINT [DF_IngestionJobs_JobId]
            DEFAULT CONVERT(NVARCHAR(36), NEWID()) FOR [JobId];
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'JobType') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.default_constraints dc
           INNER JOIN sys.columns c
               ON c.default_object_id = dc.object_id
           WHERE c.object_id = OBJECT_ID(N'dbo.IngestionJobs')
             AND c.name = N'JobType')
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs]
            ADD CONSTRAINT [DF_IngestionJobs_JobType]
            DEFAULT N'unknown' FOR [JobType];
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'SourceFilePath') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.default_constraints dc
           INNER JOIN sys.columns c
               ON c.default_object_id = dc.object_id
           WHERE c.object_id = OBJECT_ID(N'dbo.IngestionJobs')
             AND c.name = N'SourceFilePath')
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs]
            ADD CONSTRAINT [DF_IngestionJobs_SourceFilePath]
            DEFAULT N'' FOR [SourceFilePath];
    END

    IF COL_LENGTH('dbo.IngestionJobs', 'StartTime') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.default_constraints dc
           INNER JOIN sys.columns c
               ON c.default_object_id = dc.object_id
           WHERE c.object_id = OBJECT_ID(N'dbo.IngestionJobs')
             AND c.name = N'StartTime')
    BEGIN
        ALTER TABLE [dbo].[IngestionJobs]
            ADD CONSTRAINT [DF_IngestionJobs_StartTime]
            DEFAULT SYSUTCDATETIME() FOR [StartTime];
    END
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    UPDATE [dbo].[IngestionJobs]
    SET [IngestionJobId] = COALESCE(
            TRY_CONVERT(UNIQUEIDENTIFIER, NULLIF([JobId], N'')),
            NEWID())
    WHERE [IngestionJobId] IS NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [CreatedAtUtc] = COALESCE([CreatedAt], [StartTime], SYSUTCDATETIME())
    WHERE [CreatedAtUtc] IS NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [StartedAtUtc] = COALESCE([StartedAtUtc], [StartTime])
    WHERE [StartedAtUtc] IS NULL
      AND [StartTime] IS NOT NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [CompletedAtUtc] = COALESCE([CompletedAtUtc], [EndTime])
    WHERE [CompletedAtUtc] IS NULL
      AND [EndTime] IS NOT NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [CreatedBySubject] = COALESCE(
            NULLIF([CreatedBySubject], N''),
            NULLIF([UserId], N''),
            NULLIF([UserEmail], N''))
    WHERE [CreatedBySubject] IS NULL
       OR LTRIM(RTRIM([CreatedBySubject])) = N'';

    UPDATE [dbo].[IngestionJobs]
    SET [FailureReason] = COALESCE([FailureReason], [ErrorMessage])
    WHERE [FailureReason] IS NULL
      AND [ErrorMessage] IS NOT NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [Status] = CASE LOWER(LTRIM(RTRIM([Status])))
        WHEN N'' THEN N'Queued'
        WHEN N'0' THEN N'Queued'
        WHEN N'1' THEN N'Processing'
        WHEN N'2' THEN N'Indexing'
        WHEN N'3' THEN N'Completed'
        WHEN N'4' THEN N'Failed'
        WHEN N'5' THEN N'Cancelled'
        WHEN N'6' THEN N'PartiallyCompleted'
        WHEN N'queued' THEN N'Queued'
        WHEN N'processing' THEN N'Processing'
        WHEN N'running' THEN N'Processing'
        WHEN N'inprogress' THEN N'Processing'
        WHEN N'indexing' THEN N'Indexing'
        WHEN N'completed' THEN N'Completed'
        WHEN N'complete' THEN N'Completed'
        WHEN N'failed' THEN N'Failed'
        WHEN N'error' THEN N'Failed'
        WHEN N'cancelled' THEN N'Cancelled'
        WHEN N'canceled' THEN N'Cancelled'
        WHEN N'partiallycompleted' THEN N'PartiallyCompleted'
        WHEN N'partially-completed' THEN N'PartiallyCompleted'
        ELSE [Status]
    END
    WHERE [Status] IS NOT NULL;

    UPDATE [dbo].[IngestionJobs]
    SET [InputType] = CASE LOWER(LTRIM(RTRIM(COALESCE([InputType], [JobType], N''))))
        WHEN N'' THEN N'StructuredSpecification'
        WHEN N'0' THEN N'StructuredSpecification'
        WHEN N'1' THEN N'BikeGraph'
        WHEN N'2' THEN N'PDFManual'
        WHEN N'3' THEN N'WebContent'
        WHEN N'4' THEN N'Batch'
        WHEN N'5' THEN N'Scheduled'
        WHEN N'pdfmanual' THEN N'PDFManual'
        WHEN N'manual-pdf' THEN N'PDFManual'
        WHEN N'pdf' THEN N'PDFManual'
        WHEN N'bikegraph' THEN N'BikeGraph'
        WHEN N'bike-graph' THEN N'BikeGraph'
        WHEN N'structuredspecification' THEN N'StructuredSpecification'
        WHEN N'structured-specification' THEN N'StructuredSpecification'
        WHEN N'spec-dataset' THEN N'StructuredSpecification'
        WHEN N'csv' THEN N'StructuredSpecification'
        WHEN N'webcontent' THEN N'WebContent'
        WHEN N'batch' THEN N'Batch'
        WHEN N'scheduled' THEN N'Scheduled'
        ELSE N'StructuredSpecification'
    END
    WHERE [InputType] IS NULL
       OR LTRIM(RTRIM([InputType])) = N'';

    UPDATE [dbo].[IngestionJobs]
    SET [InputRef] = COALESCE(
            NULLIF([InputRef], N''),
            NULLIF([SourceFilePath], N''),
            CONVERT(NVARCHAR(36), [IngestionJobId]))
    WHERE [InputRef] IS NULL
       OR LTRIM(RTRIM([InputRef])) = N'';

    UPDATE [dbo].[IngestionJobs]
    SET [ComputeProvider] = N'LegacyPipeline'
    WHERE [ComputeProvider] IS NULL
       OR LTRIM(RTRIM([ComputeProvider])) = N'';
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IngestionJobs')
BEGIN
    -- Drop filtered version if it already exists from a prior deployment.
    -- Filtered indexes cannot be referenced by foreign keys, so a filtered
    -- IX_IngestionJobs_IngestionJobId will block creation of
    -- FK_IndexedArtifacts_IngestionJobs below.
    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'IX_IngestionJobs_IngestionJobId'
          AND has_filter = 1)
    BEGIN
        DROP INDEX [IX_IngestionJobs_IngestionJobId] ON [dbo].[IngestionJobs];
    END

    -- Create the non-filtered unique index required by foreign key constraints.
    -- The backfill above (lines 559-659) guarantees every IngestionJobId is
    -- non-NULL, so a non-filtered unique index will succeed.
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'IX_IngestionJobs_IngestionJobId')
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX [IX_IngestionJobs_IngestionJobId]
            ON [dbo].[IngestionJobs] ([IngestionJobId]);
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'IX_IngestionJobs_InputType')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_IngestionJobs_InputType]
            ON [dbo].[IngestionJobs] ([InputType]);
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'IX_IngestionJobs_InputRef_InputType')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_IngestionJobs_InputRef_InputType]
            ON [dbo].[IngestionJobs] ([InputRef], [InputType]);
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.IngestionJobs')
          AND name = N'IX_IngestionJobs_CreatedAtUtc')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_IngestionJobs_CreatedAtUtc]
            ON [dbo].[IngestionJobs] ([CreatedAtUtc]);
    END
END
GO

-- Create stored procedure for getting ingestion jobs by status
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetIngestionJobsByStatus')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetIngestionJobsByStatus]
        @Status NVARCHAR(50),
        @Limit INT = 100
    AS
    BEGIN
        SELECT TOP (@Limit) *
        FROM [dbo].[IngestionJobs]
        WHERE [Status] = @Status
        ORDER BY [StartTime] DESC
    END
    ')
END
GO

-- Create stored procedure for getting ingestion jobs by job type
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetIngestionJobsByJobType')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetIngestionJobsByJobType]
        @JobType NVARCHAR(50),
        @Limit INT = 100
    AS
    BEGIN
        SELECT TOP (@Limit) *
        FROM [dbo].[IngestionJobs]
        WHERE [JobType] = @JobType
        ORDER BY [StartTime] DESC
    END
    ')
END
GO

-- Create stored procedure for getting recent ingestion jobs
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetRecentIngestionJobs')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetRecentIngestionJobs]
        @Limit INT = 50
    AS
    BEGIN
        SELECT TOP (@Limit) *
        FROM [dbo].[IngestionJobs]
        ORDER BY [StartTime] DESC
    END
    ')
END
GO

-- Create stored procedure for getting ingestion jobs by user
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetIngestionJobsByUser')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetIngestionJobsByUser]
        @UserId NVARCHAR(128),
        @Limit INT = 100
    AS
    BEGIN
        SELECT TOP (@Limit) *
        FROM [dbo].[IngestionJobs]
        WHERE [UserId] = @UserId
        ORDER BY [StartTime] DESC
    END
    ')
END
GO

-- Create stored procedure for getting ingestion jobs by date range
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetIngestionJobsByDateRange')
BEGIN
    EXEC ('
    CREATE PROCEDURE [dbo].[sp_GetIngestionJobsByDateRange]
        @StartDate DATETIME2,
        @EndDate DATETIME2
    AS
    BEGIN
        SELECT *
        FROM [dbo].[IngestionJobs]
        WHERE [StartTime] BETWEEN @StartDate AND @EndDate
        ORDER BY [StartTime] DESC
    END
    ')
END
GO

-- Create IndexedArtifacts table (per-blob/artifact catalog)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IndexedArtifacts')
BEGIN
    CREATE TABLE [dbo].[IndexedArtifacts] (
        [IndexedArtifactId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [IngestionJobId] UNIQUEIDENTIFIER NOT NULL,
        [UploadId] NVARCHAR(64) NOT NULL,
        [ArtifactType] NVARCHAR(50) NOT NULL,
        [BlobContainer] NVARCHAR(256) NOT NULL,
        [BlobPath] NVARCHAR(1024) NOT NULL,
        [SourceFileName] NVARCHAR(512) NULL,
        [State] NVARCHAR(50) NOT NULL,
        [ExpectedChunkCount] INT NULL,
        [IndexedChunkCount] INT NULL,
        [FailedChunkCount] INT NULL,
        [LastProcessedAtUtc] DATETIME2(7) NULL,
        [FailureReason] NVARCHAR(2000) NULL,
        [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] DATETIME2(7) NULL,
        CONSTRAINT [FK_IndexedArtifacts_IngestionJobs] FOREIGN KEY ([IngestionJobId]) REFERENCES [dbo].[IngestionJobs]([IngestionJobId])
    );

    CREATE UNIQUE INDEX [UQ_IndexedArtifacts_Upload_Type] ON [dbo].[IndexedArtifacts]([UploadId], [ArtifactType]);
    CREATE INDEX [IX_IndexedArtifacts_IngestionJobId] ON [dbo].[IndexedArtifacts]([IngestionJobId]);
    CREATE INDEX [IX_IndexedArtifacts_State] ON [dbo].[IndexedArtifacts]([State]);
END
GO

-- Create IndexedChunks table (per-chunk tracking)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IndexedChunks')
BEGIN
    CREATE TABLE [dbo].[IndexedChunks] (
        [ChunkId] NVARCHAR(128) NOT NULL PRIMARY KEY,
        [IndexedArtifactId] UNIQUEIDENTIFIER NOT NULL,
        [IngestionJobId] UNIQUEIDENTIFIER NOT NULL,
        [UploadId] NVARCHAR(64) NOT NULL,
        [SourceFileName] NVARCHAR(512) NULL,
        [PageNumber] INT NULL,
        [ChunkIndex] INT NULL,
        [Stage] NVARCHAR(100) NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [ProcessedAtUtc] DATETIME2(7) NULL,
        [FailureReason] NVARCHAR(1000) NULL,
        CONSTRAINT [FK_IndexedChunks_IndexedArtifacts] FOREIGN KEY ([IndexedArtifactId]) REFERENCES [dbo].[IndexedArtifacts]([IndexedArtifactId])
    );

    CREATE INDEX [IX_IndexedChunks_IndexedArtifactId] ON [dbo].[IndexedChunks]([IndexedArtifactId]);
    CREATE INDEX [IX_IndexedChunks_IngestionJobId] ON [dbo].[IndexedChunks]([IngestionJobId]);
    CREATE INDEX [IX_IndexedChunks_Status] ON [dbo].[IndexedChunks]([Status]);
    CREATE INDEX [IX_IndexedChunks_UploadId] ON [dbo].[IndexedChunks]([UploadId]);
END
GO

-- Create WebTrustPolicies table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WebTrustPolicies')
BEGIN
    CREATE TABLE [dbo].[WebTrustPolicies] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [DomainPattern] NVARCHAR(255) NOT NULL,
        [Tier] INT NOT NULL,
        [IsBlocked] BIT NOT NULL DEFAULT 0,
        [Reason] NVARCHAR(1000) NULL,
        [AllowSubdomains] BIT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [UQ_WebTrustPolicies_DomainPattern] UNIQUE ([DomainPattern])
    );

    CREATE INDEX [IX_WebTrustPolicies_Tier] ON [dbo].[WebTrustPolicies]([Tier]);
    CREATE INDEX [IX_WebTrustPolicies_IsBlocked] ON [dbo].[WebTrustPolicies]([IsBlocked]);
END
GO

-- Create ToolConfigurations table for MCP tool configuration management
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ToolConfigurations')
BEGIN
    CREATE TABLE [dbo].[ToolConfigurations] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [ToolId] NVARCHAR(255) NOT NULL,
        [Name] NVARCHAR(255) NOT NULL,
        [Description] NVARCHAR(1000) NULL,
        [ServerUrl] NVARCHAR(500) NOT NULL,
        [ToolType] NVARCHAR(100) NOT NULL,
        [Version] NVARCHAR(50) NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [IsSystemTool] BIT NOT NULL DEFAULT 0,
        [Priority] INT NOT NULL DEFAULT 0,
        [TimeoutMs] INT NULL,
        [RetryOnFailure] BIT NOT NULL DEFAULT 0,
        [MaxRetries] INT NOT NULL DEFAULT 0,
        [DisabledReason] NVARCHAR(500) NULL,
        [LastConnectionStatus] NVARCHAR(50) NULL,
        [LastTestedAt] DATETIME2 NULL,
        [ConfigurationJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [UQ_ToolConfigurations_ToolId] UNIQUE ([ToolId])
    );

    CREATE INDEX [IX_ToolConfigurations_ToolId] ON [dbo].[ToolConfigurations]([ToolId]);
    CREATE INDEX [IX_ToolConfigurations_IsEnabled] ON [dbo].[ToolConfigurations]([IsEnabled]);
    CREATE INDEX [IX_ToolConfigurations_ToolType] ON [dbo].[ToolConfigurations]([ToolType]);
    CREATE INDEX [IX_ToolConfigurations_Priority] ON [dbo].[ToolConfigurations]([Priority]);
    CREATE INDEX [IX_ToolConfigurations_CreatedAt] ON [dbo].[ToolConfigurations]([CreatedAt]);
END
GO

-- Create ToolConfigurationAuditLog table for tracking changes to tool configurations
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ToolConfigurationAuditLog')
BEGIN
    CREATE TABLE [dbo].[ToolConfigurationAuditLog] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ToolConfigurationId] UNIQUEIDENTIFIER NOT NULL,
        [ToolId] NVARCHAR(255) NOT NULL,
        [Action] NVARCHAR(50) NOT NULL,
        [BeforeJson] NVARCHAR(MAX) NULL,
        [AfterJson] NVARCHAR(MAX) NULL,
        [UserId] NVARCHAR(256) NULL,
        [ChangeReason] NVARCHAR(500) NULL,
        [IpAddress] NVARCHAR(50) NULL,
        [ChangedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [FK_ToolConfigurationAuditLog_ToolConfigurations] FOREIGN KEY ([ToolConfigurationId]) REFERENCES [dbo].[ToolConfigurations]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_ToolConfigurationAuditLog_ToolConfigurationId] ON [dbo].[ToolConfigurationAuditLog]([ToolConfigurationId]);
    CREATE INDEX [IX_ToolConfigurationAuditLog_ToolId] ON [dbo].[ToolConfigurationAuditLog]([ToolId]);
    CREATE INDEX [IX_ToolConfigurationAuditLog_Action] ON [dbo].[ToolConfigurationAuditLog]([Action]);
    CREATE INDEX [IX_ToolConfigurationAuditLog_ChangedAt] ON [dbo].[ToolConfigurationAuditLog]([ChangedAt]);
    CREATE INDEX [IX_ToolConfigurationAuditLog_UserId] ON [dbo].[ToolConfigurationAuditLog]([UserId]);
END
GO

-- =============================================================================
-- BikeModels table
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

    CREATE UNIQUE NONCLUSTERED INDEX [UQ_BikeModels_Make_Model_Year]
        ON [dbo].[BikeModels] ([Make], [Model], [Year]);

    CREATE NONCLUSTERED INDEX [IX_BikeModels_Make_Model_Year]
        ON [dbo].[BikeModels] ([Make], [Model], [Year]);
END;
GO

-- =============================================================================
-- Graph RAG tables (SQL Server 2017+ Graph feature required)
-- =============================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = N'GraphNode' AND is_node = 1
)
BEGIN
    CREATE TABLE [dbo].[GraphNode] (
        [Id]               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID(),
        [Name]             NVARCHAR(512)    NOT NULL,
        [Type]             NVARCHAR(128)    NOT NULL,
        [Description]      NVARCHAR(MAX)        NULL,
        [SourceDocumentId] UNIQUEIDENTIFIER     NULL,
        [CreatedAtUtc]     DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc]     DATETIME2(7)         NULL
    ) AS NODE;

    ALTER TABLE [dbo].[GraphNode]
        ADD CONSTRAINT [PK_GraphNode] PRIMARY KEY CLUSTERED ([Id]);

    CREATE NONCLUSTERED INDEX [IX_GraphNode_SourceDocumentId]
        ON [dbo].[GraphNode] ([SourceDocumentId])
        WHERE [SourceDocumentId] IS NOT NULL;

    CREATE NONCLUSTERED INDEX [IX_GraphNode_Type]
        ON [dbo].[GraphNode] ([Type]);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = N'GraphEdge' AND is_edge = 1
)
BEGIN
    CREATE TABLE [dbo].[GraphEdge] (
        [FromNodeId]       UNIQUEIDENTIFIER NOT NULL,
        [ToNodeId]         UNIQUEIDENTIFIER NOT NULL,
        [RelationshipType] NVARCHAR(256)    NOT NULL,
        [Weight]           FLOAT            NOT NULL DEFAULT 1.0,
        [Context]          NVARCHAR(MAX)        NULL,
        [CreatedAtUtc]     DATETIME2(7)     NOT NULL DEFAULT SYSUTCDATETIME()
    ) AS EDGE;

    CREATE NONCLUSTERED INDEX [IX_GraphEdge_RelationshipType]
        ON [dbo].[GraphEdge] ([RelationshipType]);

    CREATE NONCLUSTERED INDEX [IX_GraphEdge_FromTo_RelationshipType]
        ON [dbo].[GraphEdge] ([FromNodeId], [ToNodeId], [RelationshipType]);

    -- Dedicated single-column indexes to support the JOIN-based ingestion-job
    -- delete query on Azure SQL Basic tier (T12: ingestion job delete timeout fix).
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.GraphEdge')
          AND name = N'IX_GraphEdge_FromNodeId')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_GraphEdge_FromNodeId]
            ON [dbo].[GraphEdge] ([FromNodeId]);
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.GraphEdge')
          AND name = N'IX_GraphEdge_ToNodeId')
    BEGIN
        CREATE NONCLUSTERED INDEX [IX_GraphEdge_ToNodeId]
            ON [dbo].[GraphEdge] ([ToNodeId]);
    END
END;
GO

-- Vector-graph anchor columns on GraphNode (plan
-- 2026-08-01-vector-graph-anchor-id-contract.md, decision D4). Runs
-- unconditionally (outside the CREATE TABLE guard above) so it also upgrades
-- already-deployed databases where dbo.GraphNode already exists.
IF COL_LENGTH('dbo.GraphNode', 'ChunkId') IS NULL
    ALTER TABLE [dbo].[GraphNode] ADD [ChunkId] NVARCHAR(128) NULL;

IF COL_LENGTH('dbo.GraphNode', 'SourceContentHash') IS NULL
    ALTER TABLE [dbo].[GraphNode] ADD [SourceContentHash] NVARCHAR(128) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_GraphNode_ChunkId' AND object_id = OBJECT_ID(N'dbo.GraphNode')
)
    CREATE NONCLUSTERED INDEX [IX_GraphNode_ChunkId]
        ON [dbo].[GraphNode] ([ChunkId])
        WHERE [ChunkId] IS NOT NULL;
GO

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

PRINT 'Motorcycle RAG System database schema created successfully!';
