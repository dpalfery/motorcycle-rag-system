-- Create a test user for davidpalfery@gmail.com
-- This user will be approved for testing

DECLARE @UserId NVARCHAR(36) = CAST(NEWID() AS NVARCHAR(36));
DECLARE @Email NVARCHAR(256) = 'davidpalfery@gmail.com';
DECLARE @PlanId NVARCHAR(36) = '00000000-0000-0000-0000-000000000001'; -- Default/Free plan

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @Email)
BEGIN
    INSERT INTO dbo.Users (Id, Email, DisplayName, FirstName, LastName, IsEnabled, CreatedDate, LastUpdatedDate, PlanId, TierLabel, AccessState, AuthProvider, ProviderUserId)
    VALUES (@UserId, @Email, 'David Palfery', 'David', 'Palfery', 1, SYSUTCDATETIME(), SYSUTCDATETIME(), @PlanId, 'Standard', 'Active', 'Microsoft', @Email);
    
    INSERT INTO dbo.UserIdentities (ManagedUserId, Provider, ProviderEmail, InvitationStatus, InvitationCreatedAtUtc, LastSyncedAtUtc)
    VALUES (@UserId, 'Microsoft', @Email, 'Provisioned', SYSUTCDATETIME(), SYSUTCDATETIME());

    PRINT 'User created successfully with ID: ' + CAST(@UserId AS NVARCHAR(36));
END
ELSE
BEGIN
    SELECT @UserId = Id FROM dbo.Users WHERE Email = @Email;

    -- Update existing user to be approved
    UPDATE dbo.Users 
    SET IsEnabled = 1, AccessState = 'Active', LastUpdatedDate = SYSUTCDATETIME()
    WHERE Id = @UserId;
    
    IF NOT EXISTS (SELECT 1 FROM dbo.UserIdentities WHERE ManagedUserId = @UserId)
    BEGIN
        INSERT INTO dbo.UserIdentities (ManagedUserId, Provider, ProviderEmail, InvitationStatus, InvitationCreatedAtUtc, LastSyncedAtUtc)
        VALUES (@UserId, 'Microsoft', @Email, 'Provisioned', SYSUTCDATETIME(), SYSUTCDATETIME());
    END
    ELSE
    BEGIN
        UPDATE dbo.UserIdentities
        SET AccessRevokedAtUtc = NULL, LastSyncedAtUtc = SYSUTCDATETIME()
        WHERE ManagedUserId = @UserId;
    END
    
    PRINT 'User updated to approved status';
    
    SELECT Id, Email, AccessState, IsEnabled FROM dbo.Users WHERE Email = @Email;
END
