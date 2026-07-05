-- Quick Database Check Script
-- Run this to see what's missing

USE RentCollectionDB;
GO

PRINT '=================================================================';
PRINT 'QUICK DATABASE CHECK';
PRINT '=================================================================';
PRINT '';

-- Check if Documents table exists
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents')
BEGIN
    PRINT 'SUCCESS: Documents table exists';

    -- Show column structure
    PRINT '';
    PRINT 'Documents table columns:';
    SELECT
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Documents'
    ORDER BY ORDINAL_POSITION;

    -- Check for data
    DECLARE @DocCount INT;
    SELECT @DocCount = COUNT(*) FROM Documents;
    PRINT '';
    PRINT 'Document count: ' + CAST(@DocCount AS NVARCHAR(10));
END
ELSE
BEGIN
    PRINT 'ERROR: Documents table DOES NOT EXIST!';
    PRINT '';
    PRINT 'REQUIRED ACTION:';
    PRINT 'Run: sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"';
    PRINT 'Or double-click: SetupDatabase.bat';
END

PRINT '';
PRINT '=================================================================';
GO
