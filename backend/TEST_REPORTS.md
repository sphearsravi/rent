# Test Reports Module - Troubleshooting Guide

## Common Errors and Fixes

### 1. "GenerateReport" Action Not Found
**Error**: 404 or Action method not found

**Fix**: The GenerateReport action exists in the controller. Check:
1. URL should be: `/Reports/GenerateReport?reportType=payment&fromDate=2024-01-01&toDate=2024-12-31`
2. Method should be GET
3. All three parameters are required

### 2. Database Connection Errors
**Error**: Invalid column name or table doesn't exist

**Fix**: Run the database schema fix:
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

### 3. No Data Returned
**Error**: Empty grid or "No data available"

**Fix**:
1. Make sure you have data in the database
2. Check date range - data must exist within selected dates
3. Run sample data scripts if needed

### 4. Excel Export Fails
**Error**: File not downloading or corrupt file

**Fix**: Check EPPlus is installed:
```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet add package EPPlus --version 7.0.0
```

---

## Test Each Report Type

### Test 1: Payment Report
**Steps**:
1. Navigate to Reports page
2. Select "Payment Report" from dropdown
3. Click "This Month"
4. Click "Generate Report"
5. Should show monthly payment data

**Expected Result**:
- Data grid appears with columns: Month, Total Payments, Total Amount, Avg Payment, Payment Count
- At least one row of data if payments exist
- Totals row at bottom

### Test 2: Revenue Summary
**Steps**:
1. Select "Revenue Summary" from dropdown
2. Click "This Quarter"
3. Click "Generate Report"

**Expected Result**:
- Grid shows: Month, Property Count, Total Rent, Payments Received, Outstanding
- One row per month in quarter

### Test 3: Invoice Report
**Steps**:
1. Select "Invoice Report" from dropdown
2. Click "This Month"
3. Click "Generate Report"

**Expected Result**:
- Grid shows: Month, Invoices Generated, Total Amount, Paid, Pending, Overdue
- Data for current month

### Test 4: Excel Export
**Steps**:
1. Generate any report (steps above)
2. Click "Export to Excel" button
3. Wait for download

**Expected Result**:
- Excel file downloads
- File name format: `PaymentReport_YYYYMMDD_HHMMSS.xlsx`
- File opens in Excel
- Data formatted correctly
- Currency shows KWD with 3 decimals

---

## Manual Testing URLs

### Test Generate Report Directly
Open these URLs in browser (replace dates):

**Payment Report**:
```
http://localhost:5032/Reports/GenerateReport?reportType=payment&fromDate=2024-01-01&toDate=2024-12-31
```

**Revenue Summary**:
```
http://localhost:5032/Reports/GenerateReport?reportType=revenue&fromDate=2024-01-01&toDate=2024-12-31
```

**Invoice Report**:
```
http://localhost:5032/Reports/GenerateReport?reportType=invoice&fromDate=2024-01-01&toDate=2024-12-31
```

**Expected Response**: JSON with success=true and data array

---

## Check Browser Console for Errors

1. Open Reports page
2. Press F12 (Developer Tools)
3. Go to "Console" tab
4. Try to generate report
5. Look for red error messages

**Common Console Errors**:

### "Failed to fetch"
**Cause**: Server not running or wrong URL
**Fix**: Check application is running on port 5032

### "500 Internal Server Error"
**Cause**: Database error or missing data
**Fix**: Check server logs, run FixDatabaseSchema.sql

### "404 Not Found"
**Cause**: Wrong URL or action not found
**Fix**: Verify URL matches controller action

### "Unexpected token"
**Cause**: JSON parsing error
**Fix**: Check server is returning valid JSON

---

## Check Server Logs

When you run `dotnet run`, watch for errors in the console:

**Good Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5032
```

**Error Output** (examples):
```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand
      Invalid object name 'Invoices'
```
**Fix**: Run FixDatabaseSchema.sql

```
fail: Microsoft.EntityFrameworkCore.Query[10100]
      An exception occurred while iterating
```
**Fix**: Check entity relationships, run database fix

---

## Quick Diagnostic

Run this SQL to check if you have reportable data:

```sql
USE RentCollectionDB;
GO

SELECT
    'Payments' AS TableName,
    COUNT(*) AS Count,
    MIN(PaymentDate) AS EarliestDate,
    MAX(PaymentDate) AS LatestDate
FROM Payments
UNION ALL
SELECT
    'Invoices',
    COUNT(*),
    MIN(InvoiceDate),
    MAX(InvoiceDate)
FROM Invoices
UNION ALL
SELECT
    'Leases',
    COUNT(*),
    MIN(LeaseStartDate),
    MAX(LeaseStartDate)
FROM Leases;
GO
```

**Expected**:
- At least some records in each table
- Dates should be recent

---

## If Nothing Works

### Complete Reset:

1. **Stop application** (Ctrl+C)

2. **Fix database**:
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

3. **Ensure data exists**:
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

4. **Rebuild application**:
```bash
cd C:\Users\r.shankar\RentCollectionSystem
dotnet clean
dotnet build
```

5. **Run application**:
```bash
cd RentCollection.Web
dotnet run
```

6. **Test Reports**:
   - Navigate to http://localhost:5032/Reports
   - Select "Payment Report"
   - Click "This Month"
   - Click "Generate Report"

---

## Contact Information for Specific Errors

**Please provide**:
1. Exact error message (screenshot or copy-paste)
2. Which report type you selected
3. Date range you selected
4. Browser console errors (F12 → Console tab)
5. Server log output (from terminal where `dotnet run` is running)

This will help diagnose the exact issue you're experiencing.

