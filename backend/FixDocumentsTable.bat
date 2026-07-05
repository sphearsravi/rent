@echo off
REM Quick fix for Documents table issue

echo.
echo ====================================================================
echo FIX DOCUMENTS TABLE ERROR
echo ====================================================================
echo.
echo This will create the Documents table if it doesn't exist.
echo.
pause

echo.
echo Checking if Documents table exists...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -Q "USE RentCollectionDB; IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents') PRINT 'MISSING' ELSE PRINT 'EXISTS'" 2>nul | findstr /C:"MISSING" >nul

if %errorlevel% equ 0 (
    echo Documents table DOES NOT EXIST - creating it now...
    echo.
    echo Running FixDatabaseSchema.sql...
    echo.

    sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"

    if %errorlevel% equ 0 (
        echo.
        echo SUCCESS: Documents table created!
    ) else (
        echo.
        echo WARNING: There may have been issues. Check output above.
    )
) else (
    sqlcmd -S RAVIWST\SQLEXPRESS -E -Q "USE RentCollectionDB; IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents') PRINT 'Documents table EXISTS'" 2>nul | findstr /C:"EXISTS" >nul

    if %errorlevel% equ 0 (
        echo Documents table already EXISTS - no fix needed!
    ) else (
        echo ERROR: Cannot connect to SQL Server!
        echo.
        echo Please start SQL Server:
        echo   1. Press Windows + R
        echo   2. Type: services.msc
        echo   3. Find "SQL Server (SQLEXPRESS)" and start it
        echo.
        pause
        exit /b 1
    )
)

echo.
echo ====================================================================
echo FIX COMPLETE!
echo ====================================================================
echo.
echo Next steps:
echo   1. Restart your application (Ctrl+C then "dotnet run")
echo   2. Navigate to: http://localhost:5032/Documents
echo   3. Click "Upload Document"
echo   4. Try uploading a file
echo.
echo If you still see errors, please provide the EXACT error message.
echo.
pause
