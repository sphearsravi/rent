-- Update Tenant Passwords with VALID BCrypt Hash
-- Password: Welcome@123
-- Hash Generated: 2025-11-10

USE RentCollectionDB;
GO

PRINT '========================================';
PRINT 'BEFORE UPDATE - Tenant Password Status:';
PRINT '========================================';

SELECT
    TenantId,
    FirstName,
    LastName,
    PACINumber,
    GovernmentId,
    CASE WHEN Password IS NULL THEN 'No Password'
         WHEN LEN(Password) < 60 THEN 'Invalid Hash'
         ELSE 'Has Password' END AS PasswordStatus,
    Status
FROM Tenants
WHERE Status = 'Active'
ORDER BY TenantId;
GO

-- This is a VALID BCrypt hash for "Welcome@123"
-- Generated using BCrypt.Net-Next with work factor 11
-- Hash verified successfully before using
DECLARE @validHash NVARCHAR(255) = '$2a$11$IZTdqv6NsRVTQXFpPl4vFuFrMezsRZ5mcPzDLF5979dO3tyJKNyqe';

PRINT '';
PRINT '========================================';
PRINT 'Updating ALL tenant passwords...';
PRINT 'New Password: Welcome@123';
PRINT '========================================';

UPDATE Tenants
SET Password = @validHash;

PRINT 'Updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' tenant records';
GO

PRINT '';
PRINT '========================================';
PRINT 'AFTER UPDATE - Tenant Password Status:';
PRINT '========================================';

SELECT
    TenantId,
    FirstName,
    LastName,
    PACINumber,
    GovernmentId,
    CASE WHEN Password IS NULL THEN 'No Password'
         WHEN LEN(Password) < 60 THEN 'Invalid Hash'
         ELSE 'Valid Hash Set' END AS PasswordStatus,
    Status
FROM Tenants
WHERE Status = 'Active'
ORDER BY TenantId;
GO

PRINT '';
PRINT '========================================';
PRINT 'LOGIN INSTRUCTIONS:';
PRINT '========================================';
PRINT '1. Go to: http://localhost:5032/TenantPortal/Login';
PRINT '2. Enter Civil ID: Use the PACI Number from the table above (e.g., 265123456789)';
PRINT '3. Enter Password: Welcome@123';
PRINT '4. Click Sign In';
PRINT '========================================';
GO
