# Document Upload Error - Complete Fix Guide

## Error You're Seeing

```
Error uploading document: An error occurred while saving the entity changes. See the inner exception for details.
```

## Root Cause

This error occurs because the **Documents table doesn't exist in your database**. The application is trying to save a document record, but the table hasn't been created yet.

---

## Quick Fix (Choose One Method)

### Method 1: Double-Click Batch File (Easiest)

Simply double-click this file:
```
FixDocumentsTable.bat
```

This will automatically check and create the Documents table.

### Method 2: Run PowerShell Script

Right-click `FixDocumentsTable.ps1` → **Run with PowerShell**

### Method 3: Manual Command Line

Open Command Prompt and run:
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\FixDatabaseSchema.sql"
```

### Method 4: Complete Database Setup

If you want to set up everything at once, double-click:
```
SetupDatabase.bat
```

This will:
- Create Documents table
- Add missing columns to other tables
- Add PACI numbers
- Load sample data
- Generate invoices

---

## After Running the Fix

1. **Restart the application:**
   ```bash
   # Press Ctrl+C to stop the app, then run:
   cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
   dotnet run
   ```

2. **Test document upload:**
   - Navigate to: `http://localhost:5032/Documents`
   - Click "Upload Document"
   - Select a file from your computer
   - Choose document type (e.g., "Lease Agreement")
   - Optionally link to an entity (Tenant/Property/Lease/Maintenance)
   - Click "Upload Document"
   - ✅ Should see: "Document uploaded successfully!"

---

## What Gets Created

The `FixDatabaseSchema.sql` script creates the Documents table with these columns:

| Column | Type | Description |
|--------|------|-------------|
| DocumentId | INT (Primary Key) | Unique document identifier |
| DocumentName | NVARCHAR(255) | Original file name |
| DocumentType | NVARCHAR(100) | Type of document (Lease Agreement, Invoice, etc.) |
| FilePath | NVARCHAR(500) | Path to file on server |
| FileSize | BIGINT | File size in bytes |
| RelatedEntityType | NVARCHAR(50) | Tenant, Property, Lease, or Maintenance |
| RelatedEntityId | INT | ID of related entity |
| Description | NVARCHAR(MAX) | Optional description |
| UploadedBy | NVARCHAR(450) | User who uploaded (references AspNetUsers) |
| UploadedDate | DATETIME2 | When file was uploaded |

---

## Verifying the Fix

After running the fix, check if the table exists:

```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\QuickCheck.sql"
```

**Expected Output:**
```
SUCCESS: Documents table exists

Documents table columns:
DocumentId, DocumentName, DocumentType, FilePath, FileSize,
RelatedEntityType, RelatedEntityId, Description, UploadedBy, UploadedDate

Document count: 0
```

---

## If SQL Server Won't Connect

**Error:** "Cannot connect to SQL Server" or "Login timeout expired"

**Fix: Start SQL Server**

1. Press **Windows + R**
2. Type: `services.msc`
3. Press Enter
4. Find **SQL Server (SQLEXPRESS)**
5. Right-click → **Start**
6. Wait for it to start (Status should show "Running")
7. Try the fix again

---

## Understanding the Error

### Original Error (Casting Issue)
✅ **FIXED** - Changed `List<dynamic>` to `IEnumerable<dynamic>` in Upload.cshtml

### Current Error (Missing Table)
⚠️ **NEEDS FIX** - Run one of the methods above to create Documents table

### Error Flow

1. User selects file and clicks "Upload Document"
2. File is saved to disk successfully (in `wwwroot/uploads/documents/`)
3. Application tries to create database record: `_context.Documents.Add(document)`
4. Application tries to save: `await _context.SaveChangesAsync()`
5. ❌ **ERROR:** SQL Server says "Documents table doesn't exist"
6. Exception is caught and error message shown to user

---

## Testing the Complete Upload Flow

### 1. Upload Document Without Entity Link

- Navigate to: `http://localhost:5032/Documents`
- Click "Upload Document"
- Select any file (PDF, Word, Image, etc.)
- Choose document type: "General Document"
- Leave entity type as "-- None --"
- Add description (optional)
- Click "Upload Document"
- ✅ Should succeed and show in Documents list

### 2. Upload Document Linked to Tenant

- Click "Upload Document" again
- Select file
- Choose document type: "ID Document"
- Select entity type: "Tenant"
- Choose a tenant from dropdown (e.g., "Ahmed Al-Rashid - 265123456789")
- Click "Upload Document"
- ✅ Should succeed with tenant badge shown

### 3. Upload Document Linked to Property

