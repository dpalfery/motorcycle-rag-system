-- Motorcycle RAG System - Test Data
-- This script seeds the database with sample test data for development and testing

USE [MotorcycleRAG];
GO

-- Insert User Plans (Free, Plus, Pro)
IF NOT EXISTS (SELECT 1 FROM [dbo].[UserPlans] WHERE [Name] = 'Free')
BEGIN
    INSERT INTO [dbo].[UserPlans] ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES ('free-plan-001', 'Free', 'Free tier with basic features', 10, 0, SYSUTCDATETIME());
    PRINT 'Inserted Free plan';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[UserPlans] WHERE [Name] = 'Plus')
BEGIN
    INSERT INTO [dbo].[UserPlans] ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES ('plus-plan-001', 'Plus', 'Plus tier for regular users', 100, 1, SYSUTCDATETIME());
    PRINT 'Inserted Plus plan';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[UserPlans] WHERE [Name] = 'Pro')
BEGIN
    INSERT INTO [dbo].[UserPlans] ([Id], [Name], [Description], [DailyRequestLimit], [IsPaid], [CreatedDate])
    VALUES ('pro-plan-001', 'Pro', 'Professional tier with unlimited requests', 2147483647, 1, SYSUTCDATETIME());
    PRINT 'Inserted Pro plan';
END
GO

-- Insert Test Users
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'testuser.free@example.com')
BEGIN
    INSERT INTO [dbo].[Users] ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId])
    VALUES ('user-free-001', 'testuser.free@example.com', 'Test User Free', 'Test', 'User Free', 1, SYSUTCDATETIME(), 'free-plan-001', 'EntraExternalID', 'ext-user-001');
    PRINT 'Inserted Free test user';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'testuser.plus@example.com')
BEGIN
    INSERT INTO [dbo].[Users] ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId])
    VALUES ('user-plus-001', 'testuser.plus@example.com', 'Test User Plus', 'Test', 'User Plus', 1, SYSUTCDATETIME(), 'plus-plan-001', 'EntraExternalID', 'ext-user-002');
    PRINT 'Inserted Plus test user';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'testuser.pro@example.com')
BEGIN
    INSERT INTO [dbo].[Users] ([Id], [Email], [DisplayName], [FirstName], [LastName], [IsEnabled], [CreatedDate], [PlanId], [AuthProvider], [ProviderUserId])
    VALUES ('user-pro-001', 'testuser.pro@example.com', 'Test User Pro', 'Test', 'User Pro', 1, SYSUTCDATETIME(), 'pro-plan-001', 'EntraExternalID', 'ext-user-003');
    PRINT 'Inserted Pro test user';
END
GO

-- Insert Web Sources (Trusted motorcycle sites)
IF NOT EXISTS (SELECT 1 FROM [dbo].[WebSources] WHERE [Url] = 'https://www.cycleworld.com')
BEGIN
    INSERT INTO [dbo].[WebSources] ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES ('https://www.cycleworld.com', 'Cycle World', 'Leading motorcycle news and reviews', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted Cycle World web source';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[WebSources] WHERE [Url] = 'https://www.motorcycle.com')
BEGIN
    INSERT INTO [dbo].[WebSources] ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES ('https://www.motorcycle.com', 'Motorcycle.com', 'Comprehensive motorcycle information', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted Motorcycle.com web source';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[WebSources] WHERE [Url] = 'https://www.revzilla.com')
BEGIN
    INSERT INTO [dbo].[WebSources] ([Url], [Name], [Description], [IsEnabled], [TrustTier], [CreatedDate], [CrawlFrequencyHours], [IncludeInSearch], [MaxCrawlDepth])
    VALUES ('https://www.revzilla.com', 'RevZilla', 'Motorcycle gear and accessories reviews', 1, 2, SYSUTCDATETIME(), 24, 1, 2);
    PRINT 'Inserted RevZilla web source';
END
GO

-- Insert Sample Tool Configurations (MCP tools)
IF NOT EXISTS (SELECT 1 FROM [dbo].[ToolConfigurations] WHERE [ToolId] = 'web-search-tool')
BEGIN
    INSERT INTO [dbo].[ToolConfigurations] ([ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version], [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure], [MaxRetries], [CreatedAt], [UpdatedAt])
    VALUES ('web-search-tool', 'Web Search Tool', 'Search the web for motorcycle information', 'https://api.search.example.com', 'WebSearch', '1.0', 1, 1, 10, 30000, 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME());
    PRINT 'Inserted Web Search tool configuration';
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[ToolConfigurations] WHERE [ToolId] = 'vector-search-tool')
BEGIN
    INSERT INTO [dbo].[ToolConfigurations] ([ToolId], [Name], [Description], [ServerUrl], [ToolType], [Version], [IsEnabled], [IsSystemTool], [Priority], [TimeoutMs], [RetryOnFailure], [MaxRetries], [CreatedAt], [UpdatedAt])
    VALUES ('vector-search-tool', 'Vector Search Tool', 'Search indexed motorcycle specifications', 'https://api.vector.example.com', 'VectorSearch', '1.0', 1, 1, 5, 30000, 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME());
    PRINT 'Inserted Vector Search tool configuration';
END
GO

-- Insert Sample Audit Logs
INSERT INTO [dbo].[AuditLogs] ([UserId], [UserEmail], [Action], [EntityType], [EntityId], [NewValue], [ActionDate], [Status])
VALUES 
    ('user-free-001', 'testuser.free@example.com', 'UserCreated', 'User', 'user-free-001', '{"plan":"free-plan-001"}', SYSUTCDATETIME(), 'Success'),
    ('user-plus-001', 'testuser.plus@example.com', 'UserCreated', 'User', 'user-plus-001', '{"plan":"plus-plan-001"}', SYSUTCDATETIME(), 'Success'),
    ('user-pro-001', 'testuser.pro@example.com', 'UserCreated', 'User', 'user-pro-001', '{"plan":"pro-plan-001"}', SYSUTCDATETIME(), 'Success');
GO

PRINT 'Test data seeding completed successfully!';
GO
