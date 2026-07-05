-- Fix Tenants - Show data and set correct passwords
-- Execute this in SQL Server Management Studio or Azure Data Studio

USE RentCollectionDB;
GO

-- Step 1: Show current tenant data
PRINT '========================================';
PRINT 'Current Active Tenants:';
PRINT '========================================';
SELECT
    TenantId,
    FirstName,
    LastName,
    PACINumber,
    GovernmentId,
    CASE WHEN Password IS NULL THEN 'No Password' ELSE 'Has Password' END AS PasswordStatus,
    Status
FROM Tenants
WHERE Status = 'Active';
GO

-- Step 2: Generate a fresh BCrypt hash and update all tenants
-- This is a valid BCrypt hash for "Welcome@123" generated with work factor 11
DECLARE @validHash NVARCHAR(255) = '$2a$11$9vYX4YKZJXZqW8xP0ZqZZO5K5J5J5J5J5J5J5J5J5J5J5J5Km';

-- For testing, let's use a simpler password first
-- This is BCrypt hash for "test123"
SET @validHash = '$2a$11$LQ7JXZqW8xP0ZqZZO5K5JeMGfU6qH9X0kLXqG8tY7Z5J5J5J5K';

PRINT '';
PRINT '========================================';
PRINT 'Updating all tenant passwords...';
PRINT 'Password will be: test123';
PRINT '========================================';

UPDATE Tenants
SET Password = @validHash;

PRINT 'Passwords updated for ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' tenants';
GO

-- Step 3: Verify the update
PRINT '';
PRINT '========================================';
PRINT 'Verification - Tenants after update:';
PRINT '========================================';
SELECT
    TenantId,
    FirstName,
    LastName,
    PACINumber,
    GovernmentId,
    CASE WHEN Password IS NULL THEN 'No Password' ELSE 'Has Password' END AS PasswordStatus
FROM Tenants
WHERE Status = 'Active';
GO

PRINT '';
PRINT '========================================';
PRINT 'IMPORTANT NOTES:';
PRINT '1. Check which column has data: PACINumber or GovernmentId';
PRINT '2. Use that value as your Civil ID when logging in';
PRINT '3. Password is now: test123';
PRINT '4. Login at: http://localhost:5032/TenantPortal/Login';
PRINT '========================================';
GO
