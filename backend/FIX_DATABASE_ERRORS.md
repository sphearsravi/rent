# Fix Database Errors - Step by Step Guide

## Problem
The application shows errors about missing tables and columns:
- `Invalid object name 'Documents'` - Documents table doesn't exist
- Missing columns in AspNetUsers table (FirstName, LastName, etc.)
- Column name mismatches in MaintenanceRequests table

## Solution - Run SQL Scripts in Order

### Step 1: Fix Database Schema
This adds all missing tables and columns.

**Run this command:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

**Or using SSMS:**
1. Open SQL Server Management Studio
2. Connect to `RAVIWST\SQLEXPRESS`
3. Open file: `C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql`
4. Execute (F5)

**What this script does:**
- ✅ Adds missing columns to AspNetUsers:
  - FirstName, LastName
  - CompanyId (with foreign key to Companies)
  - IsActive, EmailVerified, MFAEnabled
  - LastLogin, CreatedDate, ModifiedDate

- ✅ Creates Documents table with:
  - DocumentId, DocumentName, DocumentType
  - FilePath, FileSize
  - RelatedEntityType, RelatedEntityId
  - Description, UploadedBy, UploadedDate
  - Foreign key to AspNetUsers

- ✅ Adds missing columns to MaintenanceRequests:
  - Category, EstimatedCost
  - Notes, CreatedDate, ModifiedDate

### Step 2: Add PACI Numbers (If not already done)
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"
```

### Step 3: Add Sample Data (If not already done)
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"
```

### Step 4: Generate Invoices (If not already done)
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

---

## Verify Database is Fixed

Run this query in SSMS to verify all tables exist:

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

**Expected Result:**
All tables should show with record counts (Documents may be 0).

---

## Verify Column Structure

### Check AspNetUsers columns:
```sql
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'AspNetUsers'
ORDER BY ORDINAL_POSITION;
```

**Should include:**
- FirstName (nvarchar)
- LastName (nvarchar)
- CompanyId (int)
- IsActive (bit)
- EmailVerified (bit)
- MFAEnabled (bit)
- LastLogin (datetime2)
- CreatedDate (datetime2)
- ModifiedDate (datetime2)

### Check Documents table exists:
```sql
SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Documents';
```

**Should return:** 1 row

### Check MaintenanceRequests columns:
```sql
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'MaintenanceRequests'
ORDER BY ORDINAL_POSITION;
```

**Should include:**
- RequestId (int)
- Category (nvarchar)
- EstimatedCost (decimal)
- ActualCost (decimal)
- Notes (nvarchar)
- CreatedDate (datetime2)
- ModifiedDate (datetime2)

---

## Restart Application

After running the database fixes:

1. **Stop** the current application (Ctrl+C if running)
2. **Start** the application:
   ```bash
   cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
   dotnet run
   ```
3. **Open browser:** `http://localhost:5032`

---

## Test All Modules

### ✅ Test Documents Module
1. Navigate to **Operations → Documents**
2. Should load without errors
3. Test file upload
4. Verify documents list displays

### ✅ Test Maintenance Module
1. Navigate to **Operations → Maintenance**
2. Should show maintenance requests
3. Click "Create New"
4. All fields should work (Category dropdown, etc.)

### ✅ Test Reports Module
1. Navigate to **Financial → Reports**
2. Select report type from dropdown
3. Choose date range
4. Click "Generate Report"
5. Data grid should display
6. Click "Export to Excel"
7. Excel file should download

### ✅ Test Invoices Module
1. Navigate to **Financial → Invoices**
2. Should show invoices if generated
3. Click "Generate Monthly Invoices"
4. New invoices should be created

---

## Common Errors and Solutions

### Error: "Invalid object name 'Documents'"
**Solution:** Run `FixDatabaseSchema.sql` script

### Error: "Invalid column name 'FirstName'"
**Solution:** Run `FixDatabaseSchema.sql` script

### Error: "Cannot insert duplicate key"
**Solution:** Data already exists, skip that insert

### Error: "Failed to determine the https port"
**This is a warning, not an error.** Ignore it or access via http://localhost:5032

---

## All Scripts Summary

Run in this order for a fresh setup:

```bash
# 1. Fix schema (REQUIRED - fixes the errors)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"

# 2. Add PACI numbers (if not done)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"

# 3. Add sample data (if not done)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"

# 4. Generate invoices (if not done)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

---

## Script Execution Order (Visual)

```
┌─────────────────────────────┐
│  1. FixDatabaseSchema.sql   │  ← FIX ERRORS (Run this first!)
│  - Add Documents table      │
│  - Add AspNetUsers columns  │
│  - Fix MaintenanceRequests  │
└─────────────────────────────┘
              ↓
┌─────────────────────────────┐
│  2. AddPACINumbers.sql      │  ← Add PACI fields
│  - Properties.PACINumber    │
│  - Tenants.PACINumber       │
└─────────────────────────────┘
              ↓
┌─────────────────────────────┐
│  3. AddKuwaitSampleData.sql │  ← Add test data
│  - 5 Properties             │
│  - 11 Tenants               │
│  - 17 Leases                │
│  - Payments & Maintenance   │
└─────────────────────────────┘
              ↓
┌─────────────────────────────┐
│  4. GenerateInvoices.sql    │  ← Create invoices
│  - Current month invoices   │
│  - Previous month invoices  │
└─────────────────────────────┘
```

---

## After Running Scripts

**You should have:**
- ✅ All tables created (10 tables)
- ✅ All columns present
- ✅ Sample data loaded
- ✅ Invoices generated
- ✅ No errors when accessing any page

**Test checklist:**
- [ ] Dashboard loads
- [ ] Properties works
- [ ] Tenants works
- [ ] Leases works
- [ ] Payments works
- [ ] Invoices works (no "Documents" error)
- [ ] Maintenance works
- [ ] Documents works (table exists now)
- [ ] Reports works (can generate and export)

---

**If you still see errors after running FixDatabaseSchema.sql, please provide the exact error message.**

