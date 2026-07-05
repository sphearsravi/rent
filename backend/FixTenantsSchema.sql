-- ========================================
-- Fix Tenants Table Schema
-- Make fields nullable to match imported data
-- ========================================

USE RentCollectionDB;
GO

PRINT 'Fixing Tenants table schema...';
PRINT '';

-- Make FirstName nullable
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'FirstName' AND is_nullable = 0)
BEGIN
    ALTER TABLE Tenants ALTER COLUMN FirstName NVARCHAR(100) NULL;
    PRINT '✓ Made FirstName nullable';
END

-- Make LastName nullable
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'LastName' AND is_nullable = 0)
BEGIN
    ALTER TABLE Tenants ALTER COLUMN LastName NVARCHAR(100) NULL;
    PRINT '✓ Made LastName nullable';
END

-- Make Email nullable
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'Email' AND is_nullable = 0)
BEGIN
    ALTER TABLE Tenants ALTER COLUMN Email NVARCHAR(200) NULL;
    PRINT '✓ Made Email nullable';
END

-- Make Phone nullable
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'Phone' AND is_nullable = 0)
BEGIN
    ALTER TABLE Tenants ALTER COLUMN Phone NVARCHAR(50) NULL;
    PRINT '✓ Made Phone nullable';
END

PRINT '';
PRINT 'Schema fix complete!';
GO
