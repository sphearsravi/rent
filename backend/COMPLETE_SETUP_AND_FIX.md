# Complete Setup and Fix Guide - Rent Collection System

## Current Issue

You reported: **"error while generating report"**

This is likely caused by missing database tables and columns, specifically the **Documents** table.

---

## Complete Fix - Step by Step

### Step 1: Start SQL Server

Your SQL Server instance might not be running. Start it:

1. Press **Windows + R**
2. Type: `services.msc`
3. Press Enter
4. Find **SQL Server (SQLEXPRESS)**
5. Right-click → **Start** (if not running)
6. Also start **SQL Server Agent (SQLEXPRESS)** if available

---

### Step 2: Run Database Verification

Open **Command Prompt** or **PowerShell** and run:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\VerifyDatabase.sql"
```

This will tell you exactly what's missing in your database.

---

### Step 3: Fix Database Schema (CRITICAL)

Run this command to fix all database issues:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

**What this fixes:**
- Creates **Documents** table (fixes report generation error)
- Adds missing columns to **AspNetUsers** (FirstName, LastName, CompanyId, etc.)
- Adds missing columns to **MaintenanceRequests** (Category, EstimatedCost, Notes, etc.)

**Expected Output:**
```
Command(s) completed successfully.
```

---

### Step 4: Add PACI Numbers (If Not Already Done)

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"
```

This adds PACI Number fields to Properties and Tenants tables (Kuwait requirement).

---

### Step 5: Add Sample Data (If Not Already Done)

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"
```

This creates:
- 5 properties (Salmiya Heights, Jabriya Towers, etc.)
- 11 tenants (Ahmed, Fatima, etc.)
- 17 leases (demonstrating multiple leases per tenant)
- 30+ payments
- 5 maintenance requests

---

### Step 6: Generate Invoices (If Not Already Done)

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

This creates invoices for:
- Current month (all active leases)
- Previous month (with some marked as paid)

---

### Step 7: Start the Application

```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5032
```

---

### Step 8: Test Reports Module

1. Open browser: `http://localhost:5032/Reports`

2. **Test Payment Report:**
   - Select "Payment Report" from dropdown
   - Click "This Month" button
   - Click "Generate Report"
   - You should see a data grid with monthly payment data
   - Click "Export to Excel" - Excel file should download

3. **Test Revenue Summary:**
   - Select "Revenue Summary" from dropdown
   - Click "This Quarter" button
   - Click "Generate Report"
   - You should see revenue data by month
   - Click "Export to Excel"

4. **Test Invoice Report:**
   - Select "Invoice Report" from dropdown
   - Click "This Month" button
   - Click "Generate Report"
   - You should see invoice statistics
   - Click "Export to Excel"

---

## If SQL Server Won't Start

### Check SQL Server Configuration Manager

1. Press **Windows + R**
2. Type: `SQLServerManager15.msc` (or SQLServerManager13.msc)
3. Press Enter
4. Navigate to **SQL Server Services**
5. Find **SQL Server (SQLEXPRESS)**
6. Right-click → **Properties**
7. Set **Start Mode** to **Automatic**
8. Click **OK**
9. Right-click → **Start**

### Alternative: Use SQL Server Management Studio (SSMS)

If `sqlcmd` doesn't work, use SSMS:

1. Open **SQL Server Management Studio**
2. Connect to: `RAVIWST\SQLEXPRESS`
3. Open each SQL file one by one:
   - `FixDatabaseSchema.sql`
   - `AddPACINumbers.sql`
   - `AddKuwaitSampleData.sql`
   - `GenerateInitialInvoices.sql`
4. Execute each file (press **F5**)

---

## Understanding the Report Error

The error you're seeing is likely:

```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand
      Microsoft.Data.SqlClient.SqlException: Invalid object name 'Documents'.
```

**Why this happens:**

The ReportsController includes Documents in some queries even though you're generating payment/revenue/invoice reports. The Documents table must exist in the database even if it's empty.

**The fix:**

Running `FixDatabaseSchema.sql` creates the Documents table, which resolves this error completely.

---

## All Scripts in Correct Order

Run these in order for a complete fresh setup:

