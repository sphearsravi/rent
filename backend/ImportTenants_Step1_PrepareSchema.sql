-- ========================================
-- Rent Collection System
-- Tenant Import - Step 1: Prepare Database Schema
-- ========================================

USE RentCollectionDB;
GO

-- ========================================
-- STEP 1: Alter Tenants Table Schema
-- Make Phone field nullable to allow tenants without phone numbers
-- ========================================

PRINT 'Step 1: Making Phone field nullable in Tenants table...';

IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Tenants'
    AND COLUMN_NAME = 'Phone'
    AND IS_NULLABLE = 'NO'
)
BEGIN
    ALTER TABLE Tenants
    ALTER COLUMN Phone NVARCHAR(20) NULL;
    PRINT '  ✓ Phone column in Tenants table has been made nullable.';
END
ELSE
BEGIN
    PRINT '  ✓ Phone column is already nullable.';
END
GO

-- ========================================
-- STEP 2: Add Migration History Record
-- Mark this change in EF migrations history
-- ========================================

PRINT 'Step 2: Adding migration history record...';

IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20251105_MakeTenantPhoneOptional')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20251105_MakeTenantPhoneOptional', '9.0.0');
    PRINT '  ✓ Migration history record added.';
END
ELSE
BEGIN
    PRINT '  ✓ Migration already recorded.';
END
GO

-- ========================================
-- STEP 3: View Current Properties with PACI Numbers
-- This helps verify which properties are available
-- ========================================

PRINT '';
PRINT 'Current Properties in Database:';
PRINT '================================';

SELECT
    PropertyId,
    PropertyName,
    PACINumber,
    Governorate,
    Area,
    Block,
    BuildingNumber,
    Street
FROM Properties
ORDER BY PACINumber;
GO

-- ========================================
-- STEP 4: Check Current Tenant Count
-- ========================================

PRINT '';
PRINT 'Current Tenant Statistics:';
PRINT '==========================';

SELECT
    COUNT(*) AS TotalTenants,
    SUM(CASE WHEN Phone IS NULL OR Phone = '' THEN 1 ELSE 0 END) AS TenantsWithoutPhone,
    SUM(CASE WHEN PACINumber IS NOT NULL AND PACINumber != '' THEN 1 ELSE 0 END) AS TenantsWithPACI
FROM Tenants;
GO

-- ========================================
-- STEP 5: Get Default Company ID
-- ========================================

PRINT '';
PRINT 'Default Company for Tenants:';
PRINT '============================';

SELECT TOP 1
    CompanyId,
    CompanyName,
    '-- Use this CompanyId for tenant imports: ' + CAST(CompanyId AS VARCHAR) AS Note
FROM Companies
ORDER BY CompanyId;
GO

PRINT '';
PRINT '========================================';
PRINT 'Schema preparation complete!';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '1. Review the Properties list above';
PRINT '2. Note the CompanyId to use for tenant imports';
PRINT '3. Provide tenant INSERT data to import';
PRINT '4. Tenants will be matched to properties via PACI number';
PRINT '========================================';
GO
