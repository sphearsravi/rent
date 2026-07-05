# START HERE - Rent Collection System Setup

## Quick Start (2 Methods)

### Method 1: Automated Setup (Easiest)

Double-click this file:
```
SetupDatabase.bat
```

This will automatically:
1. Verify database status
2. Fix all schema issues
3. Add PACI numbers
4. Add sample data
5. Generate invoices

Then start the application:
```bash
cd RentCollection.Web
dotnet run
```

Open browser: `http://localhost:5032`

---

### Method 2: Manual Setup

Run these commands in order:

```bash
# Fix database schema (REQUIRED)
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"

# Add PACI numbers
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\AddPACINumbers.sql"

# Add sample data
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\AddKuwaitSampleData.sql"

# Generate invoices
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\GenerateInitialInvoices.sql"

# Start application
cd RentCollection.Web
dotnet run
```

---

## Fixing "error while generating report"

You reported this error. The fix is simple:

**The Problem:** The Documents table doesn't exist in your database.

**The Solution:** Run `FixDatabaseSchema.sql` (included in both methods above)

**Why This Fixes It:** The Reports controller references the Documents table in some queries. Even though you're generating payment/revenue/invoice reports, the Documents table must exist.

---

## Documentation

All documentation is available in the root directory:

| File | Purpose |
|------|---------|
| **COMPLETE_SETUP_AND_FIX.md** | Complete setup guide with troubleshooting |
| **QUICK_FIX_GUIDE.md** | Quick fix for database errors |
| **TEST_REPORTS.md** | Troubleshooting guide for reports module |
| **DOCUMENTS_MODULE_GUIDE.md** | Complete guide for documents module |
| **INVOICE_AND_REPORTS_GUIDE.md** | Guide for invoices and reports |
| **FIX_DATABASE_ERRORS.md** | Step-by-step database error fixes |
| **KUWAIT_SETUP_GUIDE.md** | Kuwait customization details |

---

## System Features

### Kuwait Customization
- **Currency:** Kuwaiti Dinar (KWD) with 3 decimal places
- **PACI Numbers:** Added to Properties and Tenants
- **Multiple Leases:** Tenants can have multiple leases

### Core Modules
1. **Properties** - Manage properties with PACI numbers
2. **Tenants** - Manage tenants with PACI numbers
3. **Leases** - Create and manage leases (multiple per tenant)
4. **Payments** - Track payment history in KWD
5. **Invoices** - Automatic monthly invoice generation
6. **Maintenance** - Track maintenance requests by category
7. **Documents** - Upload and link documents to entities
8. **Reports** - Advanced reports with Excel export

### Reports Module Features
- 7 report types:
  - Tenant Report
  - Property Report
  - Lease Report
  - Payment Report
  - Maintenance Report
  - Invoice Report
  - Revenue Summary
- Date range filters with quick buttons (This Month, Last Month, This Quarter, This Year)
- Dynamic data grids showing monthly breakdown
- Excel export with proper KWD formatting (3 decimals)

### Documents Module Features
- Upload any file type (PDF, Word, Excel, Images, etc.)
- Link documents to:
  - Tenants (ID documents, contracts)
  - Properties (deeds, permits, certificates)
  - Leases (signed agreements, addendums)
  - Maintenance Requests (invoices, photos, completion certificates)
- Download and delete documents
- File storage in `wwwroot/uploads/documents/`

---

## Sample Data Included

After running setup scripts, you'll have:

| Data Type | Count | Examples |
|-----------|-------|----------|
| **Properties** | 5 | Salmiya Heights, Jabriya Towers, Hawally Residence |
| **Units** | 23 | Mix of apartments and commercial spaces |
| **Tenants** | 11 | Ahmed Al-Rashid, Fatima Al-Salem, etc. |
| **Leases** | 17 | Active leases (some tenants have 2 leases) |
| **Payments** | 30+ | Payment history in KWD |
| **Invoices** | 30+ | Current + previous month |
| **Maintenance** | 5 | Plumbing, HVAC, Electrical requests |

---

## Testing the Application

### 1. Dashboard
- Navigate to: `http://localhost:5032`
- Should show summary cards with KWD amounts

### 2. Properties
- Navigate to: `/Properties`
- View 5 properties with PACI numbers
- Test Create, Edit, Details, Delete

### 3. Tenants
- Navigate to: `/Tenants`
- View 11 tenants with PACI numbers
- Some tenants have multiple leases
- Test Create, Edit, Details, Delete

