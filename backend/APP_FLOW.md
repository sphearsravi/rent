# RentCollection — App Flow

End-to-end flow document for the RentCollection.Web admin portal and tenant
portal. Captures what each role does, which controller and endpoint serves
each step, and how background work hangs off the live flow.

Generated 2026-05-25 from the live codebase. File-and-line references are
clickable from inside the repo.

---

## 1. Solution at a glance

```
RentCollectionSystem/
├── RentCollection.Core           # Entities + enums (no infra deps)
├── RentCollection.Application    # IPdfService, IExecutiveDashboardComposer, IEmailService
├── RentCollection.Infrastructure # EF Core DbContext, QuestPDF, Hangfire jobs, K-NET, email
└── RentCollection.Web            # ASP.NET Core 8 MVC + Razor views (this is the app)
```

Runtime: ASP.NET Core 8 + EF Core 9 (SQL Server), Hangfire (SQL Server
storage), QuestPDF (PDFs), EPPlus (Excel), Chart.js (CDN, inline charts).
Auth is cookie-based with five roles (see §3).

Dev URL: `http://localhost:5032`.

---

## 2. Top-level entity map

```
Company ── 1:N ── Property ── 1:N ── Unit
                                       │
                                       └── 1:1 ── Lease ── N:1 ── Tenant ── N:1 ── Company
                                                  │
                                                  ├── 1:N ── Invoice ── 1:N ── Payment
                                                  └── 1:N ── MaintenanceRequest
```

- **Tenant** implements `ISoftDeletable` — `IsDeleted` flag, never hard-deleted
  (financial history must survive). A global query filter hides deleted rows
  unless the controller opts in via `IgnoreQueryFilters()`.
- **Lease** has a lifecycle: `Pending → Active → (Expired | Terminated | Renewed)`.
  Renewals link old↔new via `RenewedFromLeaseId / RenewedToLeaseId`.
- **Invoice** statuses: `Pending`, `PartiallyPaid`, `Paid`, `Overdue`, `Cancelled`.
- **Payment** statuses (enum): `Pending`, `Completed`, `Failed`, `Refunded`.

---

## 3. Roles & access matrix

Defined in [User.cs](RentCollection.Core/Entities/User.cs):

| Role         | Scope          | Typical actions                                              |
|--------------|----------------|--------------------------------------------------------------|
| `SuperAdmin` | Cross-company  | Everything, including `Forget`/`Restore` of tenants          |
| `Admin`      | Single company | Same as SuperAdmin but scoped by `CurrentCompanyId` filter   |
| `Manager`    | Single company | Tenants, leases, maintenance — no GDPR-destructive actions   |
| `Accountant` | Single company | Invoices, payments, reports — read+write                     |
| `User`       | Single company | Read-only viewer                                             |
| `Tenant`     | Self           | TenantPortal only — never granted to admin-side users        |

Convenience groups used in `[Authorize(Roles = …)]`:
- `Roles.AnyAdmin` = SuperAdmin + Admin
- `Roles.Operational` = SuperAdmin + Admin + Manager
- `Roles.Financial` = SuperAdmin + Admin + Accountant

---

## 4. Auth & session flow

```
GET  /Account/Login                    # AccountController.Login (anonymous)
POST /Account/Login                    # validates + sets auth cookie
  │
  ├──► role = Tenant   → /TenantPortal/Dashboard
  └──► role = staff    → /Dashboard
                         (admin shell with sidebar + topbar)

GET  /Account/Logout                   # clears cookie, back to /Account/Login
GET  /Account/Profile                  # view own profile
GET  /Account/AccessDenied             # surfaced when role filter rejects
```

The admin shell is in [_Layout.cshtml](RentCollection.Web/Views/Shared/_Layout.cshtml)
and styled by [layout-shell.css](RentCollection.Web/wwwroot/css/layout-shell.css).
Every authenticated page extends it. There is a separate `@media print`
block in that CSS that flattens the shell (hides sidebar/topbar, resets
`margin-left: 0`) so invoices/receipts print clean.

---

## 5. Admin portal — page-by-page flow

