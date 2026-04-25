-- Motorcycle RAG System - Test Data
-- This script seeds the database with sample test data for development and testing

USE [MotorcycleRAG];
GO

-- Insert User Plans (Free, Plus, Pro)
-- Onboarding feature mapping is fixed to:
--   trial -> Free + DemoUser
--   road runner -> Pro + Roadrunner
--   admin -> Pro + mcr-api-admin
IF NOT EXISTS (SELECT 1
FROM [dbo].[UserPlans]
WHERE [Name] = 'Free')
BEGIN
    INSERT INTO [dbo].[UserPlans]
        ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES
        ('free-plan-001', 'Free', 'Free tier with basic features', 10, 0, SYSUTCDATETIME());
    PRINT 'Inserted Free plan';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[UserPlans]
WHERE [Name] = 'Plus')
BEGIN
    INSERT INTO [dbo].[UserPlans]
        ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES
        ('plus-plan-001', 'Plus', 'Plus tier for regular users', 100, 1, SYSUTCDATETIME());
    PRINT 'Inserted Plus plan';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[UserPlans]
WHERE [Name] = 'Pro')
BEGIN
    INSERT INTO [dbo].[UserPlans]
        ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES
        ('pro-plan-001', 'Pro', 'Professional tier with unlimited requests', 2147483647, 1, SYSUTCDATETIME());
    PRINT 'Inserted Pro plan';
END
GO

-- Insert Test Users
IF NOT EXISTS (SELECT 1
FROM [dbo].[Users]
WHERE [Email] = 'testuser.free@example.com')
BEGIN
    INSERT INTO [dbo].[Users]
        ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId], [TierLabel], [AccessState])
    VALUES
        ('user-free-001', 'testuser.free@example.com', 'Test User Free', 'Test', 'User Free', 1, SYSUTCDATETIME(), 'free-plan-001', 'EntraExternalID', 'ext-user-001', 'Trial', 'Active');
    PRINT 'Inserted Free test user';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[Users]
WHERE [Email] = 'testuser.plus@example.com')
BEGIN
    INSERT INTO [dbo].[Users]
        ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId], [TierLabel], [AccessState])
    VALUES
        ('user-plus-001', 'testuser.plus@example.com', 'Test User Plus', 'Test', 'User Plus', 1, SYSUTCDATETIME(), 'plus-plan-001', 'EntraExternalID', 'ext-user-002', NULL, 'Active');
    PRINT 'Inserted Plus test user';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[Users]
WHERE [Email] = 'testuser.pro@example.com')
BEGIN
    INSERT INTO [dbo].[Users]
        ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId], [TierLabel], [AccessState])
    VALUES
        ('user-pro-001', 'testuser.pro@example.com', 'Test User Pro', 'Test', 'User Pro', 1, SYSUTCDATETIME(), 'pro-plan-001', 'EntraExternalID', 'ext-user-003', 'RoadRunner', 'Active');
    PRINT 'Inserted Pro test user';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[Users]
WHERE [Email] = 'testuser.cancelled@example.com')
BEGIN
    INSERT INTO [dbo].[Users]
        ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId], [TierLabel], [AccessState], [CancelledAtUtc], [CancelReason])
    VALUES
        ('user-cancelled-001', 'testuser.cancelled@example.com', 'Cancelled Test User', 'Cancelled', 'User', 0, SYSUTCDATETIME(), 'free-plan-001', 'EntraExternalID', 'ext-user-cancelled-001', 'Trial', 'Cancelled', SYSUTCDATETIME(), 'Seeded cancellation scenario');
    PRINT 'Inserted Cancelled test user';
END
GO

