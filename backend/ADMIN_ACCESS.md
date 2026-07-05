# Admin Panel Access Guide

## 🔐 Admin Login

### URL
**http://localhost:5032/Account/Login**

### Admin Credentials
- **Username**: `admin` OR `admin@test.com`
- **Password**: Try these common defaults:
  - `Admin@123`
  - `admin123`
  - `password`
  - `Admin@2024`

### If Password Doesn't Work

Run this SQL to reset admin password to `Admin@123`:

```sql
-- This will set admin password to: Admin@123
UPDATE Users
SET PasswordHash = 'HqKyIzJ4P/qpftqKEMD3XA==:xqRmUwIEeQwMZfB7dF43g2rxpGqDRHDEY7KKQs2IW8g='
WHERE UserId = 1;
```

Or use the password reset utility:
**http://localhost:5032/Account/ResetAdminPassword**

---

## 📊 Admin Dashboard

After login, you'll be redirected to:
**http://localhost:5032/Dashboard/Index**

This is the main admin dashboard showing:
- Total properties
- Total tenants
- Total leases
- Revenue overview
- Recent payments
- Maintenance requests

---

## 💰 Bank Transfer Management (NEW)

### View Pending Bank Transfers
**URL**: http://localhost:5032/Payments/BankTransfers

Features:
- List all pending bank transfers
- View transfer details
- See uploaded proof documents
- Approve or reject transfers
- Filter by property/tenant
- Search by reference number

### Approve/Reject Workflow
1. Click on a pending bank transfer
2. View the proof document
3. Verify the details
4. Click "Approve" or "Reject"
5. Invoice automatically updates when approved

---

## 🏢 All Admin Pages

### Dashboard & Reports
- Dashboard: http://localhost:5032/Dashboard/Index
- Reports: http://localhost:5032/Reports/Index

### Property Management
- Properties: http://localhost:5032/Properties/Index
- Units: http://localhost:5032/Units/Index

### Tenant Management
- Tenants: http://localhost:5032/Tenants/Index
- Companies: http://localhost:5032/Companies/Index

### Lease Management
- Leases: http://localhost:5032/Leases/Index
- Invoices: http://localhost:5032/Invoices/Index

### Payment Management
- Payments: http://localhost:5032/Payments/Index
- **Bank Transfers** (NEW): http://localhost:5032/Payments/BankTransfers
- **Cash Payments** (NEW): http://localhost:5032/Payments/Cash

### Maintenance
- Maintenance Requests: http://localhost:5032/Maintenance/Index

### System
- Users: http://localhost:5032/Users/Index
- Documents: http://localhost:5032/Documents/Index

---

## 🆕 Testing Bank Transfer Approval

### Step 1: Tenant Submits Transfer
1. Login as tenant (265234567890 / Welcome@123)
2. Go to: http://localhost:5032/TenantPortal/MakePaymentSimple
3. Click "Pay Now" on an invoice
4. Fill bank transfer form and submit

### Step 2: Admin Reviews Transfer
1. Logout from tenant portal
2. Login as admin: http://localhost:5032/Account/Login
3. Go to: http://localhost:5032/Payments/BankTransfers
4. See the pending transfer in the list
5. Click "View Details"
6. Download/view the proof document
7. Click "Approve" or "Reject"

### Step 3: Verify Results
1. Check that invoice status updated to "Paid" (if approved)
2. Check payment record created
3. Tenant can see updated status in payment history

---

## 🔧 Quick Admin Tasks

### Create Cash Payment (Manual Entry)
1. Go to: http://localhost:5032/Payments/Cash/Create
2. Select property and tenant
3. Enter amount and receipt details
4. Submit

### View All Payments
1. Go to: http://localhost:5032/Payments/Index
2. Filter by:
   - Payment type (KNET, Cash, Bank Transfer)
   - Status (Pending, Approved, Rejected)
   - Date range
   - Property
   - Tenant

### Generate Reports
1. Go to: http://localhost:5032/Reports/Index
2. Select report type:
   - Revenue Report
   - Outstanding Payments
   - Bank Transfer Summary
   - Cash Payment Log

---

## 👤 Current Admin User

**User ID**: 1
**Username**: admin
**Email**: admin@test.com
**Role**: Admin
**Status**: Active
**Last Login**: 2025-11-10 05:15:02

---

## 🚀 Quick Start

1. **Login**: http://localhost:5032/Account/Login
   - Email: `admin@test.com`
   - Password: Try `Admin@123` first

2. **View Dashboard**: Should redirect automatically

3. **Check Bank Transfers**: http://localhost:5032/Payments/BankTransfers

4. **If Login Fails**:
   - Run SQL to reset password (see above)
   - Or contact system administrator