### 5.1 /Dashboard
Single landing page after login. Shows KPI cards, recent activity, alerts.
Backed by [DashboardController](RentCollection.Web/Controllers/DashboardController.cs).

### 5.2 /Properties  (Operational)
[PropertiesController](RentCollection.Web/Controllers/PropertiesController.cs)

| Verb | Route                          | Purpose                                       |
|------|--------------------------------|-----------------------------------------------|
| GET  | `/Properties`                  | Grid+Table view, KPI strip, filter chips      |
| GET  | `/Properties/Snapshot`         | Portfolio KPIs (cached 30s)                   |
| GET  | `/Properties/KpiBatch?ids=…`   | Per-card MTD / Outstanding / Last payment     |
| POST | `/Properties/ToggleStatus/{id}`| Active ↔ Inactive (antiforgery)               |
| GET  | `/Properties/ExportExcel`      | EPPlus xlsx, filters round-trip               |
| GET  | `/Properties/Details/{id}`     | Drill-down: units, leases, occupancy          |
| GET  | `/Properties/Create`           | New property form                             |
| GET  | `/Properties/Edit/{id}`        | Edit + delete from kebab                      |

KPIs in the strip: Total Properties / Total Units / Occupied / Occupancy% /
Avg Rent. Each card shows MTD collected + Outstanding + Last payment date
lazily loaded via the KpiBatch endpoint.

### 5.3 /Tenants  (Operational, with Admin-gated destructive actions)
[TenantsController](RentCollection.Web/Controllers/TenantsController.cs)

```
GET  /Tenants                                    # list + KPI strip + bulk bar
GET  /Tenants?includeArchived=true               # IgnoreQueryFilters() → shows soft-deleted
GET  /Tenants/Snapshot                           # 5 portfolio KPIs (cached)
GET  /Tenants/KpiBatch?ids=…                     # outstanding / last payment / active leases / health / archived
POST /Tenants/ToggleStatus/{id}                  # inline Active/Inactive flip
POST /Tenants/BulkUpdateStatus  ids[], status    # bulk Active/Inactive (S5)
POST /Tenants/BulkArchive       ids[]            # bulk soft-delete (S5, Admin)
GET  /Tenants/ExportExcel?search&propertyId&status&ids
                                                 # filtered OR selection export
GET  /Tenants/Statement/{id}                     # PDF (S3) — contact + active leases + last 12 payments
GET  /Tenants/Import                             # CSV/XLSX upload (S6, Admin)
GET  /Tenants/ImportTemplate                     # blank xlsx with sample row
POST /Tenants/Import         IFormFile file      # parse → validate → insert → ImportResult view
POST /Tenants/Restore/{id}                       # un-archive (S4, Admin)
POST /Tenants/Forget/{id}                        # GDPR redact (Admin)
GET  /Tenants/Export/{id}                        # GDPR JSON dump per tenant
GET  /Tenants/Create  ·  Edit  ·  Delete  ·  Details
```

Toolbar flow (top to bottom): KPI strip → filter chips + Show-archived
toggle + Expiring-lease selector + Import + Export → search/property/status
filters → table with bulk checkboxes → floating bulk-action pill when
something is selected.

Health badge per row:
- `good`   — outstanding ≤ 0
- `amber`  — outstanding > 0 but oldest due within `Tenants:OverdueAmberDays`
- `bad`    — past `Tenants:OverdueRedDays`
- `pending`— KPI batch hasn't loaded yet

### 5.4 /Leases  (Operational)
[LeasesController](RentCollection.Web/Controllers/LeasesController.cs)

```
GET  /Leases                                     # grouped by property + KPI strip + chips
GET  /Leases/Snapshot                            # totalLeases / active / expiringSoon / expired / monthlyRentRoll
GET  /Leases/KpiBatch?ids=…                      # per-lease: outstanding, mtdCollected, lastPaymentDate (S2)
GET  /Leases/ExportExcel?search&propertyId&status&expiring
POST /Leases/Renew      id, newEndDate, newMonthlyRent?
POST /Leases/Terminate  id, terminationDate, reason?, depositRefundedAmount?
POST /Leases/Transfer   id, newUnitId, effectiveDate
GET  /Leases/Create  ·  Edit  ·  Delete  ·  Details
```

