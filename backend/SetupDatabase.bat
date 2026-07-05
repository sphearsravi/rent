@echo off
REM ====================================================================
REM Database Setup Script for Rent Collection System
REM This script will fix all database schema issues and add sample data
REM ====================================================================

echo.
echo ====================================================================
echo RENT COLLECTION SYSTEM - DATABASE SETUP
echo ====================================================================
echo.
echo This script will:
echo   1. Fix database schema (create Documents table, add columns)
echo   2. Add PACI numbers to Properties and Tenants
echo   3. Add Kuwait sample data (properties, tenants, leases)
echo   4. Generate invoices for current and previous month
echo.
echo Press Ctrl+C to cancel or
pause

echo.
echo ====================================================================
echo Step 1: Verifying Database Status
echo ====================================================================
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\VerifyDatabase.sql"

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Could not connect to SQL Server!
    echo.
    echo Possible issues:
    echo   1. SQL Server is not running
    echo   2. SQL Server instance name is incorrect
    echo.
    echo To start SQL Server:
    echo   1. Press Windows + R
    echo   2. Type: services.msc
    echo   3. Find "SQL Server (SQLEXPRESS)" and start it
    echo.
    pause
    exit /b 1
)

echo.
echo Press any key to continue with fixes...
pause > nul

echo.
echo ====================================================================
echo Step 2: Fixing Database Schema (CRITICAL)
echo ====================================================================
echo This creates Documents table and adds missing columns...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"

if %errorlevel% neq 0 (
    echo.
    echo WARNING: Schema fix encountered issues
    echo This may be normal if tables already exist
    echo.
) else (
    echo.
    echo SUCCESS: Database schema fixed!
    echo.
)

pause

echo.
echo ====================================================================
echo Step 3: Adding PACI Numbers
echo ====================================================================
echo This adds PACI Number fields to Properties and Tenants...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\AddPACINumbers.sql"

if %errorlevel% neq 0 (
    echo.
    echo WARNING: PACI columns may already exist (this is OK)
    echo.
) else (
    echo.
    echo SUCCESS: PACI numbers added!
    echo.
)

pause

echo.
echo ====================================================================
echo Step 4: Adding Kuwait Sample Data
echo ====================================================================
echo This creates 5 properties, 11 tenants, 17 leases, and more...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\AddKuwaitSampleData.sql"

if %errorlevel% neq 0 (
    echo.
    echo WARNING: Sample data may already exist or there was an error
    echo Check the output above for details
    echo.
) else (
    echo.
    echo SUCCESS: Sample data added!
    echo.
)

pause

echo.
echo ====================================================================
echo Step 5: Generating Invoices
echo ====================================================================
echo This creates invoices for current and previous month...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\GenerateInitialInvoices.sql"

if %errorlevel% neq 0 (
    echo.
    echo WARNING: Invoice generation encountered issues
    echo This may be normal if invoices already exist
    echo.
) else (
    echo.
    echo SUCCESS: Invoices generated!
    echo.
)

pause

echo.
echo ====================================================================
echo Step 6: Final Verification
echo ====================================================================
echo Checking database status after setup...
echo.

sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\VerifyDatabase.sql"

echo.
echo ====================================================================
echo DATABASE SETUP COMPLETE!
echo ====================================================================
echo.
echo Your database is now ready. You can start the application with:
echo.
echo   cd RentCollection.Web
echo   dotnet run
echo.
echo Then open your browser to: http://localhost:5032
echo.
echo Test the Reports module:
echo   1. Navigate to Financial -^> Reports
echo   2. Select "Payment Report"
echo   3. Click "This Month"
echo   4. Click "Generate Report"
echo   5. Data grid should appear
echo   6. Click "Export to Excel" to download
echo.
echo All modules should now work without errors!
echo.
echo ====================================================================
pause
