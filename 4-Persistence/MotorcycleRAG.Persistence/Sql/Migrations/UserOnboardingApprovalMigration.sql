SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- =============================================================================
-- Migration: User onboarding approval schema
-- Target: SQL Server 2017+
-- Run: Execute once against the target database. Idempotent.
-- =============================================================================

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Users')
BEGIN
    IF COL_LENGTH('dbo.Users', 'TierLabel') IS NULL
        ALTER TABLE [dbo].[Users] ADD [TierLabel] NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.Users', 'AccessState') IS NULL
        ALTER TABLE [dbo].[Users] ADD [AccessState] NVARCHAR(50) NOT NULL CONSTRAINT [DF_Users_AccessState_Onboarding] DEFAULT N'None';

    IF COL_LENGTH('dbo.Users', 'CancelledAtUtc') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelledAtUtc] DATETIME2(7) NULL;

    IF COL_LENGTH('dbo.Users', 'CancelledByUserId') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelledByUserId] NVARCHAR(128) NULL;

    IF COL_LENGTH('dbo.Users', 'CancelReason') IS NULL
        ALTER TABLE [dbo].[Users] ADD [CancelReason] NVARCHAR(500) NULL;

    IF COL_LENGTH('dbo.Users', 'RowVersion') IS NULL
        ALTER TABLE [dbo].[Users] ADD [RowVersion] ROWVERSION;

    IF EXISTS (
        SELECT 1
        FROM sys.key_constraints
        WHERE [name] = N'UQ_Users_ProviderUserId'
          AND [parent_object_id] = OBJECT_ID(N'dbo.Users')
    )
        ALTER TABLE [dbo].[Users] DROP CONSTRAINT [UQ_Users_ProviderUserId];

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_AccessState' AND object_id = OBJECT_ID(N'dbo.Users'))
        CREATE INDEX [IX_Users_AccessState] ON [dbo].[Users]([AccessState]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_TierLabel' AND object_id = OBJECT_ID(N'dbo.Users'))
        CREATE INDEX [IX_Users_TierLabel] ON [dbo].[Users]([TierLabel]);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_ProviderUserId_NotNull' AND object_id = OBJECT_ID(N'dbo.Users'))
        CREATE UNIQUE INDEX [UX_Users_ProviderUserId_NotNull] ON [dbo].[Users]([ProviderUserId]) WHERE [ProviderUserId] IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AccessRequests')
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
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'UserIdentities')
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
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'OnboardingAttempts')
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
END;
GO