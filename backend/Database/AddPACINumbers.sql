-- Add PACI Number columns for Kuwait requirements
USE RentCollectionDB;
GO

-- Add PACI Number to Properties table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Properties]') AND name = 'PACINumber')
BEGIN
    ALTER TABLE Properties
    ADD PACINumber NVARCHAR(50) NULL;
    PRINT 'Added PACINumber column to Properties table';
END
GO

-- Add PACI Number to Tenants table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND name = 'PACINumber')
BEGIN
    ALTER TABLE Tenants
    ADD PACINumber NVARCHAR(50) NULL;
    PRINT 'Added PACINumber column to Tenants table';
END
GO

PRINT 'Migration completed successfully!';
