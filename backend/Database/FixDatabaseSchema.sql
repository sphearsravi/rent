-- Fix Database Schema - Add missing tables and columns
USE RentCollectionDB;
GO

PRINT 'Starting database schema fixes...';
PRINT '';

-- 1. Add missing columns to AspNetUsers table
PRINT 'Checking AspNetUsers table...';

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'FirstName')
BEGIN
    ALTER TABLE AspNetUsers ADD FirstName NVARCHAR(100) NOT NULL DEFAULT '';
    PRINT '- Added FirstName column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'LastName')
BEGIN
    ALTER TABLE AspNetUsers ADD LastName NVARCHAR(100) NOT NULL DEFAULT '';
    PRINT '- Added LastName column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'CompanyId')
BEGIN
    ALTER TABLE AspNetUsers ADD CompanyId INT NULL;
    PRINT '- Added CompanyId column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'IsActive')
BEGIN
    ALTER TABLE AspNetUsers ADD IsActive BIT NOT NULL DEFAULT 1;
    PRINT '- Added IsActive column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'EmailVerified')
BEGIN
    ALTER TABLE AspNetUsers ADD EmailVerified BIT NOT NULL DEFAULT 0;
    PRINT '- Added EmailVerified column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'MFAEnabled')
BEGIN
    ALTER TABLE AspNetUsers ADD MFAEnabled BIT NOT NULL DEFAULT 0;
    PRINT '- Added MFAEnabled column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'LastLogin')
BEGIN
    ALTER TABLE AspNetUsers ADD LastLogin DATETIME2 NULL;
    PRINT '- Added LastLogin column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'CreatedDate')
BEGIN
    ALTER TABLE AspNetUsers ADD CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE();
    PRINT '- Added CreatedDate column';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = 'ModifiedDate')
BEGIN
    ALTER TABLE AspNetUsers ADD ModifiedDate DATETIME2 NULL;
    PRINT '- Added ModifiedDate column';
END

-- 2. Create Documents table if not exists
PRINT '';
PRINT 'Checking Documents table...';

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Documents')
BEGIN
    CREATE TABLE Documents (
        DocumentId INT IDENTITY(1,1) PRIMARY KEY,
        DocumentName NVARCHAR(255) NOT NULL,
        DocumentType NVARCHAR(100) NOT NULL,
        FilePath NVARCHAR(500) NOT NULL,
        FileSize BIGINT NOT NULL,
        RelatedEntityType NVARCHAR(50) NULL,
        RelatedEntityId INT NULL,
        Description NVARCHAR(MAX) NULL,
        UploadedBy NVARCHAR(450) NOT NULL,
        UploadedDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Documents_AspNetUsers FOREIGN KEY (UploadedBy) REFERENCES AspNetUsers(Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Documents_UploadedBy ON Documents(UploadedBy);
    CREATE INDEX IX_Documents_RelatedEntity ON Documents(RelatedEntityType, RelatedEntityId);

    PRINT '- Created Documents table';
END
ELSE
BEGIN
    PRINT '- Documents table already exists';
END

-- 3. Add foreign key constraint for AspNetUsers.CompanyId if not exists
PRINT '';
PRINT 'Checking foreign key constraints...';

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_AspNetUsers_Companies_CompanyId')
BEGIN
    ALTER TABLE AspNetUsers
    ADD CONSTRAINT FK_AspNetUsers_Companies_CompanyId
    FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId) ON DELETE SET NULL;
    PRINT '- Added FK_AspNetUsers_Companies_CompanyId';
END

-- 4. Verify MaintenanceRequests table structure
PRINT '';
PRINT 'Checking MaintenanceRequests table...';

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'MaintenanceRequests')
BEGIN
    -- Entity uses RequestId, keep it as RequestId
    PRINT '- MaintenanceRequests table exists';

    -- Add missing columns
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaintenanceRequests]') AND name = 'Category')
    BEGIN
        ALTER TABLE MaintenanceRequests ADD Category NVARCHAR(50) NOT NULL DEFAULT 'General';
        PRINT '- Added Category column';
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaintenanceRequests]') AND name = 'EstimatedCost')
    BEGIN
        ALTER TABLE MaintenanceRequests ADD EstimatedCost DECIMAL(18,3) NULL;
        PRINT '- Added EstimatedCost column';
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaintenanceRequests]') AND name = 'Notes')
    BEGIN
        ALTER TABLE MaintenanceRequests ADD Notes NVARCHAR(MAX) NULL;
        PRINT '- Added Notes column';
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaintenanceRequests]') AND name = 'CreatedDate')
    BEGIN
        ALTER TABLE MaintenanceRequests ADD CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE();
        PRINT '- Added CreatedDate column';
    END

    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MaintenanceRequests]') AND name = 'ModifiedDate')
    BEGIN
        ALTER TABLE MaintenanceRequests ADD ModifiedDate DATETIME2 NULL;
        PRINT '- Added ModifiedDate column';
    END
END

PRINT '';
PRINT 'Database schema fixes completed successfully!';
PRINT '';
PRINT 'Summary of changes:';
PRINT '- AspNetUsers: Added custom columns (FirstName, LastName, CompanyId, etc.)';
PRINT '- Documents: Created table with all required columns';
PRINT '- MaintenanceRequests: Fixed column names to match entity';
PRINT '';
GO
