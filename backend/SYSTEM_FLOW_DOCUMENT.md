# Rent Collection System - Flow Document

## System Overview
ASP.NET Core 9 MVC application for managing rental properties, tenants, leases, invoices, and payments.

---

## User Roles

1. **Admin/Staff** - Manage properties, tenants, leases, invoices, approve payments
2. **Tenant** - View invoices, make payments, submit maintenance requests

---

## Main Flows

### 1. Property Setup Flow
```
Admin Login → Create Property → Add Units → Set Monthly Rent → Property Ready
```

### 2. Tenant Onboarding Flow
```
Create Tenant → Assign to Unit → Create Lease → Unit Status = Occupied → Ready for Invoicing
```

### 3. Invoice Generation Flow
```
Admin clicks "Generate Monthly Invoices" → System checks all active leases → Creates invoice for each lease → Invoice Status = Pending
```

### 4. Payment Flow - Cash
```
Tenant pays cash to Admin → Admin records payment → Payment Status = Completed → Invoice updated to Paid
```

### 5. Payment Flow - Bank Transfer
```
Tenant uploads transfer slip → Status = PendingVerification → Admin reviews → Approve/Reject → If Approved: Invoice = Paid
```

### 6. Tenant Portal Flow
```
Tenant Login (PACI + Password) → Dashboard → View Invoices → Make Payment → View Payment History
```

### 7. Maintenance Request Flow
```
Tenant submits request → Admin assigns to staff → Staff completes work → Request closed
```

### 8. Reporting Flow
```
Admin → Reports → Select Report Type → Apply Filters → View Grid/Export Excel
```

---

## Data Flow Summary

```
Company → Property → Unit → Lease → Tenant
                              ↓
                          Invoice → Payment (Cash/Bank Transfer)
```

---

## URLs

- **Admin Login**: http://localhost:5032/Account/Login
- **Admin Dashboard**: http://localhost:5032/Dashboard
- **Tenant Login**: http://localhost:5032/TenantPortal/Login
- **Reports**: http://localhost:5032/Reports

---

## Authentication

- **Admin**: Cookie-based (Email + Password)
- **Tenant**: Session-based (PACI Number + Password)
- **API**: JWT Token