Lease lifecycle from the kebab:
```
[Active] ──Renew──► creates new Lease, old → Renewed, RenewedTo/From wired
[Active] ──Terminate──► sets TerminationDate/Reason/By, deposit-refund flag
[Active] ──Transfer──► closes old as "Transfer to unit N", new Lease on new Unit
```

Expiring filter: rows have `data-days-remaining` set server-side from
`lease.LeaseEndDate − today`. Only Active leases within the horizon match;
past-due rows are reached via the **Status: Expired** filter instead.

Per-row Collection cell shows outstanding (or "Paid up") plus a sub-line
`MTD <amt> · last <date>` from the KpiBatch endpoint.

### 5.5 /Invoices  (Financial)
[InvoicesController](RentCollection.Web/Controllers/InvoicesController.cs)
+ [InvoiceApiController](RentCollection.Web/Controllers/Api/InvoiceApiController.cs)

```
GET  /Invoices?tenantId=&leaseId=                # filtered list
GET  /Invoices/Create                            # one or batch
GET  /Invoices/Details/{id}                      # has "Download PDF" button
GET  /api/tenant/{tenantId}/invoices/{id}/invoice.pdf
                                                 # PDF via IPdfService.RenderInvoice
POST /Invoices/MarkPaid  ·  Cancel  ·  Reissue
```

### 5.6 /Payments and /CashPayment  (Financial)
- `/Payments` — list, filter, refund-aware
- `/CashPayment/Record` — manual cash entry by accountant (writes Payment +
  marks linked Invoice paid or partially paid)
- `/PaymentApproval` — Manager/Admin queue for verifying tenant-uploaded
  bank-transfer screenshots before flipping the Payment to Completed

### 5.7 /Reports  (Financial)
[ReportsController](RentCollection.Web/Controllers/ReportsController.cs)
+ [IExecutiveDashboardComposer](RentCollection.Application/Interfaces/IExecutiveDashboardComposer.cs)

```
GET  /Reports                                    # 4-card KPI strip + 3 charts
GET  /Reports/Snapshot?from=&to=                 # JSON: KPIs + 12-month trend (w/ YoY) + status breakdown + top-5 properties
GET  /Reports/ExecutiveDashboard                 # bigger view, drill-down on status donut
GET  /Reports/ExecutiveDashboardPdf?from=&to=    # IPdfService.RenderExecutiveDashboard (ResponseCache 60s)
```

Period is persisted to a cookie (`rc-reports-period`, 30 days) so the
selection survives page navigation.

### 5.8 /Maintenance, /Documents, /UserManagement, /GlExport, /Refunds
Standard CRUD pages — each follows the same `_Layout` + `_PageHeader` +
`_AlertMessages` pattern.

---

## 6. Tenant portal flow

[TenantPortalController](RentCollection.Web/Controllers/TenantPortalController.cs)

```
GET /TenantPortal/Dashboard                      # outstanding, lease, recent payments
GET /TenantPortal/Invoices                       # mine
GET /TenantPortal/Payments                       # mine
GET /TenantPortal/MaintenanceRequests            # mine
POST /TenantPortal/SubmitMaintenance             # opens a ticket
POST /TenantPortal/UploadPaymentProof            # bank-transfer evidence
                                                 # → goes to /PaymentApproval queue
```

A tenant logs in via the same `/Account/Login` page; if `role=Tenant` the
post-login redirect goes to `/TenantPortal/Dashboard` instead of `/Dashboard`.

---

## 7. PDF generation

[IPdfService](RentCollection.Application/Interfaces/IPdfService.cs) →
[QuestPdfService](RentCollection.Infrastructure/Services/Pdf/QuestPdfService.cs)

