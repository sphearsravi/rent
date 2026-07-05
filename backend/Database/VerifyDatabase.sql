-- Database Verification Script
-- Run this to check if all tables and columns exist

USE RentCollectionDB;
GO

PRINT '=================================================================';
PRINT 'DATABASE VERIFICATION SCRIPT';
PRINT 'Checking for all required tables and columns...';
PRINT '=================================================================';
PRINT '';

-- Check if all required tables exist
PRINT '1. CHECKING TABLES...';
PRINT '-----------------------------------------------------------------';

DECLARE @MissingTables TABLE (TableName NVARCHAR(100));

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Companies')
    INSERT INTO @MissingTables VALUES ('Companies');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AspNetUsers')
    INSERT INTO @MissingTables VALUES ('AspNetUsers');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Properties')
    INSERT INTO @MissingTables VALUES ('Properties');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Units')
    INSERT INTO @MissingTables VALUES ('Units');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tenants')
    INSERT INTO @MissingTables VALUES ('Tenants');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Leases')
    INSERT INTO @MissingTables VALUES ('Leases');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Payments')
    INSERT INTO @MissingTables VALUES ('Payments');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Invoices')
    INSERT INTO @MissingTables VALUES ('Invoices');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MaintenanceRequests')
    INSERT INTO @MissingTables VALUES ('MaintenanceRequests');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents')
    INSERT INTO @MissingTables VALUES ('Documents');

IF EXISTS (SELECT * FROM @MissingTables)
BEGIN
    PRINT 'ERROR: Missing tables detected!';
    SELECT TableName AS 'Missing Table' FROM @MissingTables;
    PRINT '';
    PRINT 'ACTION REQUIRED: Run FixDatabaseSchema.sql';
END
ELSE
BEGIN
    PRINT 'SUCCESS: All required tables exist';
END

PRINT '';
PRINT '';

-- Check Documents table columns
PRINT '2. CHECKING DOCUMENTS TABLE COLUMNS...';
PRINT '-----------------------------------------------------------------';

DECLARE @DocumentsIssues TABLE (Issue NVARCHAR(255));

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Documents' AND COLUMN_NAME = 'DocumentId')
        INSERT INTO @DocumentsIssues VALUES ('Missing column: DocumentId');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Documents' AND COLUMN_NAME = 'DocumentName')
        INSERT INTO @DocumentsIssues VALUES ('Missing column: DocumentName');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Documents' AND COLUMN_NAME = 'FilePath')
        INSERT INTO @DocumentsIssues VALUES ('Missing column: FilePath');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Documents' AND COLUMN_NAME = 'RelatedEntityType')
        INSERT INTO @DocumentsIssues VALUES ('Missing column: RelatedEntityType');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Documents' AND COLUMN_NAME = 'UploadedBy')
        INSERT INTO @DocumentsIssues VALUES ('Missing column: UploadedBy');

    IF EXISTS (SELECT * FROM @DocumentsIssues)
    BEGIN
        PRINT 'ERROR: Documents table has issues!';
        SELECT Issue FROM @DocumentsIssues;
        PRINT '';
        PRINT 'ACTION REQUIRED: Run FixDatabaseSchema.sql';
    END
    ELSE
    BEGIN
        PRINT 'SUCCESS: Documents table structure is correct';
    END
END

PRINT '';
PRINT '';

-- Check AspNetUsers columns
PRINT '3. CHECKING ASPNETUSERS EXTENDED COLUMNS...';
PRINT '-----------------------------------------------------------------';

DECLARE @UserIssues TABLE (Issue NVARCHAR(255));

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'FirstName')
    INSERT INTO @UserIssues VALUES ('Missing column: FirstName');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'LastName')
    INSERT INTO @UserIssues VALUES ('Missing column: LastName');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'CompanyId')
    INSERT INTO @UserIssues VALUES ('Missing column: CompanyId');

IF EXISTS (SELECT * FROM @UserIssues)
BEGIN
    PRINT 'ERROR: AspNetUsers missing extended columns!';
    SELECT Issue FROM @UserIssues;
    PRINT '';
    PRINT 'ACTION REQUIRED: Run FixDatabaseSchema.sql';
END
ELSE
BEGIN
    PRINT 'SUCCESS: AspNetUsers has all extended columns';
END

PRINT '';
PRINT '';

-- Check PACI Number columns
PRINT '4. CHECKING PACI NUMBER COLUMNS...';
PRINT '-----------------------------------------------------------------';

DECLARE @PACIIssues TABLE (Issue NVARCHAR(255));

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Properties' AND COLUMN_NAME = 'PACINumber')
    INSERT INTO @PACIIssues VALUES ('Missing column: Properties.PACINumber');
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tenants' AND COLUMN_NAME = 'PACINumber')
    INSERT INTO @PACIIssues VALUES ('Missing column: Tenants.PACINumber');

