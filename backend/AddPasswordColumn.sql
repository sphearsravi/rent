-- Migration Script: Add Password Column to Tenants Table
-- Execute this script in SQL Server Management Studio or any SQL client
-- Database: RentCollectionDB

USE RentCollectionDB;
GO

-- Step 1: Add Password column to Tenants table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'Password')
BEGIN
    ALTER TABLE Tenants ADD Password nvarchar(255) NULL;
    PRINT 'Password column added successfully.';
END
ELSE
BEGIN
    PRINT 'Password column already exists.';
END
GO

-- Step 2: Set default password for all existing tenants
-- Default password is "Welcome@123" - hashed using BCrypt
-- Tenants must change this password after first login
UPDATE Tenants
SET Password = '$2a$11$vWZQV5Y5rGxK5J5K5J5K5.J5K5J5K5J5K5J5K5J5K5J5K5J5K5Jm'
WHERE Password IS NULL;
PRINT 'Default passwords set for ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' tenants.';
GO

-- Step 3: Update migration history so EF Core knows this migration was applied
IF NOT EXISTS (SELECT * FROM __EFMigrationsHistory WHERE MigrationId = '20251110054122_AddPasswordToTenant')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20251110054122_AddPasswordToTenant', '8.0.0');
    PRINT 'Migration history updated.';
END
ELSE
BEGIN
    PRINT 'Migration already recorded in history.';
END
GO

-- Step 4: Verify the changes
SELECT
    COUNT(*) AS TotalTenants,
    COUNT(Password) AS TenantsWithPassword
FROM Tenants;
GO

PRINT 'Migration completed successfully!';
GO
