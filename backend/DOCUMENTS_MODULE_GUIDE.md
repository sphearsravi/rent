# Documents Module - Complete Guide

## Overview
The Documents module allows you to upload, manage, and link documents to Tenants, Properties, Leases, and Maintenance Requests.

---

## Features

### 1. Document Upload with Entity Linking
Upload documents and link them to specific entities in the system.

### 2. Document Management
View, download, and delete documents with full entity relationship information.

### 3. Entity Relationships
Documents can be linked to:
- **Tenants** - ID documents, contracts, personal files
- **Properties** - Property deeds, permits, certificates
- **Leases** - Lease agreements, addendums, signed contracts
- **Maintenance Requests** - Work orders, invoices, completion photos

---

## How to Use

### Upload a Document

1. Navigate to **Operations → Documents**
2. Click **"Upload Document"** button
3. Fill in the form:

#### Document Information Section
- **Select File**: Click to browse or drag-and-drop
- **Document Type**: Choose from dropdown
  - Lease Agreement
  - Invoice
  - Receipt
  - ID Document
  - Contract
  - Report
  - Other

#### Link to Entity Section (Optional)
- **Entity Type**: Select what to link to
  - None (document not linked to anything)
  - Tenant
  - Property
  - Lease
  - Maintenance Request

- **Select Entity**: After choosing entity type, select specific entity
  - **Tenant dropdown**: Shows "FirstName LastName - PACI Number"
  - **Property dropdown**: Shows "Property Name - PACI Number"
  - **Lease dropdown**: Shows "Tenant Name - Property Name Unit Number"
  - **Maintenance dropdown**: Shows "#RequestID - Property Name Unit Number - Category"

#### Additional Information
- **Description**: Optional notes about the document

4. Click **"Upload Document"**
5. Document is saved and linked to selected entity

---

## Document Type Examples

### For Tenants
**Document Types**: ID Document, Contract
**Examples**:
- Civil ID copy
- Passport copy
- Employment letter
- Bank statements
- Rental application

**How to Upload**:
1. Select file
2. Document Type: "ID Document"
3. Entity Type: "Tenant"
4. Select Tenant: Choose from dropdown (e.g., "Ahmed Al-Rashid - 265123456789")
5. Description: "Civil ID - Front and Back"
6. Upload

### For Properties
**Document Types**: Contract, Report, Other
**Examples**:
- Property ownership deed
- Building permits
- Safety certificates
- Insurance documents
- Property photos

**How to Upload**:
1. Select file
2. Document Type: "Contract" or "Other"
3. Entity Type: "Property"
4. Select Property: Choose from dropdown (e.g., "Salmiya Heights - 789456123")
5. Description: "Property Ownership Deed"
6. Upload

### For Leases
**Document Types**: Lease Agreement, Contract
**Examples**:
- Signed lease agreement
- Lease renewal documents
- Addendums
- Move-in inspection report
- Move-out checklist

**How to Upload**:
1. Select file
2. Document Type: "Lease Agreement"
3. Entity Type: "Lease"
4. Select Lease: Choose from dropdown (e.g., "Ahmed Al-Rashid - Salmiya Heights Unit A101")
5. Description: "Original Signed Lease - 1 Year"
6. Upload

### For Maintenance Requests
**Document Types**: Invoice, Receipt, Report
**Examples**:
- Repair invoices
- Before/after photos
- Work completion certificate
- Material receipts
- Contractor quotes

**How to Upload**:
1. Select file
2. Document Type: "Invoice"
3. Entity Type: "Maintenance Request"
4. Select Maintenance: Choose from dropdown (e.g., "#1 - Salmiya Heights Unit A101 - Plumbing")
5. Description: "Plumbing Repair Invoice"
6. Upload

---

## Viewing Documents

### Documents List View
Navigate to **Operations → Documents** to see all uploaded documents.

**Each Document Card Shows**:
- Document icon (based on file type)
- Document name
- Document type badge
- File size (formatted: KB, MB, GB)
- Upload date
- Uploaded by (user email)
- **Linked Entity** (if applicable): Shows entity type and ID in blue badge

**File Type Icons**:
- 📄 PDF - Red icon
- 📊 Excel - Green icon
- 📝 Word - Blue icon
- 🖼️ Images - Purple icon
- 📋 Text - Gray icon
- 📁 Others - Default icon

---

## Downloading Documents

1. Find document in list
2. Click **"Download"** button
3. File downloads to your browser's download folder
4. Original filename is preserved

**Supported File Types**:
- PDF (.pdf)
- Word (.doc, .docx)
- Excel (.xls, .xlsx)
- Images (.png, .jpg, .jpeg, .gif)
- Text (.txt)
- And more...

---

## Deleting Documents

1. Find document in list
2. Click **"Delete"** button
3. Confirm deletion
4. Document is removed from:
   - Database
   - File system (physical file deleted)

**Warning**: Deletion is permanent and cannot be undone!

---

## Entity Linking Benefits

### Why Link Documents to Entities?

1. **Organization**: Find all documents related to a specific tenant, property, or lease
2. **Quick Access**: View relevant documents when viewing entity details
3. **Compliance**: Keep required documents organized per tenant/property
4. **History**: Track document history for each entity
5. **Reporting**: Generate reports showing which entities have required documents

### Finding Linked Documents

**From Tenant Details Page**:
- Future feature: Will show all documents linked to this tenant

**From Property Details Page**:
- Future feature: Will show all documents for this property