IF EXISTS (SELECT * FROM @PACIIssues)
BEGIN
    PRINT 'WARNING: PACI Number columns missing!';
    SELECT Issue FROM @PACIIssues;
    PRINT '';
    PRINT 'ACTION REQUIRED: Run AddPACINumbers.sql';
END
ELSE
BEGIN
    PRINT 'SUCCESS: PACI Number columns exist';
END

PRINT '';
PRINT '';

-- Check MaintenanceRequests columns
PRINT '5. CHECKING MAINTENANCE REQUESTS COLUMNS...';
PRINT '-----------------------------------------------------------------';

DECLARE @MaintenanceIssues TABLE (Issue NVARCHAR(255));

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MaintenanceRequests')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceRequests' AND COLUMN_NAME = 'Category')
        INSERT INTO @MaintenanceIssues VALUES ('Missing column: Category');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceRequests' AND COLUMN_NAME = 'EstimatedCost')
        INSERT INTO @MaintenanceIssues VALUES ('Missing column: EstimatedCost');
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MaintenanceRequests' AND COLUMN_NAME = 'Notes')
        INSERT INTO @MaintenanceIssues VALUES ('Missing column: Notes');

    IF EXISTS (SELECT * FROM @MaintenanceIssues)
    BEGIN
        PRINT 'ERROR: MaintenanceRequests table has issues!';
        SELECT Issue FROM @MaintenanceIssues;
        PRINT '';
        PRINT 'ACTION REQUIRED: Run FixDatabaseSchema.sql';
    END
    ELSE
    BEGIN
        PRINT 'SUCCESS: MaintenanceRequests table structure is correct';
    END
END

PRINT '';
PRINT '';

-- Check data counts
PRINT '6. CHECKING DATA COUNTS...';
PRINT '-----------------------------------------------------------------';

SELECT
    'Companies' AS TableName,
    COUNT(*) AS RecordCount,
    CASE WHEN COUNT(*) = 0 THEN 'No data - Need sample data' ELSE 'Has data' END AS Status
FROM Companies
UNION ALL
SELECT 'Properties', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run AddKuwaitSampleData.sql' ELSE 'Has data' END FROM Properties
UNION ALL
SELECT 'Tenants', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run AddKuwaitSampleData.sql' ELSE 'Has data' END FROM Tenants
UNION ALL
SELECT 'Leases', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run AddKuwaitSampleData.sql' ELSE 'Has data' END FROM Leases
UNION ALL
SELECT 'Payments', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run AddKuwaitSampleData.sql' ELSE 'Has data' END FROM Payments
UNION ALL
SELECT 'Invoices', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run GenerateInitialInvoices.sql' ELSE 'Has data' END FROM Invoices
UNION ALL
SELECT 'MaintenanceRequests', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Run AddKuwaitSampleData.sql' ELSE 'Has data' END FROM MaintenanceRequests
UNION ALL
SELECT 'Documents', COUNT(*), CASE WHEN COUNT(*) = 0 THEN 'No data - Upload files via app' ELSE 'Has data' END FROM Documents;

PRINT '';
PRINT '';

-- Summary and recommendations
PRINT '=================================================================';
PRINT 'VERIFICATION COMPLETE';
PRINT '=================================================================';
PRINT '';

-- Count issues
DECLARE @TotalIssues INT;
SELECT @TotalIssues =
    (SELECT COUNT(*) FROM @MissingTables) +
    (SELECT COUNT(*) FROM @DocumentsIssues) +
    (SELECT COUNT(*) FROM @UserIssues) +
    (SELECT COUNT(*) FROM @MaintenanceIssues);

IF @TotalIssues = 0
BEGIN
    PRINT 'RESULT: Database schema is CORRECT';
    PRINT '';
    PRINT 'Your database is properly configured!';
    PRINT 'You can now run the application without schema errors.';
    PRINT '';
    PRINT 'If you see "No data" warnings above:';
    PRINT '  1. Run AddKuwaitSampleData.sql to add sample data';
    PRINT '  2. Run GenerateInitialInvoices.sql to create invoices';
END
ELSE
BEGIN
    PRINT 'RESULT: Database has ISSUES that need to be fixed';
    PRINT '';
    PRINT 'Total issues found: ' + CAST(@TotalIssues AS NVARCHAR(10));
    PRINT '';
    PRINT 'RECOMMENDED ACTIONS:';
    PRINT '  1. Run: FixDatabaseSchema.sql (CRITICAL)';
    PRINT '  2. Run: AddPACINumbers.sql (if PACI warnings above)';
    PRINT '  3. Run: AddKuwaitSampleData.sql (for sample data)';
    PRINT '  4. Run: GenerateInitialInvoices.sql (for invoices)';
    PRINT '';
    PRINT 'Command to fix schema:';
    PRINT 'sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"';
END

PRINT '';
PRINT '=================================================================';
GO