| Method                       | Triggered by                                     |
|------------------------------|--------------------------------------------------|
| `RenderInvoice`              | `/Invoices/Details` "Download PDF" button, monthly invoice email |
| `RenderReceipt`              | After successful Payment Completed event         |
| `RenderExecutiveDashboard`   | `/Reports/ExecutiveDashboardPdf` + monthly cron  |
| `RenderTenantStatement`      | `/Tenants/Statement/{id}` (Tenants S3 kebab)     |

QuestPDF Community license is activated once at process start.

---

## 8. Background jobs (Hangfire)

[RecurringJobs](RentCollection.Infrastructure/Services/RecurringJobs.cs).
Gated on `Hangfire:Enabled` config flag. SQL Server storage. Dashboard at
`/hangfire` (admin-only).

| Job ID                            | Schedule       | What it does                                           |
|-----------------------------------|----------------|--------------------------------------------------------|
| `monthly-invoice-generation`      | 1st of month   | Generates next-month invoices from active leases       |
| `monthly-executive-dashboard-email` | 1st of month   | Composes dashboard PDF, emails distribution list       |
| `daily-overdue-flag`              | Daily 02:00    | Flips invoices past due date to Overdue                |
| `weekly-arrears-reminder`         | Monday 09:00   | Emails tenants with outstanding balance                |

---

## 9. Cross-cutting features

### Caching
- `AddResponseCaching()` + `UseResponseCaching()` registered in
  [Program.cs](RentCollection.Web/Program.cs).
- All `*/Snapshot` endpoints: `[ResponseCache(Duration=30)]`.
- All `*/KpiBatch` endpoints: `[ResponseCache(Duration=30, VaryByQueryKeys={"ids"})]`.
- `ExecutiveDashboardPdf`: `[ResponseCache(Duration=60)]`.

### Soft delete
- `ISoftDeletable` on `Tenant`. Global query filter in
  [ApplicationDbContext.OnModelCreating](RentCollection.Infrastructure/Data/ApplicationDbContext.cs).
- Bypass with `IgnoreQueryFilters()` when an admin opts in
  (`/Tenants?includeArchived=true` + the **Show archived** toolbar toggle).

### Excel (EPPlus)
- `ExcelPackage.LicenseContext = LicenseContext.NonCommercial` set in the
  constructors of TenantsController, PropertiesController, LeasesController.
- Selection export pattern: when `?ids=…` is present the controller ignores
  the search/filter params; otherwise it applies them. Same xlsx-builder
  is reused for both paths.

### CSV/XLSX import (Tenants only)
- [Tenants/Import.cshtml](RentCollection.Web/Views/Tenants/Import.cshtml)
  → drag-drop or pick file → `POST /Tenants/Import`
- Header-mapped column lookup (column order doesn't matter, optional
  columns can be dropped).
- Dedupes against existing emails + PACIs, and within the file.
- Renders [Tenants/ImportResult.cshtml](RentCollection.Web/Views/Tenants/ImportResult.cshtml)
  with per-row "Imported / Skipped + reason".

### Antiforgery + bulk actions
- Every mutating POST uses `[ValidateAntiForgeryToken]`.
- Bulk-action JS reads the page's `__RequestVerificationToken` once and
  attaches it to every fetch (`/Tenants/BulkUpdateStatus`,
  `/Tenants/BulkArchive`, `/Tenants/Restore`, `/Tenants/Forget`).

---

## 10. Known deferrals (active backlog)

Server-side pagination on the big list pages (Tenants, Leases, Invoices) —
the client currently loads all rows then filters in JS. Fine up to ~5 000
rows per list.

Audit trail view — would need an `AuditLog` entity + migration + writer
service. Forget/Restore/Bulk-archive currently log to `ILogger` only.

Bulk actions on Leases — terminating leases has side effects (deposit
refund, unit-status flip, possible refunds queue) that make a bulk "×"
risky. Kept to single-lease lifecycle endpoints on purpose.

CSV import for Properties/Leases — the Tenants importer is the template
to copy from when prioritised.

Mobile-native experience — `_Layout` is responsive but the operational
flows assume a wide screen. The mobile breakpoints on /Tenants and /Leases
convert the wide tables to card stacks; the rest still expect ≥ 1024 px.