**From Lease Details Page**:
- Future feature: Will show lease agreement and related documents

**From Maintenance Details Page**:
- Future feature: Will show invoices and work photos

---

## Workflow Examples

### Tenant Onboarding
1. Upload Civil ID → Link to Tenant
2. Upload Employment Letter → Link to Tenant
3. Upload Bank Statement → Link to Tenant
4. Upload Signed Lease → Link to Lease
5. Upload Move-in Inspection → Link to Lease

### Property Management
1. Upload Ownership Deed → Link to Property
2. Upload Building Permit → Link to Property
3. Upload Insurance Certificate → Link to Property
4. Upload Safety Inspection → Link to Property

### Maintenance Work Order
1. Tenant reports issue → Maintenance request created
2. Upload contractor quote → Link to Maintenance Request
3. Upload work photos → Link to Maintenance Request
4. Upload completion invoice → Link to Maintenance Request
5. Upload payment receipt → Link to Maintenance Request

### Lease Renewal
1. Upload new lease draft → Link to Lease
2. Upload signed renewal → Link to Lease
3. Upload updated rent receipt → Link to Lease

---

## File Storage

### Location
All uploaded files are stored in:
```
RentCollection.Web/wwwroot/uploads/documents/
```

### File Naming
Files are renamed upon upload to prevent conflicts:
```
{GUID}_{OriginalFileName}
Example: a1b2c3d4-5678-90ab-cdef-1234567890ab_LeaseAgreement.pdf
```

### Security
- Files are stored outside of version control
- Only authenticated users can upload/download
- File paths are not exposed in URLs
- Original filenames preserved for downloads

---

## Database Schema

### Documents Table Structure
```sql
DocumentId          INT (Primary Key)
DocumentName        NVARCHAR(255)  -- Original filename
DocumentType        NVARCHAR(100)  -- Type from dropdown
FilePath            NVARCHAR(500)  -- Server path
FileSize            BIGINT         -- Size in bytes
RelatedEntityType   NVARCHAR(50)   -- Tenant/Property/Lease/Maintenance
RelatedEntityId     INT            -- ID of related entity
Description         NVARCHAR(MAX)  -- Optional notes
UploadedBy          NVARCHAR(450)  -- User ID
UploadedDate        DATETIME2      -- Upload timestamp
```

### Foreign Keys
- `UploadedBy` → `AspNetUsers.Id`

### Indexes
- `IX_Documents_UploadedBy` - For user lookups
- `IX_Documents_RelatedEntity` - For entity relationship queries

---

## Tips and Best Practices

### Document Naming
- Use descriptive filenames before upload
- Include dates in filename (e.g., "Lease_2024_Ahmed.pdf")
- Avoid special characters

### Document Types
- Choose appropriate type for easy filtering
- Use "ID Document" for all identification papers
- Use "Lease Agreement" for all lease contracts
- Use "Invoice" for bills and invoices
- Use "Receipt" for payment confirmations

### Entity Linking
- Always link documents when possible
- Link tenant documents to both Tenant AND Lease
- Link property documents to Property
- Link maintenance documents to specific request

### Organization
- Upload documents immediately upon receiving them
- Add descriptions for context
- Delete outdated/superseded documents
- Keep only current and historical documents

### File Sizes
- Recommended: Under 10MB per file
- Large files may take longer to upload
- Compress images before uploading
- Use PDF instead of Word for final documents

---

## Testing Checklist

After running the database fix (`FixDatabaseSchema.sql`):

- [ ] Navigate to Documents page (no errors)
- [ ] Click "Upload Document" button
- [ ] Form displays with all sections
- [ ] Select file from computer
- [ ] Choose document type from dropdown
- [ ] Select "Tenant" as entity type
- [ ] Tenant dropdown appears with list
- [ ] Select a tenant from dropdown
- [ ] Add description
- [ ] Click "Upload Document"
- [ ] Success message appears
- [ ] Document appears in list
- [ ] Document shows linked entity badge
- [ ] Download button works
- [ ] File downloads with correct name
- [ ] Delete button works
- [ ] Document removed from list

---

## Troubleshooting

### Error: "Invalid object name 'Documents'"
**Solution**: Run `FixDatabaseSchema.sql` to create Documents table

### Dropdown Shows "No data"
**Solution**: Ensure you have sample data (tenants, properties, leases)

### File Upload Fails
**Possible Causes**:
1. File too large (check server limits)
2. Invalid file type
3. Disk space full
4. Permission issues on uploads folder

**Solution**: Check error message, verify file size, check server logs

### Downloaded File Doesn't Open
**Possible Causes**:
1. File corrupted during upload
2. Wrong file extension
3. Missing required software

**Solution**: Re-upload file, verify file integrity

---

## Future Enhancements

Planned features:
1. **Document Preview** - View PDFs and images without downloading
2. **Bulk Upload** - Upload multiple documents at once
3. **Document Versioning** - Keep history of document updates
4. **Entity Document List** - Show all documents on entity detail pages
5. **Required Documents Checklist** - Track required documents per tenant
6. **Document Expiry** - Alert when documents expire (e.g., IDs, permits)
7. **OCR Integration** - Extract text from scanned documents
8. **Digital Signatures** - Sign documents electronically

---

**Module Status**: ✅ Fully Functional
**Last Updated**: November 2, 2025
**Database Required**: Documents table (created by FixDatabaseSchema.sql)
