-- Motorcycle RAG System - SQL Schema
-- This script creates the core database tables for the Motorcycle RAG System

USE [MotorcycleRAG];
GO

-- Check if database exists, create if not
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'MotorcycleRAG')
BEGIN
    CREATE DATABASE [MotorcycleRAG];
END
GO

-- Use the database
USE [MotorcycleRAG];
GO

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
        [AuthProvider] NVARCHAR(50) NULL,
        [ProviderUserId] NVARCHAR(128) NULL,
        CONSTRAINT [UQ_Users_Email] UNIQUE ([Email]),
        CONSTRAINT [UQ_Users_ProviderUserId] UNIQUE ([ProviderUserId])
    );
    
    CREATE INDEX [IX_Users_Email] ON [dbo].[Users]([Email]);
    CREATE INDEX [IX_Users_IsEnabled] ON [dbo].[Users]([IsEnabled]);
    CREATE INDEX [IX_Users_PlanId] ON [dbo].[Users]([PlanId]);
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
        [JobId] NVARCHAR(128) NOT NULL,
        [JobType] NVARCHAR(50) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [SourceFilePath] NVARCHAR(500) NOT NULL,
        [SourceFileName] NVARCHAR(500) NULL,
        [StartTime] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [EndTime] DATETIME2 NULL,
        [UserId] NVARCHAR(128) NULL,
        [UserEmail] NVARCHAR(256) NULL,
        [TotalRecordsProcessed] INT NOT NULL DEFAULT 0,
        [RecordsIndexed] INT NOT NULL DEFAULT 0,
        [RecordsFailed] INT NOT NULL DEFAULT 0,
        [RecordsWithWarnings] INT NOT NULL DEFAULT 0,
        [MetricsJson] NVARCHAR(MAX) NULL,
        [ErrorsJson] NVARCHAR(MAX) NULL,
        [ErrorMessage] NVARCHAR(2000) NULL,
        [MetadataJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [UQ_IngestionJobs_JobId] UNIQUE ([JobId])
    );
    
    CREATE INDEX [IX_IngestionJobs_JobId] ON [dbo].[IngestionJobs]([JobId]);
    CREATE INDEX [IX_IngestionJobs_Status] ON [dbo].[IngestionJobs]([Status]);
    CREATE INDEX [IX_IngestionJobs_JobType] ON [dbo].[IngestionJobs]([JobType]);
    CREATE INDEX [IX_IngestionJobs_StartTime] ON [dbo].[IngestionJobs]([StartTime]);
    CREATE INDEX [IX_IngestionJobs_UserId] ON [dbo].[IngestionJobs]([UserId]);
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

PRINT 'Motorcycle RAG System database schema created successfully!';