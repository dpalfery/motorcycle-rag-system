-- Create a test user for davidpalfery@gmail.com
-- This user will be approved for testing

DECLARE @UserId UNIQUEIDENTIFIER = NEWID();
DECLARE @ExternalId NVARCHAR(256) = 'davidpalfery@gmail.com';
DECLARE @Email NVARCHAR(256) = 'davidpalfery@gmail.com';
DECLARE @PlanId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001'; -- Default/Free plan

-- Check if user already exists
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @Email)
BEGIN
    INSERT INTO dbo.Users (Id, Email, ExternalId, PlanId, IsApproved, CreatedDate, UpdatedDate)
    VALUES (@UserId, @Email, @ExternalId, @PlanId, 1, GETUTCDATE(), GETUTCDATE());
    
    PRINT 'User created successfully with ID: ' + CAST(@UserId AS NVARCHAR(36));
END
ELSE
BEGIN
    -- Update existing user to be approved
    UPDATE dbo.Users 
    SET IsApproved = 1, UpdatedDate = GETUTCDATE()
    WHERE Email = @Email;
    
    PRINT 'User updated to approved status';
    
    SELECT Id, Email, ExternalId, IsApproved FROM dbo.Users WHERE Email = @Email;
END
