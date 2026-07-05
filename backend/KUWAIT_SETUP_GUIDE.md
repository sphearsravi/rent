# Kuwait Rent Collection System - Complete Setup Guide

## Step 1: Run Database Migrations

Execute these SQL scripts in order on your SQL Server (`RAVIWST\SQLEXPRESS`):

### 1.1 Add PACI Number Fields
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddPACINumbers.sql"
```

### 1.2 Add Kuwait Sample Data
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\AddKuwaitSampleData.sql"
```

**Or use SQL Server Management Studio:**
1. Open SSMS and connect to `RAVIWST\SQLEXPRESS`
2. Open and execute `AddPACINumbers.sql`
3. Open and execute `AddKuwaitSampleData.sql`

## Step 2: Start the Application

```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
```

Open browser: `http://localhost:5032`

## Sample Data Overview

### 5 Kuwait Properties:
1. **Salmiya Heights** - 24 units, Salmiya (PACI: 789456123)
2. **Fintas Towers** - 30 units, Fintas (PACI: 987654321)
3. **Jabriya Complex** - 18 units, Jabriya (PACI: 456789123)
4. **Sharq Business Center** - 12 commercial units, Sharq (PACI: 147258369)
5. **Mangaf Residences** - 20 units, Mangaf (PACI: 369258147)

### 11 Tenants (Including Multiple Leases):
- **Ahmed Al-Rashid** - 2 leases (A101 + J-102) - PACI: 265123456789
- **Sarah Al-Mutairi** - 2 leases (A102 + J-101) - PACI: 265234567890
- **Khalid Al-Faris** - 2 leases (T1-101 + J-201) - PACI: 265567890123
- **Mohammed Al-Sabah** - 2 leases (A201 + M-101) - PACI: 265345678901
- **Fatima Al-Ahmad** - 2 leases (A301 + M-102) - PACI: 265456789012
- **Layla Al-Hassan** - 2 leases (T1-201 + M-201) - PACI: 265678901234
- **Omar Al-Zaid** - 1 lease (T1-301) - PACI: 265789012345
- **Noura Al-Kandari** - 1 lease (T2-201) - PACI: 265890123456
- **TechCorp Kuwait** - 1 commercial lease (Office-101) - PACI: 265901234567
- **Green Solutions LLC** - 1 commercial lease (Office-201) - PACI: 265012345678
- **Consulting Plus WLL** - 1 commercial lease (Office-301) - PACI: 265123456780

### 17 Active Leases
All with Kuwait-style rent amounts in KWD (3 decimal places)

### Payment History
Multiple payments for various tenants showing 3-month history

### 6 Maintenance Requests
Various statuses: Open, In Progress, Completed

## Features to Test

### ✅ Dashboard
- View total revenue in KWD
- See property and tenant statistics
- View recent payments
- Check upcoming lease expirations

### ✅ Properties Module
- **List**: View all 5 Kuwait properties with PACI numbers
- **Create**: Add new property with PACI number field
- **Edit**: Update property information
- **Details**: View property details, units, occupancy
- **Delete**: Remove property (with warnings)

### ✅ Tenants Module
- **List**: View all tenants with PACI numbers
- **Create**: Add new tenant with PACI number
- **Edit**: Update tenant information
- **Details**: View tenant details, see MULTIPLE leases per tenant
- **Delete**: Remove tenant

### ✅ Leases Module
- **List**: View all 17 active leases
- **Create**: Create new lease
- **Edit**: Update lease terms
- **Details**: View complete lease information
- **Delete**: Remove lease
- **Note**: Verify tenants can have multiple leases (Sarah, Ahmed, Khalid, Mohammed, Fatima, Layla all have 2 leases)

### ✅ Payments Module
- **List**: View all payments in KWD format (3 decimals)
- Payment summary cards showing totals in KWD

### ✅ Maintenance Module
- **List**: View all 6 maintenance requests with status badges
- **Create**: Submit new maintenance request
- **Edit**: Update request status, add completion info
- **Details**: View full request details
- **Delete**: Remove request
- Test priority levels: High, Medium, Low
- Test statuses: Open, In Progress, Completed, Cancelled

