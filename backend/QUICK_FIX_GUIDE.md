# Quick Fix Guide - Resolve All Database Errors

## Current Problem
You're seeing errors like:
- "Invalid object name 'Documents'"
- "error while generating report"
- Missing columns in database tables

## THE FIX (Run This Now)

### Step 1: Stop the Application
If the application is running, press **Ctrl+C** in the terminal to stop it.

### Step 2: Run Database Fix Script
Open a **NEW** command prompt or PowerShell window and run:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

**Expected Output:**
```
Command(s) completed successfully.
```

### Step 3: Verify Database is Fixed
Run this verification query:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -d RentCollectionDB -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('Documents', 'Invoices', 'MaintenanceRequests') ORDER BY TABLE_NAME"
```

**Expected Output:**
```
TABLE_NAME
Documents
Invoices
MaintenanceRequests
```

### Step 4: Restart Application
Go back to your original terminal and run:

```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
```

### Step 5: Test Reports Module
1. Open browser: `http://localhost:5032/Reports`
2. Select "Payment Report" from dropdown
3. Click "This Month" quick filter button
4. Click "Generate Report"
5. You should see data grid with monthly payment data
6. Click "Export to Excel" button to download Excel file

---

## What This Fix Does

The `FixDatabaseSchema.sql` script:

1. **Creates Documents table** (fixes "Invalid object name 'Documents'" error)
   - DocumentId, DocumentName, DocumentType
   - FilePath, FileSize
   - RelatedEntityType, RelatedEntityId
   - Description, UploadedBy, UploadedDate

2. **Adds missing columns to AspNetUsers table**
   - FirstName, LastName
   - CompanyId (with foreign key)
   - IsActive, EmailVerified, MFAEnabled
   - LastLogin, CreatedDate, ModifiedDate

3. **Adds missing columns to MaintenanceRequests table**
   - Category, EstimatedCost
   - Notes, CreatedDate, ModifiedDate

---

## After Running the Fix

### ✅ All Modules Should Work

Test each module to confirm:

#### 1. Documents Module
- Navigate to **Operations → Documents**
- Should load without errors
- Test uploading a file
- Select entity type (Tenant/Property/Lease/Maintenance)
- Upload should succeed

#### 2. Reports Module
- Navigate to **Financial → Reports**
- Select report type: Payment Report
- Choose date range: This Month
- Click Generate Report
- Data grid should display
- Click Export to Excel
- Excel file should download

#### 3. Invoices Module
- Navigate to **Financial → Invoices**
- Should show invoices list
- Click "Generate Monthly Invoices"
- New invoices should be created for active leases

#### 4. Maintenance Module
- Navigate to **Operations → Maintenance**
- Should show maintenance requests
- Click "Create New"
- Category dropdown should work
- All fields should be present

---

## Troubleshooting

### If sqlcmd command not found
Install SQL Server Command Line Tools or use SQL Server Management Studio (SSMS):

1. Open SSMS
2. Connect to `RAVIWST\SQLEXPRESS`
3. Open file: `C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql`
4. Press F5 to execute

### If "Database doesn't exist" error
First create the database:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -Q "CREATE DATABASE RentCollectionDB"
```

Then run all scripts in order:
1. FixDatabaseSchema.sql
2. AddPACINumbers.sql
3. AddKuwaitSampleData.sql
4. GenerateInitialInvoices.sql

### If Reports Still Show Error
Check the exact error:

1. **In Browser**: Press F12, go to Console tab, look for errors
2. **In Server**: Check terminal where `dotnet run` is running for error messages

Common errors:

**"No data available"** = Sample data not loaded, run AddKuwaitSampleData.sql
**"404 Not Found"** = URL is wrong, check browser address bar
**"500 Internal Server Error"** = Database schema issue, re-run FixDatabaseSchema.sql

---

## Complete Database Setup (If Starting Fresh)

If you want to completely reset the database:

```bash
# 1. Fix database schema (REQUIRED)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"

# 2. Add PACI numbers to Properties and Tenants
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"

# 3. Add Kuwait sample data (5 properties, 11 tenants, 17 leases)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"

# 4. Generate invoices for current and previous month
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

---

## Quick Health Check

Run this query to see if all data exists:

```sql
USE RentCollectionDB;
GO

SELECT
    'Companies' AS TableName, COUNT(*) AS RecordCount FROM Companies
UNION ALL SELECT 'AspNetUsers', COUNT(*) FROM AspNetUsers
UNION ALL SELECT 'Properties', COUNT(*) FROM Properties
UNION ALL SELECT 'Units', COUNT(*) FROM Units
UNION ALL SELECT 'Tenants', COUNT(*) FROM Tenants
UNION ALL SELECT 'Leases', COUNT(*) FROM Leases
UNION ALL SELECT 'Payments', COUNT(*) FROM Payments
UNION ALL SELECT 'Invoices', COUNT(*) FROM Invoices
UNION ALL SELECT 'MaintenanceRequests', COUNT(*) FROM MaintenanceRequests
UNION ALL SELECT 'Documents', COUNT(*) FROM Documents;
GO
```

**Expected Result** (after running all scripts):
```
TableName              RecordCount
Companies              1
AspNetUsers            1
Properties             5
Units                  23
Tenants                11
Leases                 17
Payments               30+
Invoices               30+
MaintenanceRequests    5
Documents              0 (will increase as you upload)
```

---

## Next Steps After Fix

Once database is fixed and application is running:

1. **Test Dashboard** - Should show summary cards with counts and KWD amounts
2. **Test Properties** - View properties with PACI numbers
3. **Test Tenants** - View tenants with PACI numbers, see multiple leases per tenant
4. **Test Leases** - See all active leases with KWD rent amounts
5. **Test Payments** - View payment history in KWD
6. **Test Invoices** - Generate monthly invoices, see status (Paid/Pending/Overdue)
7. **Test Maintenance** - Create maintenance requests, assign categories
8. **Test Documents** - Upload documents and link to entities
9. **Test Reports** - Generate all 7 report types, export to Excel

---

## If You Still See Errors

Please provide:
1. **Exact error message** (screenshot or copy-paste)
2. **Which module/page** you're trying to access
3. **Browser console errors** (F12 → Console tab)
4. **Server terminal output** (from where `dotnet run` is running)

This will help diagnose the specific issue.

---

**Status**: This fix resolves all known database schema errors.
**Time to Fix**: 2-3 minutes
**Impact**: All modules will work correctly after running this fix.