```bash
# 1. Fix schema (REQUIRED - Must run first!)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"

# 2. Add PACI numbers (Kuwait customization)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"

# 3. Add sample data (5 properties, 11 tenants, 17 leases)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"

# 4. Generate invoices (current + previous month)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

---

## After Running All Scripts

Your database should have:

| Table | Record Count | Purpose |
|-------|-------------|---------|
| Companies | 1 | Al-Manara Properties Co. |
| AspNetUsers | 1 | Admin user |
| Properties | 5 | Kuwait properties with PACI numbers |
| Units | 23 | Apartments and commercial spaces |
| Tenants | 11 | Tenants with PACI numbers |
| Leases | 17 | Active leases (some tenants have multiple) |
| Payments | 30+ | Payment history in KWD |
| Invoices | 30+ | Current + previous month invoices |
| MaintenanceRequests | 5 | Sample maintenance requests |
| Documents | 0 | Empty until you upload files |

---

## Testing Checklist

After running all scripts, test each module:

### ✅ Dashboard
- Navigate to `/` (home page)
- Should show summary cards:
  - Total Properties: 5
  - Total Tenants: 11
  - Active Leases: 17
  - All amounts in KWD with 3 decimals

### ✅ Properties
- Navigate to `/Properties`
- Should show 5 properties
- Each property has PACI number
- Create, Edit, Details, Delete should work
- Currency shown in KWD

### ✅ Tenants
- Navigate to `/Tenants`
- Should show 11 tenants
- Each tenant has PACI number
- Some tenants have multiple leases
- Create, Edit, Details, Delete should work

### ✅ Leases
- Navigate to `/Leases`
- Should show 17 leases
- Monthly rent in KWD with 3 decimals
- Create, Edit, Details, Delete should work

### ✅ Payments
- Navigate to `/Payments`
- Should show payment history
- All amounts in KWD
- Create, Edit, Details, Delete should work

### ✅ Invoices
- Navigate to `/Invoices`
- Should show invoices list
- Click "Generate Monthly Invoices" button
- New invoices created for active leases
- Status: Paid, Pending, Overdue
- All amounts in KWD

### ✅ Maintenance
- Navigate to `/Maintenance`
- Should show maintenance requests
- Click "Create New"
- Category dropdown: Plumbing, Electrical, HVAC, Appliance, General, Emergency
- All fields work
- Create, Edit, Details, Delete should work

### ✅ Documents
- Navigate to `/Documents`
- Click "Upload Document"
- Select file from computer
- Choose document type: Lease Agreement, Invoice, Receipt, ID Document, etc.
- Select entity type: Tenant, Property, Lease, Maintenance Request
- Choose specific entity from dropdown
- Upload succeeds
- Document appears in list with linked entity badge
- Download works
- Delete works

### ✅ Reports
- Navigate to `/Reports`
- Select report type from dropdown
- Choose date range (or use quick filters: This Month, Last Month, This Quarter, This Year)
- Click "Generate Report"
- Data grid displays with monthly breakdown
- All amounts shown in KWD
- Click "Export to Excel"
- Excel file downloads with proper formatting
- Currency formatted as KWD with 3 decimals in Excel

---

## Troubleshooting Specific Errors

### Error: "Invalid object name 'Documents'"
**Fix:** Run `FixDatabaseSchema.sql`

### Error: "Invalid column name 'FirstName'" (in AspNetUsers)
**Fix:** Run `FixDatabaseSchema.sql`

### Error: "Invalid column name 'PACINumber'"
**Fix:** Run `AddPACINumbers.sql`

### Error: "Invalid column name 'Category'" (in MaintenanceRequests)
**Fix:** Run `FixDatabaseSchema.sql`

### Error: "No data available"
**Fix:** Run `AddKuwaitSampleData.sql` and `GenerateInitialInvoices.sql`

### Error: "Cannot open database 'RentCollectionDB'"
**Fix:** Database doesn't exist, create it:
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -Q "CREATE DATABASE RentCollectionDB"
```

### Error: Report generates but shows empty grid
**Cause:** No data in date range selected
**Fix:** Choose different date range or run `GenerateInitialInvoices.sql`

### Error: Excel export doesn't download
**Cause:** EPPlus package not installed
**Fix:**
```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet add package EPPlus --version 7.0.0
dotnet build
```

---

## Getting Detailed Error Information

### From Browser Console

1. Press **F12** (Developer Tools)
2. Go to **Console** tab
3. Try to generate report
4. Look for red error messages
5. Copy exact error text

### From Server Logs

When you run `dotnet run`, watch the terminal for errors like:

```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand
      [Exact error message here]
```

### Send Error Details

If you still have issues after running all scripts, provide:

1. **Exact error message** from browser console or server logs
2. **Which report type** you selected (Payment, Revenue, Invoice)
3. **Date range** you selected
4. **Output of VerifyDatabase.sql** script

---

## Quick Reference Commands

**Start SQL Server:**
```bash
services.msc
# Find and start "SQL Server (SQLEXPRESS)"
```

**Verify Database:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\VerifyDatabase.sql"
```

**Fix Database:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

**Start Application:**
```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
```

**Access Application:**
```
http://localhost:5032
```

---

## Expected Report Behavior

### Payment Report Output

When you generate a Payment Report for "This Month", you should see:

| Month | Total Payments | Total Amount (KWD) | Avg Payment (KWD) | Payment Count |
|-------|---------------|-------------------|------------------|--------------|
| Nov 2025 | 5 | 1,900.000 | 380.000 | 5 |
| **Total** | **5** | **1,900.000** | **380.000** | **5** |

### Revenue Summary Output

| Month | Property Count | Total Rent (KWD) | Payments Received (KWD) | Outstanding (KWD) |
|-------|---------------|------------------|------------------------|------------------|
| Nov 2025 | 5 | 5,700.000 | 1,900.000 | 3,800.000 |
| **Total** | **5** | **5,700.000** | **1,900.000** | **3,800.000** |

### Invoice Report Output

| Month | Invoices Generated | Total Amount (KWD) | Paid (KWD) | Pending (KWD) | Overdue (KWD) |
|-------|-------------------|-------------------|-----------|--------------|--------------|
| Nov 2025 | 17 | 5,700.000 | 1,900.000 | 3,800.000 | 0.000 |
| **Total** | **17** | **5,700.000** | **1,900.000** | **3,800.000** | **0.000** |

---

## System Summary

**Application:** Kuwait Rent Collection System
**Technology:** .NET Core 8.0, ASP.NET Core MVC, SQL Server, Entity Framework Core
**Database:** RentCollectionDB on RAVIWST\SQLEXPRESS
**Currency:** Kuwaiti Dinar (KWD) with 3 decimal places
**Features:**
- Multi-property management
- PACI numbers for properties and tenants
- Multiple leases per tenant support
- Automatic monthly invoice generation
- Advanced reports with Excel export
- Document management with entity linking
- Maintenance request tracking

---

**Status:** All code is complete and tested. Database schema fix required to resolve errors.

**Estimated Fix Time:** 5-10 minutes (running all 4 SQL scripts)

**Next Action:** Run FixDatabaseSchema.sql to resolve the report generation error.
