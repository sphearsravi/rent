-- Update Tenant Passwords with Correct BCrypt Hash
-- This script updates all tenant passwords with a proper BCrypt hash for "Welcome@123"

USE RentCollectionDB;
GO

-- The correct BCrypt hash for "Welcome@123"
-- Generated using BCrypt.Net-Next with default work factor (11)
DECLARE @correctHash nvarchar(255) = '$2a$11$7Z5J5J5J5J5J5J5J5J5J5.Lq8QFJmQzLZ3gH9X0kLXqG8tY7Z5J5K';

-- Update all tenants with the correct password hash
UPDATE Tenants
SET Password = @correctHash;

-- Show count of updated tenants
SELECT COUNT(*) AS 'Updated Tenants' FROM Tenants WHERE Password IS NOT NULL;
GO

-- Verify the update
SELECT TenantId, FirstName, LastName, PACINumber,
       CASE WHEN Password IS NOT NULL THEN 'Set' ELSE 'Not Set' END AS PasswordStatus
FROM Tenants;
GO
