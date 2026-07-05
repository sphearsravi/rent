# RentCollectionSystem - Business Workflows

## Table of Contents

1. [Tenant Login](#1-tenant-login)
2. [KNET Payment](#2-knet-payment)
3. [Bank Transfer Payment](#3-bank-transfer-payment)
4. [Cash Payment](#4-cash-payment)
5. [Payment Approval](#5-payment-approval)
6. [Invoice Generation](#6-invoice-generation)
7. [Maintenance Requests](#7-maintenance-requests)
8. [Payment Verification & Mismatch Detection](#8-payment-verification--mismatch-detection)
9. [Admin Dashboard](#9-admin-dashboard)
10. [Tenant Portal Dashboard](#10-tenant-portal-dashboard)

---

## 1. Tenant Login

**Entry Point:** `/TenantPortal/Login`
**Auth Method:** Civil ID + Password (BCrypt)
**Session:** 2-hour timeout, optional 30-day "Remember Me" cookie

```
┌──────────────────────────────────────────────────────┐
│                  TENANT LOGIN FLOW                    │
└──────────────────────────────────────────────────────┘

  Browser → GET /TenantPortal/Login
       │
       ├─ Session has TenantId? ──YES──→ Redirect to Dashboard
       │
       ├─ "TenantRememberMe" cookie? ──YES──→ Validate tenant
       │       │                                    │
       │       │                              Active? → Auto-login
       │       │                              Expired? → Delete cookie
       │
       └─ Show login form
              │
        POST /TenantPortal/Login
              │
        ┌─────┴─────┐
        │ Validate   │
        │ Civil ID   │──EMPTY──→ "Please enter your Civil ID"
        │ + Password │──EMPTY──→ "Please enter your password"
        └─────┬─────┘
              │
        Query: Tenant WHERE GovernmentId = CivilId AND Status = "Active"
              │
        ┌─────┴─────┐
        │ Not Found  │──→ "Invalid Civil ID or password"
        └─────┬─────┘
              │
        BCrypt.Verify(password, storedHash)
              │
        ┌─────┴─────┐
        │  Failed    │──→ "Invalid Civil ID or password"
        └─────┬─────┘
              │
        ✓ Set Session: TenantId, TenantName
              │
        ┌─────┴──────────┐
        │ Remember Me?    │
        ├─ YES → Set 30-day HttpOnly cookie (TenantRememberMe)
        └─ NO  → Delete cookie if exists
              │
        Redirect → /TenantPortal/Dashboard
```

**API Login (JWT):** `POST /api/auth/login` returns JWT token + all active leases for the tenant.

---

## 2. KNET Payment

**Entry Point:** Click "Pay with KNET" on MakePayment page
**Gateway:** `kpaytest.com.kw` (sandbox) / `kpay.com.kw` (production)
**Encryption:** AES-128-CBC with TerminalResourceKey

```
┌──────────────────────────────────────────────────────┐
│                 KNET PAYMENT FLOW                     │
└──────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────┐
  │  PHASE 1: INITIATION                            │
  │  POST /api/payments/knet/initiate               │
  └─────────────────────────────────────────────────┘

  Tenant clicks "Pay with KNET"
       │
  Frontend: fetch('/api/payments/knet/initiate', {
       invoiceId, tenantId, propertyId, amount, language
  })
       │
  ┌────┴────┐
  │ Backend │ KnetPaymentService.InitiatePaymentAsync()
  └────┬────┘
       │
  1. Idempotency Check
       ├─ Key exists & not expired? → Return existing PaymentId
       └─ Continue
       │
  2. Validate Invoice
       ├─ Invoice not found? → Error
       └─ Continue
       │
  3. Validate Building
       ├─ Building not found? → Error
       ├─ AcceptKNET = false? → Error
       └─ Continue
       │
  4. Mismatch Detection
       ├─ CivilID matches tenant? → OK
       ├─ PropertyId matches lease? → OK
       └─ Mismatch? → Flag, create PaymentAudit record
       │
  5. Generate TrackId: "INV-{invoiceId}-{yyyyMMddHHmmss}"
       │
  6. Resolve KNET Credentials
       ├─ Building has own credentials? → Use building's
       └─ Otherwise → Use default from appsettings.json
       │
  7. Test Mode?
       ├─ YES → Create Payment(Pending), redirect to /TenantPortal/TestPayment
       └─ NO  → Continue to real KNET
       │
  8. Encrypt params with AES-128-CBC
       │   cleartext: id, password, action, langid, currencycode, amt,
       │              responseURL, errorURL, trackid, udf1-5
       │   → raw encrypted bytes → uppercase HEX string
       │
  9. Build redirect URL:
       │   {paymentApiUrl}&trandata={hexEncrypted}&tranportalId={id}
       │   &responseURL={url}&errorURL={url}
       │
  10. Create Payment record (Status = Pending)
       │
  11. Create PaymentIdempotency record (24hr expiry)
       │
  12. Return { success: true, redirectUrl: "https://kpaytest.com.kw/..." }


  ┌─────────────────────────────────────────────────┐
  │  PHASE 2: KNET GATEWAY                          │
  │  Browser redirects to kpaytest.com.kw           │
  └─────────────────────────────────────────────────┘

  Frontend: window.location.href = redirectUrl
       │
  ┌────┴─────────────────┐
  │  KNET Payment Page   │
  │  Tenant enters card  │
  │  details & confirms  │
  └────┬─────────────────┘
       │
  ┌────┴────┐
  │ KNET    │ Processes card charge
  │ Gateway │
  └────┬────┘
       │
  ┌────┴─────────────┐
  │ CAPTURED?        │
  ├─ YES → POST to responseURL (success callback)
  └─ NO  → POST to errorURL (error callback)


  ┌─────────────────────────────────────────────────┐
  │  PHASE 3: CALLBACK HANDLING                     │
  │  POST /api/payments/knet/response               │
  └─────────────────────────────────────────────────┘

  KNET POSTs encrypted trandata to our server
       │
  KnetPaymentController.KnetResponse()
       │
  1. Decrypt trandata using TerminalResourceKey
       │   HEX → raw bytes → AES decrypt → cleartext params
       │
  2. Parse params: result, paymentid, tranid, trackid, auth, ref
       │
  3. Find Payment by trackId
       │
  4. Evaluate result
       │
  ┌────┴──────────────────────────────────┐
  │                                       │
  result = "CAPTURED"              result != "CAPTURED"
       │                                  │
  Payment.Status = Completed        Payment.Status = Failed
  Payment.ApprovedDate = now        Notes += error details
  Invoice.PaidAmount += amount
       │                                  │
  PaidAmount >= Total?                    │
  ├─ YES → Invoice.Status = "Paid"       │
  └─ NO  → "Partially Paid"              │
       │                                  │
  ┌────┴──────────────────────────────────┘
  │
  Redirect to:
  ├─ Success → /TenantPortal/PaymentSuccess?paymentId=X&reference=Y
  └─ Failure → /TenantPortal/PaymentFailed?error=Z&reference=Y
```

### Test Mode Flow

When `UseTestMode: true`, instead of redirecting to KNET gateway:

```
  Payment record created (Pending)
       │
  Redirect to /TenantPortal/TestPayment
       │
  ┌────┴──────────────────┐
  │  Test Payment Page    │
  │  Simulated KNET UI    │
  │                       │
  │  [Simulate Success]   │──→ POST /api/payments/knet/test/complete (success=true)
  │  [Simulate Failure]   │──→ POST /api/payments/knet/test/complete (success=false)
  └───────────────────────┘
       │
  CompleteTestPaymentAsync()
       │
  Update Payment & Invoice same as real flow
       │
  Redirect to PaymentSuccess / PaymentFailed
```

### Multi-Building KNET Credentials

```
  Building (Property entity)
  ├── KnetTranportalId      ─┐
  ├── KnetTranportalPwd      ├─ Building-specific (optional)
  └── KnetTerminalResourceKey┘
                               │
  appsettings.json Knet section─┐
  ├── TranportalId              ├─ Default fallback
  ├── TranportalPwd             │
  └── TerminalResourceKey      ─┘
                               │
  Resolution: Building fields not empty? Use building's. Otherwise use default.
```

---

## 3. Bank Transfer Payment

**Entry Point:** Tenant portal MakePayment page → "Bank Transfer" option
**Proof Required:** JPG, PNG, or PDF (max 5MB)

```
┌──────────────────────────────────────────────────────┐
│             BANK TRANSFER PAYMENT FLOW                │
└──────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────┐
  │  PHASE 1: TENANT SUBMISSION                     │
  │  POST /api/payments/bank/upload                 │
  └─────────────────────────────────────────────────┘

  Tenant fills bank transfer form:
  - Bank name, transfer date, reference number
  - Amount, notes
  - Upload proof file (transfer slip image/PDF)
       │
  1. Validate file
       ├─ Extension not in [.jpg, .jpeg, .png, .pdf]? → Error
       ├─ Size > 5MB? → Error
       └─ OK
       │
  2. Validate invoice
       ├─ Not found? → Error
       ├─ Already paid? → Error
       └─ OK
       │
  3. Save proof file to: uploads/BankTransfers/{TenantId}/{filename}
       │
  4. Create Payment record
       │   PaymentType = BankTransfer
       │   Status = PendingVerification
       │
  5. Create BankTransfer record
       │   ProofFilePath, ProofFileName
       │   Status = PendingVerification
       │   SubmittedBy = tenant name
       │
  6. Update Invoice.Status = "Pending Verification"
       │
  Return: { bankTransferId, paymentId, status: "PendingVerification" }


  ┌─────────────────────────────────────────────────┐
  │  PHASE 2: ADMIN REVIEW                          │
  │  /PaymentApproval                               │
  └─────────────────────────────────────────────────┘

  Admin sees pending bank transfer in PaymentApproval/Index
       │
  Admin reviews:
  - Transfer details (bank, date, reference, amount)
  - Downloads proof file
  - Verifies against bank statement
       │
  ┌────┴──────────────────────────────────┐
  │                                       │
  [Approve]                          [Reject]
       │                                  │
  BankTransfer.Status = Approved    BankTransfer.Status = Rejected
  Payment.Status = Completed        BankTransfer.RejectionReason = "..."
  Payment.ApprovedBy = admin        Payment.Status = Rejected
  Payment.ReceiptNumber generated   Invoice.Status → "Pending"
       │                            (tenant can resubmit)
  Invoice.PaidAmount += amount
  Invoice.Status = "Paid" or "Partially Paid"
```

---

## 4. Cash Payment

**Entry Point:** Admin records cash collected by field staff
**Receipt:** Auto-generated in format `CASH-{yyyyMMdd}-{count:D5}`

```
┌──────────────────────────────────────────────────────┐
│                CASH PAYMENT FLOW                      │
└──────────────────────────────────────────────────────┘

  Field staff collects cash from tenant
       │
  Admin records in system:
  - Tenant, Property, Invoice
  - Amount, payment date
  - Collector name, notes
       │
  RecordCashPaymentAsync()
       │
  1. Validate invoice (exists, not already paid)
       │
  2. Generate receipt: "CASH-20260212-00001"
       │
  3. Create Payment record
       │   PaymentType = Cash
       │   Status = PendingVerification
       │   CollectedBy = field staff name
       │   ReceiptNumber = generated receipt
       │
  4. Create CashPayment record
       │   Amount, PaymentDate, CollectedBy
       │   ReceiptNumber, Notes
       │
  5. Invoice.Status = "Pending Verification"
       │
  ┌────┴──────────────────────────────────┐
  │  Awaits admin approval                │
  │  (same as Bank Transfer Phase 2)      │
  │                                       │
  │  Approve → Completed, Invoice updated │
  │  Reject  → Rejected                   │
  └───────────────────────────────────────┘
```

---

## 5. Payment Approval

**Entry Point:** `/PaymentApproval` (Admin only)
**Applies to:** Cash payments and Bank transfers (not KNET - auto-processed)

```
┌──────────────────────────────────────────────────────┐
│              PAYMENT APPROVAL FLOW                    │
└──────────────────────────────────────────────────────┘

  /PaymentApproval/Index
       │
  Query: Payments WHERE Status = PendingVerification
         AND PaymentType IN (Cash, BankTransfer)
       │
  Display list with:
  - Pending count, Approved today, Rejected today
  - Tenant name, method, amount, date
       │
  Admin clicks a payment → /PaymentApproval/Details/{id}
       │
  Shows full details:
  - Bank Transfer: proof file download, bank name, reference
  - Cash: collector name, receipt number
  - Invoice details, tenant info
       │
  ┌────┴──────────────────────────────────┐
  │                                       │
  [Approve]                          [Reject + Reason]
       │                                  │
  Payment                           Payment
  ├─ Status = Completed             ├─ Status = Rejected
  ├─ ApprovedBy = admin             ├─ Notes += "[Rejected by {admin}]
  ├─ ApprovedDate = now             │   Reason: {reason}"
  │                                 │
  BankTransfer (if applicable)      BankTransfer (if applicable)
  ├─ Status = Approved              ├─ Status = Rejected
  ├─ ApprovedBy = admin             ├─ RejectionReason = reason
  │                                 │
  Invoice                           Invoice
  ├─ PaidAmount += payment amount   └─ Status unchanged (stays Pending)
  ├─ PaidAmount >= Total?               Tenant can resubmit
  │  ├─ YES → Status = "Paid"
  │  └─ NO  → Status = "PartiallyPaid"
  │
  Success message → Redirect to list
```

---

## 6. Invoice Generation

**Entry Point:** `/Invoices/GenerateMonthlyInvoices` (Admin action)
**Frequency:** Monthly, triggered manually

```
┌──────────────────────────────────────────────────────┐
│             INVOICE GENERATION FLOW                   │
└──────────────────────────────────────────────────────┘

  Admin clicks "Generate Monthly Invoices"
       │
  1. Determine period: current month + year
       │
  2. Fetch all leases WHERE Status = "Active"
       │
  3. For EACH active lease:
       │
       ├─ Invoice already exists for this lease + month?
       │   ├─ YES → Skip (prevent duplicates)
       │   └─ NO  → Continue
       │
       ├─ Calculate due date:
       │   └─ Use lease.PaymentDueDay (default: 1st of month)
       │       If day > days in month → use last day
       │
       └─ Create Invoice:
            InvoiceDate = now
            DueDate = calculated date
            TotalAmount = lease.MonthlyRent
            PaidAmount = 0
            Status = "Pending"
            InvoiceType = "Rent"
            Notes = "Monthly rent for {Month} {Year} - Unit {number}"
       │
  4. Bulk save all invoices
       │
  Success: "Generated {count} invoice(s) for {Month} {Year}"


  INVOICE STATUS LIFECYCLE:
  ┌─────────┐    ┌──────────────────┐    ┌────────────────┐    ┌──────┐
  │ Pending  │───→│ Pending          │───→│ Partially Paid │───→│ Paid │
  │(created) │    │ Verification     │    │ (partial amt)  │    │      │
  └─────────┘    │(payment submitted)│    └────────────────┘    └──────┘
       │         └──────────────────┘
       │                                   ┌─────────┐
       └── DueDate passed? ──────────────→ │ Overdue │
                                           └─────────┘
```

---

## 7. Maintenance Requests

**Entry Points:**
- Tenant: `/TenantPortal/Dashboard` → "Request Maintenance"
- Admin: `/Maintenance/Create`

```
┌──────────────────────────────────────────────────────┐
│            MAINTENANCE REQUEST FLOW                   │
└──────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────┐
  │  TENANT SUBMISSION                              │
  └─────────────────────────────────────────────────┘

  Tenant fills form:
  - Select lease (determines property + unit)
  - Category: Plumbing, Electrical, HVAC, General, etc.
  - Priority: High, Medium, Low
  - Description of issue
       │
  POST /TenantPortal/CreateMaintenanceRequest
       │
  Validate: category, priority, description required
       │
  Resolve: Lease → Unit → Property
       │
  Create MaintenanceRequest:
       PropertyId, UnitId, TenantId
       Category, Priority, Description
       Status = "Open"
       RequestDate = now
       │
  "Maintenance request submitted successfully!"
       │
  Redirect to Dashboard


  ┌─────────────────────────────────────────────────┐
  │  ADMIN MANAGEMENT                               │
  │  /Maintenance                                   │
  └─────────────────────────────────────────────────┘

  Admin views list of all requests (sorted by date)
       │
  Admin can:
  ├─ Assign to staff member (AssignedToUserId)
  ├─ Update status
  ├─ Set estimated cost
  ├─ Add notes
  └─ Record completion
       │

  STATUS LIFECYCLE:
  ┌──────┐     ┌─────────────┐     ┌───────────┐
  │ Open │────→│ In Progress │────→│ Completed │
  └──────┘     └─────────────┘     └───────────┘
     │                                    │
     │         ┌───────────┐              │
     └────────→│ Cancelled │              │
               └───────────┘              │
                                          │
                                   Record:
                                   - CompletedDate
                                   - ActualCost
                                   - Resolution notes
```

---

## 8. Payment Verification & Mismatch Detection

**Entry Point:** `/Payments` (Admin) → Mismatches tab
**Purpose:** Detect suspicious payments where payer details don't match tenant/building records

```
┌──────────────────────────────────────────────────────┐
│         PAYMENT VERIFICATION FLOW                     │
└──────────────────────────────────────────────────────┘

  ┌─────────────────────────────────────────────────┐
  │  AUTOMATIC MISMATCH DETECTION                   │
  │  (runs during payment initiation)               │
  └─────────────────────────────────────────────────┘

  During InitiatePaymentAsync(), system checks:
       │
  1. CIVIL ID MISMATCH
       │   Payment.CivilID != Lease.Tenant.GovernmentId?
       │   (Someone other than the registered tenant is paying)
       │
  2. BUILDING MISMATCH
       │   Payment.PropertyId != Lease.Unit.PropertyId?
       │   (Payment routed to wrong building)
       │
  3. KNET MID MISMATCH
       │   Payment.KNETMIDUsed != Building.KNETMID?
       │   (KNET merchant credentials don't match building config)
       │
  4. DUPLICATE DETECTION
       │   Idempotency key already exists and not expired?
       │   (Possible double-charge)
       │
  If ANY mismatch detected:
       ├─ Payment.HasMismatch = true
       ├─ Payment.MismatchDetails = description
       └─ Create PaymentAudit record:
            AuditType = "MismatchDetected"
            IsMismatch = true
            MismatchType = type
            Expected vs Actual values
            IsResolved = false


  ┌─────────────────────────────────────────────────┐
  │  ADMIN REVIEW & RESOLUTION                      │
  │  /Payments/Mismatches                           │
  └─────────────────────────────────────────────────┘

  Admin views /Payments → Mismatches tab
       │
  Filters available:
  - By Civil ID, Lease, Property, KNET MID
  - By date range, payment status
  - Mismatched only toggle
       │
  For each mismatched payment:
  - Shows expected vs actual values
  - Civil ID: Tenant's ID vs payer's ID
  - Building: Lease's building vs payment building
  - KNET: Configured MID vs used MID
       │
  Admin reviews and clicks "Resolve"
       │
  ResolveMismatchAsync():
  1. Payment.HasMismatch = false
  2. Prepend resolution note to MismatchDetails
  3. Update all unresolved PaymentAudit records:
       IsResolved = true
       ResolvedBy = admin
       ResolvedDate = now
       ResolutionNotes = notes
  4. Create new PaymentAudit:
       AuditType = "MismatchResolved"
       Notes = "Resolved by {admin}. Notes: {notes}"


  ┌─────────────────────────────────────────────────┐
  │  RECONCILIATION SUMMARY                         │
  │  /Payments/Reconciliation                       │
  └─────────────────────────────────────────────────┘

  Admin views reconciliation report:
       │
  Summary totals:
  - Total payments / amount (by period)
  - Completed / Pending / Failed counts + amounts
  - Mismatched payments count + amount
  - Unresolved vs resolved mismatches
       │
  Grouped by:
  - Building (PaymentCount, TotalAmount, SettledAmount, MismatchCount)
  - KNET Account (PaymentCount, TotalAmount, which buildings used it)
```

### Audit Trail

```
  Every payment event creates a PaymentAudit record:

  ┌──────────────────┬──────────────────────────────────┐
  │ AuditType        │ When Created                     │
  ├──────────────────┼──────────────────────────────────┤
  │ Created          │ Payment first created             │
  │ Updated          │ Payment status changed            │
  │ MismatchDetected │ Mismatch found during initiation │
  │ MismatchResolved │ Admin resolves the mismatch      │
  │ Duplicate        │ Duplicate payment detected        │
  │ Reconciled       │ KNET settlement matched           │
  └──────────────────┴──────────────────────────────────┘
```

---

## 9. Admin Dashboard

**Entry Point:** `/Dashboard` (default route after admin login)

```
┌──────────────────────────────────────────────────────┐
│               ADMIN DASHBOARD                         │
└──────────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  KPI CARDS (Top Row)                     │
  ├──────────────────────────────────────────┤
  │                                          │
  │  Total Properties    Total Units         │
  │  [count]             [count]             │
  │                                          │
  │  Occupied Units      Occupancy Rate      │
  │  [count]             [occupied/total %]  │
  │                                          │
  │  Active Tenants      Monthly Revenue     │
  │  [count]             [sum this month]    │
  │                                          │
  │  Pending Payments    Open Maintenance    │
  │  [outstanding KWD]   [open + in-progress]│
  │                                          │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  RECENT PAYMENTS (Last 5)                │
  ├──────────────────────────────────────────┤
  │  Tenant | Building | Amount | Date       │
  │  ─────────────────────────────────────── │
  │  Ahmed  | Jabriya  | 300 KWD | Today     │
  │  ...                                     │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  EXPIRING LEASES (Next 30 Days, Top 5)   │
  ├──────────────────────────────────────────┤
  │  Tenant | Unit | Building | Expires      │
  │  ─────────────────────────────────────── │
  │  Ali    | 201  | Salmiya  | Feb 28       │
  │  ...                                     │
  └──────────────────────────────────────────┘

  Data Sources:
  - Properties: COUNT(Properties)
  - Units: COUNT(Units), COUNT WHERE Status="Occupied"
  - Tenants: COUNT WHERE Status="Active"
  - Revenue: SUM(Payments.AmountPaid) WHERE month=current
  - Pending: SUM(Invoice.TotalAmount - Invoice.PaidAmount) WHERE Status="Pending"
  - Maintenance: COUNT WHERE Status IN ("Open","In Progress")
```

---

## 10. Tenant Portal Dashboard

**Entry Point:** `/TenantPortal/Dashboard` (after tenant login)

```
┌──────────────────────────────────────────────────────┐
│             TENANT PORTAL DASHBOARD                   │
└──────────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  Welcome, {TenantName}!                  │
  ├──────────────────────────────────────────┤
  │                                          │
  │  ┌────────┐  ┌────────┐  ┌────────┐     │
  │  │ Active │  │ Owed   │  │ Maint. │     │
  │  │ Leases │  │ Balance│  │Requests│     │
  │  │  [2]   │  │ 600KWD │  │  [1]   │     │
  │  └────────┘  └────────┘  └────────┘     │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  MY LEASES                               │
  ├──────────────────────────────────────────┤
  │  ┌─────────────────────────────────────┐ │
  │  │ Jabriya Complex - Unit 301         │ │
  │  │ Rent: 300 KWD/month                │ │
  │  │ Lease: Jan 2025 - Dec 2025         │ │
  │  │ Outstanding: 300 KWD               │ │
  │  │ [Make Payment]                     │ │
  │  └─────────────────────────────────────┘ │
  │  ┌─────────────────────────────────────┐ │
  │  │ Salmiya Tower - Unit 102           │ │
  │  │ Rent: 450 KWD/month                │ │
  │  │ Lease: Mar 2025 - Feb 2026         │ │
  │  │ Outstanding: 0 KWD                 │ │
  │  └─────────────────────────────────────┘ │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  RECENT PAYMENTS (Last 10)               │
  ├──────────────────────────────────────────┤
  │  Date | Building | Amount | Status       │
  │  ─────────────────────────────────────── │
  │  Feb 10 | Jabriya | 300 KWD | Completed  │
  │  Jan 15 | Salmiya | 450 KWD | Completed  │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  PENDING MAINTENANCE                     │
  ├──────────────────────────────────────────┤
  │  #12 | Plumbing | Jabriya Unit 301      │
  │       Status: Open | Priority: High      │
  └──────────────────────────────────────────┘

  ┌──────────────────────────────────────────┐
  │  QUICK ACTIONS                           │
  ├──────────────────────────────────────────┤
  │  [Make Payment] [Request Maintenance]    │
  │  [View Details] [Payment History]        │
  └──────────────────────────────────────────┘

  Data Sources:
  - Leases: WHERE TenantId=session AND Status="Active"
  - Payments: WHERE Lease.TenantId=session, ORDER BY date DESC, TOP 10
  - Maintenance: WHERE TenantId=session AND Status NOT IN ("Completed","Cancelled")
  - Documents: WHERE RelatedEntityType="Lease" AND LeaseId IN (active leases)
```

---

## Payment Status Reference

| Status | Meaning | Set By |
|--------|---------|--------|
| **Pending** | Payment initiated, awaiting processing | System (KNET initiation) |
| **PendingVerification** | Payment submitted, awaiting admin review | System (Cash/Bank submit) |
| **Completed** | Payment successful and confirmed | KNET callback / Admin approval |
| **Failed** | Payment failed at gateway | KNET callback |
| **Cancelled** | Payment cancelled by user/admin | Manual |
| **Approved** | Admin approved (same as Completed) | Admin |
| **Rejected** | Admin rejected the payment | Admin |

## Invoice Status Reference

| Status | Meaning | Triggered By |
|--------|---------|-------------|
| **Pending** | Invoice created, no payment yet | Invoice generation |
| **Pending Verification** | Payment submitted, not yet approved | Cash/Bank transfer submit |
| **Partially Paid** | Some amount paid, balance remaining | Partial payment approved |
| **Paid** | Full amount received | Payment covers total |
| **Overdue** | Past due date, unpaid | Admin marks manually |