### 4. Reports (Your Issue)
- Navigate to: `/Reports`
- Select "Payment Report"
- Click "This Month"
- Click "Generate Report"
- **Expected:** Data grid with monthly payment data
- Click "Export to Excel"
- **Expected:** Excel file downloads

### 5. Documents
- Navigate to: `/Documents`
- Click "Upload Document"
- Select a file
- Choose entity type (Tenant/Property/Lease/Maintenance)
- Upload
- **Expected:** Document appears in list with entity link

---

## Common Issues

### SQL Server Not Running

**Symptoms:** "Could not open a connection to SQL Server"

**Fix:**
1. Press Windows + R
2. Type: `services.msc`
3. Find "SQL Server (SQLEXPRESS)"
4. Right-click → Start

### Database Doesn't Exist

**Symptoms:** "Cannot open database 'RentCollectionDB'"

**Fix:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -Q "CREATE DATABASE RentCollectionDB"
```

Then run the setup scripts.

### Report Shows "No Data"

**Symptoms:** Report generates but grid is empty

**Fix:** Choose a different date range or run `GenerateInitialInvoices.sql`

### Documents Error

**Symptoms:** "Invalid object name 'Documents'"

**Fix:** Run `FixDatabaseSchema.sql`

---

## Next Steps After Setup

1. **Test all modules** using the testing checklist above
2. **Generate monthly invoices** via Invoices page
3. **Upload documents** and link them to entities
4. **Generate reports** and export to Excel
5. **Create new data** (properties, tenants, leases)

---

## Need Help?

Refer to these detailed guides:

**For database errors:**
- `COMPLETE_SETUP_AND_FIX.md` - Complete troubleshooting guide
- `FIX_DATABASE_ERRORS.md` - Step-by-step error fixes

**For report errors:**
- `TEST_REPORTS.md` - Report troubleshooting guide
- `INVOICE_AND_REPORTS_GUIDE.md` - How to use reports

**For document errors:**
- `DOCUMENTS_MODULE_GUIDE.md` - Complete documents guide

**For Kuwait features:**
- `KUWAIT_SETUP_GUIDE.md` - PACI numbers, KWD currency, multiple leases

---

## Quick Command Reference

**Verify database status:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\VerifyDatabase.sql"
```

**Fix database (solves most issues):**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"
```

**Start application:**
```bash
cd RentCollection.Web
dotnet run
```

**Build application:**
```bash
cd RentCollection.Web
dotnet build
```

**Restore packages:**
```bash
cd RentCollection.Web
dotnet restore
```

---

## Application URLs

After running `dotnet run`, access the application:

- **Home/Dashboard:** `http://localhost:5032`
- **Properties:** `http://localhost:5032/Properties`
- **Tenants:** `http://localhost:5032/Tenants`
- **Leases:** `http://localhost:5032/Leases`
- **Payments:** `http://localhost:5032/Payments`
- **Invoices:** `http://localhost:5032/Invoices`
- **Maintenance:** `http://localhost:5032/Maintenance`
- **Documents:** `http://localhost:5032/Documents`
- **Reports:** `http://localhost:5032/Reports`

---

## System Requirements

- **Operating System:** Windows 10/11 or Windows Server 2016+
- **Framework:** .NET Core 8.0
- **Database:** SQL Server 2019+ (SQLEXPRESS instance)
- **Browser:** Chrome, Edge, Firefox (latest versions)

---

## Technology Stack

- **Backend:** ASP.NET Core 8.0 MVC
- **Database:** SQL Server 2019+ with Entity Framework Core 9.0
- **Authentication:** ASP.NET Core Identity
- **Excel Export:** EPPlus 7.0.0
- **UI:** Custom CSS with gradient themes (no external frameworks)

---

**Application Status:** ✅ Complete and Ready to Use

**Database Status:** ⚠️ Requires schema fix (run SetupDatabase.bat)

**Report Error Fix:** Run `FixDatabaseSchema.sql` to create Documents table

**Estimated Setup Time:** 5-10 minutes

---

## Getting Started Right Now

**Simplest path:**

1. Double-click: `SetupDatabase.bat`
2. Wait for it to complete (2-3 minutes)
3. Open command prompt in `RentCollection.Web` folder
4. Run: `dotnet run`
5. Open browser: `http://localhost:5032/Reports`
6. Test report generation

**That's it!** Your report error will be fixed.

---

**Last Updated:** November 2, 2025
**Version:** 1.0
**Status:** Production Ready