### ✅ Documents Module
- **List**: View all uploaded documents
- **Upload**: Upload new documents (supports multiple file types)
  - Document types: Lease Agreement, Invoice, Receipt, ID Document, Contract, Report, Other
  - Link to entities: Property, Tenant, Lease, Payment, Maintenance
- **Download**: Download documents
- **Delete**: Remove documents

### ✅ Reports Module (Excel Export)
- **Tenant Report**: Export all tenants with PACI numbers to Excel
- **Property Report**: Export properties with values in KWD
- **Lease Report**: Export leases with rent amounts in KWD
- **Payment Report**: Export payment history in KWD
- **Maintenance Report**: Export maintenance requests with costs in KWD

All Excel exports include:
- Professional formatting with blue headers
- KWD currency with 3 decimal places
- Auto-fitted columns
- Timestamped filenames

## Kuwait-Specific Features Implemented

### 1. Currency Format
- All amounts display as: **KWD 1,234.560** (3 decimal places)
- Dashboard, Properties, Tenants, Leases, Payments all use KWD
- Excel exports format currency correctly

### 2. PACI Numbers
- **Properties**: Each property has a PACI number field
- **Tenants**: Each tenant has a PACI number field
- Visible in Create, Edit, Details, and Delete views
- Included in Excel reports

### 3. Multiple Leases per Tenant
- Tenants can lease multiple apartments/commercial spaces
- Database supports one-to-many relationship (Tenant → Leases)
- Sample data shows 6 tenants with 2 active leases each
- Tenant details view shows complete lease history

## Navigation Menu Structure

### Main Menu
- Dashboard
- Properties
- Tenants
- Leases

### Financial
- Payments
- Invoices (placeholder)
- Reports (with Excel export)

### Operations
- Maintenance
- Documents

### Settings
- Company (placeholder)
- Users (placeholder)

## Testing Checklist

- [ ] Dashboard loads and shows KWD amounts correctly
- [ ] Properties: Create, Edit, Delete, Details all work
- [ ] Properties show PACI numbers in all views
- [ ] Tenants: Create, Edit, Delete, Details all work
- [ ] Tenants show PACI numbers in all views
- [ ] Tenant Details shows multiple leases (test with Sarah, Ahmed, or Khalid)
- [ ] Leases: Create, Edit, Delete, Details all work
- [ ] All currency displays show KWD with 3 decimals
- [ ] Payments list displays correctly in KWD
- [ ] Maintenance: Create, Edit, Delete, Details all work
- [ ] Maintenance status badges work (Open=yellow, In Progress=blue, Completed=green)
- [ ] Documents: Upload file works
- [ ] Documents: Download file works
- [ ] Reports: Export Tenant Report to Excel
- [ ] Reports: Export Property Report to Excel
- [ ] Reports: Export Lease Report to Excel
- [ ] Reports: Export Payment Report to Excel
- [ ] Reports: Export Maintenance Report to Excel
- [ ] Excel files open correctly with proper formatting
- [ ] Excel currency shows as KWD with 3 decimals
- [ ] All navigation menu items work

## Known Placeholders

These pages are linked in the menu but not yet implemented:
- Invoices (Financial section)
- Company Settings
- Users Management

## Troubleshooting

### Database Connection Issues
- Verify SQL Server is running: `services.msc` → "SQL Server (SQLEXPRESS)"
- Check connection string in `appsettings.json` is set to `RAVIWST\SQLEXPRESS`

### Sample Data Not Showing
- Ensure both SQL scripts were executed successfully
- Check for error messages in SQL output
- Verify database has data: `SELECT COUNT(*) FROM Properties`

### Excel Export Not Working
- Verify EPPlus package is installed (should be added during build)
- Check file downloads to browser's download folder

### Pages Return 404
- Ensure application is running on port 5032
- Navigate to `http://localhost:5032` (not https)
- Check that controller and view files exist

## Default Login Credentials

**Email:** admin@rentcollection.com
**Password:** Admin@123

**IMPORTANT:** Change this password after first login!

---

**System Version:** 1.0 Kuwait Edition
**Last Updated:** November 2, 2025
**Currency:** Kuwaiti Dinar (KWD)
**Database:** SQL Server 2019+