IF OBJECT_ID(N'[dbo].[UserIdentities]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[UserIdentities] WHERE [ManagedUserId] = 'user-free-001' AND [Provider] = 'Microsoft')
    BEGIN
        INSERT INTO [dbo].[UserIdentities]
            ([ManagedUserId], [Provider], [ProviderEmail], [Issuer], [Subject], [ProviderUserId], [ExternalDirectoryObjectId], [InvitationStatus], [InvitationCreatedAtUtc], [LastSyncedAtUtc])
        VALUES
            ('user-free-001', 'Microsoft', 'testuser.free@example.com', 'https://login.microsoftonline.com/test/v2.0', 'seed-sub-free-001', 'ext-user-001', 'external-user-free-001', 'Redeemed', SYSUTCDATETIME(), SYSUTCDATETIME());
        PRINT 'Inserted active user identity link';
    END

    IF NOT EXISTS (SELECT 1 FROM [dbo].[UserIdentities] WHERE [ManagedUserId] = 'user-cancelled-001' AND [Provider] = 'Google')
    BEGIN
        INSERT INTO [dbo].[UserIdentities]
            ([ManagedUserId], [Provider], [ProviderEmail], [Issuer], [Subject], [ProviderUserId], [ExternalDirectoryObjectId], [InvitationStatus], [InvitationCreatedAtUtc], [AccessRevokedAtUtc], [LastSyncedAtUtc])
        VALUES
            ('user-cancelled-001', 'Google', 'testuser.cancelled@example.com', 'https://login.microsoftonline.com/test/v2.0', 'seed-sub-cancelled-001', 'ext-user-cancelled-001', 'external-user-cancelled-001', 'Redeemed', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
        PRINT 'Inserted cancelled user identity link';
    END
END
GO

IF OBJECT_ID(N'[dbo].[AccessRequests]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[AccessRequests] WHERE [RequestedProvider] = 'Microsoft' AND [RequestedEmail] = 'pending.rider@example.com')
    BEGIN
        INSERT INTO [dbo].[AccessRequests]
            ([RequestedEmail], [RequestedProvider], [RequestDecisionState], [OnboardingExecutionState], [RequestedAtUtc], [CorrelationId])
        VALUES
            ('pending.rider@example.com', 'Microsoft', 'Pending', 'NotStarted', SYSUTCDATETIME(), 'seed-pending-correlation');
        PRINT 'Inserted pending onboarding request';
    END

    IF NOT EXISTS (SELECT 1 FROM [dbo].[AccessRequests] WHERE [RequestedProvider] = 'Google' AND [RequestedEmail] = 'failed.rider@example.com')
    BEGIN
        INSERT INTO [dbo].[AccessRequests]
            ([RequestedEmail], [RequestedProvider], [RequestDecisionState], [OnboardingExecutionState], [RequestedAtUtc], [AssignedTier], [ApprovedAtUtc], [OnboardingAttemptCount], [LastFailureCode], [LastFailureMessage], [CorrelationId])
        VALUES
            ('failed.rider@example.com', 'Google', 'Approved', 'Failed', SYSUTCDATETIME(), 'Trial', SYSUTCDATETIME(), 1, 'ProvisionExternalIdentity', 'Seeded retry scenario', 'seed-failed-correlation');
        PRINT 'Inserted failed onboarding request';
    END
END
GO

-- Insert Web Sources (Trusted motorcycle sites)
IF NOT EXISTS (SELECT 1
FROM [dbo].[WebSources]
WHERE [Url] = 'https://www.cycleworld.com')
BEGIN
    INSERT INTO [dbo].[WebSources]
        ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES
        ('https://www.cycleworld.com', 'Cycle World', 'Leading motorcycle news and reviews', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted Cycle World web source';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[WebSources]
WHERE [Url] = 'https://www.motorcycle.com')
BEGIN
    INSERT INTO [dbo].[WebSources]
        ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES
        ('https://www.motorcycle.com', 'Motorcycle.com', 'Comprehensive motorcycle information', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted Motorcycle.com web source';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[WebSources]
WHERE [Url] = 'https://www.revzilla.com')
BEGIN
    INSERT INTO [dbo].[WebSources]
        ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES
        ('https://www.revzilla.com', 'RevZilla', 'Motorcycle gear and accessories reviews', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted RevZilla web source';
END
GO

-- Insert Sample Tool Configurations (MCP tools)
IF NOT EXISTS (SELECT 1
FROM [dbo].[ToolConfigurations]
WHERE [ToolId] = 'web-search-tool')
BEGIN
    INSERT INTO [dbo].[ToolConfigurations]
        ([ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version], [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure], [MaxRetries], [CreatedAt], [UpdatedAt])
    VALUES
        ('web-search-tool', 'Web Search Tool', 'Search the web for motorcycle information', 'https://api.search.example.com', 'WebSearch', '1.0', 1, 1, 10, 30000, 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME());
    PRINT 'Inserted Web Search tool configuration';
END
GO

IF NOT EXISTS (SELECT 1
FROM [dbo].[ToolConfigurations]
WHERE [ToolId] = 'vector-search-tool')
BEGIN
    INSERT INTO [dbo].[ToolConfigurations]
        ([ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version], [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure], [MaxRetries], [CreatedAt], [UpdatedAt])
    VALUES
        ('vector-search-tool', 'Vector Search Tool', 'Search indexed motorcycle specifications', 'https://api.vector.example.com', 'VectorSearch', '1.0', 1, 1, 5, 30000, 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME());
    PRINT 'Inserted Vector Search tool configuration';
END
GO

-- Insert Sample Audit Logs
INSERT INTO [dbo].[AuditLogs]
    ([UserId], [UserEmail], [Action], [EntityType], [EntityId], [NewValue], [ActionDate], [Status])
VALUES
    ('user-free-001', 'testuser.free@example.com', 'UserCreated', 'User', 'user-free-001', '{"plan":"free-plan-001"}', SYSUTCDATETIME(), 'Success'),
    ('user-plus-001', 'testuser.plus@example.com', 'UserCreated', 'User', 'user-plus-001', '{"plan":"plus-plan-001"}', SYSUTCDATETIME(), 'Success'),
    ('user-pro-001', 'testuser.pro@example.com', 'UserCreated', 'User', 'user-pro-001', '{"plan":"pro-plan-001"}', SYSUTCDATETIME(), 'Success');
GO

PRINT 'Test data seeding completed successfully!';
GO
