# Fix Documents Table - PowerShell Script
# This script checks and fixes the Documents table issue

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "DOCUMENTS TABLE FIX SCRIPT" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

# Check if SQL Server is accessible
Write-Host "Step 1: Checking SQL Server connection..." -ForegroundColor Yellow
try {
    $result = sqlcmd -S "RAVIWST\SQLEXPRESS" -E -Q "SELECT @@VERSION" -h -1 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SUCCESS: SQL Server is accessible" -ForegroundColor Green
    } else {
        throw "Cannot connect to SQL Server"
    }
} catch {
    Write-Host "ERROR: Cannot connect to SQL Server" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please start SQL Server:" -ForegroundColor Yellow
    Write-Host "  1. Press Windows + R" -ForegroundColor White
    Write-Host "  2. Type: services.msc" -ForegroundColor White
    Write-Host "  3. Find 'SQL Server (SQLEXPRESS)' and start it" -ForegroundColor White
    Write-Host ""
    pause
    exit 1
}

Write-Host ""
Write-Host "Step 2: Checking if Documents table exists..." -ForegroundColor Yellow

$checkQuery = @"
USE RentCollectionDB;
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents')
    SELECT 'EXISTS'
ELSE
    SELECT 'NOT EXISTS'
"@

$tableStatus = sqlcmd -S "RAVIWST\SQLEXPRESS" -E -Q $checkQuery -h -1 2>&1 | Select-String -Pattern "EXISTS" | Select-Object -First 1

if ($tableStatus -match "NOT EXISTS") {
    Write-Host "Documents table does NOT exist - creating it now..." -ForegroundColor Red
    Write-Host ""

    Write-Host "Step 3: Running FixDatabaseSchema.sql..." -ForegroundColor Yellow

    $scriptPath = Join-Path $PSScriptRoot "Database\FixDatabaseSchema.sql"

    if (Test-Path $scriptPath) {
        sqlcmd -S "RAVIWST\SQLEXPRESS" -E -i $scriptPath

        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "SUCCESS: Database schema fixed!" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "WARNING: There may have been issues. Check output above." -ForegroundColor Yellow
        }
    } else {
        Write-Host "ERROR: Cannot find FixDatabaseSchema.sql at: $scriptPath" -ForegroundColor Red
        exit 1
    }
} elseif ($tableStatus -match "EXISTS") {
    Write-Host "SUCCESS: Documents table already exists" -ForegroundColor Green
} else {
    Write-Host "WARNING: Could not determine table status" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Step 4: Verifying Documents table structure..." -ForegroundColor Yellow

$verifyQuery = @"
USE RentCollectionDB;
SELECT COUNT(*) AS ColumnCount
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Documents';
"@

$columnCount = sqlcmd -S "RAVIWST\SQLEXPRESS" -E -Q $verifyQuery -h -1 2>&1 | Select-String -Pattern "\d+" | Select-Object -First 1

Write-Host "Documents table has $columnCount columns" -ForegroundColor White

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "FIX COMPLETE!" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart your application (Ctrl+C then 'dotnet run')" -ForegroundColor White
Write-Host "  2. Navigate to: http://localhost:5032/Documents" -ForegroundColor White
Write-Host "  3. Click 'Upload Document'" -ForegroundColor White
Write-Host "  4. Try uploading a file" -ForegroundColor White
Write-Host ""
Write-Host "If you still see errors, provide the EXACT error message." -ForegroundColor Yellow
Write-Host ""

pause