- Click "Upload Document"
- Select file
- Choose document type: "Deed"
- Select entity type: "Property"
- Choose a property (e.g., "Salmiya Heights - PROP12345")
- Click "Upload Document"
- ✅ Should succeed with property badge shown

### 4. Upload Document Linked to Lease

- Click "Upload Document"
- Select file
- Choose document type: "Lease Agreement"
- Select entity type: "Lease"
- Choose a lease
- Click "Upload Document"
- ✅ Should succeed with lease badge shown

### 5. Upload Document Linked to Maintenance

- Click "Upload Document"
- Select file
- Choose document type: "Invoice"
- Select entity type: "Maintenance Request"
- Choose a maintenance request
- Click "Upload Document"
- ✅ Should succeed with maintenance badge shown

---

## Common Upload Issues

### Issue 1: "Please select a file to upload"
**Cause:** No file was selected
**Fix:** Click "Choose File" button and select a file before submitting

### Issue 2: "Error uploading document: Invalid object name 'Documents'"
**Cause:** Documents table doesn't exist
**Fix:** Run one of the fix methods above

### Issue 3: "Error uploading document: Cannot insert the value NULL into column 'UploadedBy'"
**Cause:** UploadedBy constraint issue
**Fix:** Already handled in code - uses default "admin@rentcollection.com"

### Issue 4: File uploads but doesn't show in list
**Cause:** Permission issue or path problem
**Fix:** Check that `wwwroot/uploads/documents/` folder exists and has write permissions

### Issue 5: Download doesn't work
**Cause:** File path issue
**Fix:** Ensure files are in `wwwroot/uploads/documents/` folder

---

## File Storage Details

Uploaded files are stored in:
```
C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\wwwroot\uploads\documents\
```

**File Naming Format:**
```
{GUID}_{OriginalFileName}
Example: 3a5b7c9d-1e2f-3456-7890-abcdef123456_Contract.pdf
```

**Database stores:**
- Relative path: `uploads/documents/3a5b7c9d-1e2f-3456-7890-abcdef123456_Contract.pdf`
- Original name: `Contract.pdf`
- File size: `2547896` (bytes)

---

## Document Types Available

- Lease Agreement
- Invoice
- Receipt
- Payment Confirmation
- ID Document (Civil ID)
- Passport
- Visa
- Deed
- Certificate
- Permit
- Inspection Report
- Maintenance Invoice
- Photo
- General Document

---

## Allowed File Types

The application accepts all file types, but common ones include:

**Documents:**
- PDF (.pdf)
- Word (.docx, .doc)
- Excel (.xlsx, .xls)
- Text (.txt)

**Images:**
- JPEG (.jpg, .jpeg)
- PNG (.png)
- GIF (.gif)
- BMP (.bmp)

**Other:**
- ZIP (.zip)
- Any file type

---

## Security Notes

1. Files are stored with GUID-prefixed names to prevent conflicts
2. Original filenames are preserved in database
3. File paths are relative, not absolute
4. User info (UploadedBy) is tracked for auditing
5. Entity linking enables proper document organization

---

## Next Steps After Fix

1. ✅ Run the fix script to create Documents table
2. ✅ Restart application
3. ✅ Test document upload
4. ✅ Test document download
5. ✅ Test document deletion
6. ✅ Verify entity linking works for all types

---

## Related Documentation

- **[README_START_HERE.md](README_START_HERE.md)** - Main setup guide
- **[COMPLETE_SETUP_AND_FIX.md](COMPLETE_SETUP_AND_FIX.md)** - Complete troubleshooting
- **[DOCUMENTS_MODULE_GUIDE.md](DOCUMENTS_MODULE_GUIDE.md)** - Full documents module guide
- **[DOCUMENT_UPLOAD_FIX.md](DOCUMENT_UPLOAD_FIX.md)** - Casting error fix (already applied)

---

## Summary of All Fixes Applied Today

1. ✅ **Type Casting Error** - Fixed `List<dynamic>` to `IEnumerable<dynamic>` in Upload.cshtml
2. ✅ **Better Error Messages** - Added inner exception display in DocumentsController
3. ✅ **Fix Scripts Created** - FixDocumentsTable.bat and .ps1
4. ⚠️ **Documents Table** - Run fix script to create table (YOU NEED TO DO THIS)

---

## Quick Commands Reference

**Create Documents table:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\FixDatabaseSchema.sql"
```

**Check if table exists:**
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "Database\QuickCheck.sql"
```

**Restart application:**
```bash
cd RentCollection.Web
dotnet run
```

**Test upload:**
```
http://localhost:5032/Documents
```

---

**Status:** Fix scripts created, waiting for you to run them

**Estimated Time:** 2 minutes to fix

**Action Required:** Run `FixDocumentsTable.bat` or any of the methods above
