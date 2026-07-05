# RentCollectionSystem - Architecture Document

## Overview

RentCollectionSystem is a **dual-portal rent collection management platform** built for Kuwait's property management industry. It provides an **Admin Portal** for property managers and an **Tenant Self-Service Portal** for tenants, with integrated KNET payment gateway support, multi-building management, and comprehensive payment verification.

**Tech Stack:** .NET 8 | ASP.NET Core MVC + REST API | EF Core 8 | SQL Server | Bootstrap 5 | jQuery

---

## Solution Structure

```
RentCollectionSystem.sln
├── RentCollection.Core           # Domain Layer - Entities, Enums
├── RentCollection.Application    # Application Layer - Interfaces, DTOs, Services
├── RentCollection.Infrastructure # Data Layer - DbContext, Repositories, External Services
├── RentCollection.Web            # Presentation - MVC Controllers, Views, API Controllers
├── RentCollection.API            # Standalone REST API (Cash/Bank Transfer endpoints)
└── RentCollection.Tests          # Unit Tests (xUnit, Moq)
```

### Dependency Flow (Clean Architecture)

```
Web/API → Application → Core
  ↓
Infrastructure → Application → Core
```

- **Core** has zero dependencies (pure domain)
- **Application** depends only on Core
- **Infrastructure** implements Application interfaces
- **Web/API** wires everything via DI

---

## Domain Model (16 Entities)

### Entity Relationship Diagram

```
Company (Root)
├── ApplicationUser (system users)
├── Tenant (renters)
│   ├── Lease → Unit → Property
│   ├── Payment
│   │   ├── CashPayment (1:1)
│   │   ├── BankTransfer (1:1)
│   │   ├── PaymentAudit (1:N)
│   │   └── KNETReconciliation (1:N)
│   ├── Invoice
│   └── MaintenanceRequest
├── Property (buildings)
│   ├── Unit (apartments)
│   └── MaintenanceRequest
└── Document (file attachments)

Standalone:
├── User (legacy auth table)
├── PaymentIdempotency (duplicate prevention)
└── KNETReconciliation (bank settlement matching)
```

### Core Entities

| Entity | Table | Purpose |
|--------|-------|---------|
| **Company** | Companies | Property management company (multi-tenant root) |
| **Property** | Properties | Building with KNET config, address, governorate |
| **Unit** | Units | Apartment/unit within a building |
| **Tenant** | Tenants | Renter with Civil ID, PACI number, password |
| **Lease** | Leases | Rental agreement linking Tenant ↔ Unit |
| **Invoice** | Invoices | Monthly rent invoice |
| **Payment** | Payments | Core payment record (KNET/Cash/BankTransfer) |
| **CashPayment** | CashPayments | Cash payment details (1:1 with Payment) |
| **BankTransfer** | BankTransfers | Bank transfer details + proof file (1:1 with Payment) |
| **PaymentAudit** | PaymentAudits | Audit log for mismatches and resolution |
| **PaymentIdempotency** | PaymentIdempotencies | 24-hour duplicate prevention keys |
| **KNETReconciliation** | KNETReconciliations | KNET bank settlement matching |
| **MaintenanceRequest** | MaintenanceRequests | Tenant maintenance tickets |
| **Document** | Documents | File metadata (uploads) |
| **ApplicationUser** | AspNetUsers | ASP.NET Identity user (admin/manager) |
| **User** | Users | Custom auth table (legacy) |

### Enums

```csharp
PaymentType:   KNET(1), Cash(2), BankTransfer(3), Cheque(4)
PaymentStatus: Pending(1), Completed(2), Failed(3), Cancelled(4), PendingVerification(5), Approved(6), Rejected(7)
BankTransferStatus: PendingVerification(1), Approved(2), Rejected(3)
```

---

## Database

- **Provider:** SQL Server (`RAVIWST\SQLEXPRESS`)
- **Database:** `RentCollectionDB`
- **ORM:** EF Core 8 with Code-First Migrations
- **DbContext:** `ApplicationDbContext` extends `IdentityDbContext<ApplicationUser>`
- **DbSets:** 15 entity sets + ASP.NET Identity tables

### Key Configuration

| Entity | Indexes | Constraints |
|--------|---------|-------------|
| Payment | PaymentDate, PaymentType, Status, CivilID, IdempotencyKey, HasMismatch | FK to Lease, Tenant, Property, Invoice |
| Unit | (PropertyId, UnitNumber) unique | FK to Property (Cascade) |
| Lease | Status, LeaseEndDate | FK to Unit, Tenant (Restrict) |
| Invoice | DueDate, Status | FK to Lease, Tenant (Restrict) |
| Tenant | Email, GovernmentId | FK to Company (Restrict) |
| Property | - | FK to Company (Restrict) |
| PaymentAudit | AuditTimestamp, IsMismatch, IsResolved | FK to Payment (Cascade) |

---

## Authentication & Authorization

### Three Authentication Schemes

| Scheme | Used By | Mechanism |
|--------|---------|-----------|
| **Cookie Auth** | Admin Portal (MVC) | ASP.NET Identity cookies, 12hr expiry |
| **Session Auth** | Tenant Portal | HttpContext.Session with 2hr timeout |
| **JWT Bearer** | REST API | HS256 signed tokens, 60min expiry |

### Admin Portal Auth
- Login: `/Account/Login` with username + password (BCrypt hash)
- Roles: `SuperAdmin`, `Admin`, `Manager`, `Accountant`, `User`
- Cookie: `RentCollectionAuth` (HttpOnly, 12hr sliding)
- Global `[Authorize]` filter — all controllers require auth by default

### Tenant Portal Auth
- Login: `/TenantPortal/Login` with Civil ID + password (BCrypt hash)
- Session: `TenantSession` cookie (HttpOnly, 2hr timeout)
- Optional "Remember Me" cookie (30 days)
- API endpoints use `[AllowAnonymous]` since tenant auth is session-based

### API Auth
- JWT token from `/api/auth/login`
- Configurable via `Jwt` section in appsettings.json
- Access token: 60 min, Refresh token: 7 days

---

## Application Layer

### Service Interfaces

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `IKnetPaymentService` | `KnetPaymentService` | KNET gateway: initiate, handle response, test mode |
| `ITenantLeaseService` | `TenantLeaseService` | Multi-lease tenant login, lease resolution |
| `IPaymentVerificationService` | `PaymentVerificationService` | Mismatch detection, audit, reconciliation |
| `IPaymentService` | `PaymentService` | Payment queries and aggregation |
| `IBankTransferService` | `BankTransferService` | Bank transfer submission and approval |
| `ICashPaymentService` | `CashPaymentService` | Cash payment recording |
| `IFileStorageService` | `FileStorageService` | File upload to wwwroot/uploads |
| `IJwtTokenService` | `JwtTokenService` | JWT token generation |
| `IRepository<T>` | `Repository<T>` | Generic async CRUD operations |

### Generic Repository Pattern

```csharp
IRepository<T>:
  GetByIdAsync(int id)
  GetAllAsync() / GetAllAsync(predicate)
  GetFirstOrDefaultAsync(predicate)
  AddAsync(T) / UpdateAsync(T) / DeleteAsync(T)
  CountAsync(predicate?) / ExistsAsync(predicate)
```

---

## Web Layer

### URL Configuration
- **Web App:** http://localhost:5032
- **API App:** http://localhost:5069 (separate project)

### Controllers (23 Total)

**Admin MVC Controllers:**

| Controller | Route | Features |
|-----------|-------|----------|
| DashboardController | /Dashboard | KPIs: properties, units, occupancy, revenue, payments |
| PropertiesController | /Properties | CRUD buildings with KNET config, units, documents |
| TenantsController | /Tenants | CRUD tenants with company assignment |
| LeasesController | /Leases | CRUD lease agreements |
| InvoicesController | /Invoices | Invoice list, details, deletion |
| PaymentsController | /Payments | Payment list, mismatch detection, reconciliation |
| PaymentApprovalController | /PaymentApproval | Approve/reject bank transfers and cash |
| MaintenanceController | /Maintenance | Maintenance request CRUD with assignment |
| DocumentsController | /Documents | File upload and management |
| ReportsController | /Reports | Executive dashboard, payment/invoice/property reports, Excel export |
| UsersController | /Users | System user CRUD with role assignment |
| AccountController | /Account | Login, logout, password reset |

**Tenant Portal Controller:**

| Controller | Route | Features |
|-----------|-------|----------|
| TenantPortalController | /TenantPortal | Login, dashboard, payments, receipts, maintenance |

**API Controllers:**

| Controller | Route | Features |
|-----------|-------|----------|
| KnetPaymentController | /api/payments/knet | Initiate, response callback, error callback, test mode |
| BankTransferApiController | /api/banktransfer | Bank transfer API |
| AuthController | /api/auth | JWT authentication |
| TenantApiController | /api/tenant | Tenant data API |
| DiagnosticsController | /api/diagnostics | Health checks |
| SupportController | /api/support | Support endpoints |

### Views Structure (16 Modules)

```
Views/
├── Shared/_Layout.cshtml     # Admin layout: purple sidebar + top header
├── Account/                  # Login, register, password reset (8 views)
├── Dashboard/Index.cshtml    # KPI cards, recent payments, expiring leases
├── TenantPortal/             # Standalone tenant UI (12 views)
├── Properties/               # Building management (5 views)
├── Tenants/                  # Tenant management (3 views)
├── Leases/                   # Lease management (5 views)
├── Invoices/                 # Invoice tracking (3 views)
├── Payments/                 # Payment verification (1 view)
├── PaymentApproval/          # Approval workflow (3 views)
├── Maintenance/              # Maintenance CRUD (5 views)
├── Documents/                # Document uploads (2 views)
├── Reports/                  # Analytics & export (6 views)
└── Users/                    # User management (5 views)
```

### Admin UI Design
- Purple/Indigo gradient theme (#6366f1)
- Fixed 240px left sidebar with navigation groups
- Fixed 60px top header with search, notifications, profile
- Bootstrap 5 + Font Awesome 6.4.0
- Responsive with mobile menu toggle

---

## Payment Architecture

### Payment Methods

```
                    ┌─────────────────┐
                    │     Payment     │
                    │  (core record)  │
                    └────────┬────────┘
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────────┐
        │   KNET   │  │   Cash   │  │ BankTransfer │
        │ (online) │  │ (manual) │  │  (with proof)│
        └──────────┘  └──────────┘  └──────────────┘
```

### KNET Payment Flow (Direct Gateway Integration)

```
1. Tenant selects invoice → clicks "Pay with KNET"
2. Frontend: POST /api/payments/knet/initiate
3. Backend: KnetPaymentService.InitiatePaymentAsync()
   a. Validate invoice, resolve building credentials
   b. Create Payment record (Pending)
   c. Encrypt params with AES-128-CBC (TerminalResourceKey)
   d. Return KNET redirect URL
4. Frontend: window.location.href = redirectUrl
5. Browser → kpaytest.com.kw (sandbox) or kpay.com.kw (prod)
6. Tenant completes payment on KNET page
7. KNET POSTs encrypted trandata to /api/payments/knet/response
8. Backend: Decrypt trandata, update Payment → Completed/Failed
9. Redirect to PaymentSuccess or PaymentFailed page
```

**KNET Encryption:** AES-128-CBC with TerminalResourceKey as both Key and IV. Cleartext params encrypted → raw bytes → uppercase hex string.

**Multi-Building KNET:** Each Property can have its own `KnetTranportalId`, `KnetTranportalPwd`, `KnetTerminalResourceKey`. Falls back to default credentials from appsettings.json.

### Bank Transfer Flow

```
1. Tenant submits bank transfer with proof document
2. Payment created with Status = PendingVerification
3. Admin sees pending in PaymentApproval/Index
4. Admin reviews proof, clicks Approve or Reject
5. Payment status → Approved/Rejected, Invoice updated
```

### Cash Payment Flow

```
1. Admin records cash payment via PaymentApproval
2. CashPayment created with CollectedBy, ReceiptNumber
3. Payment status → Completed, Invoice updated
```

### Payment Verification & Audit

```
Payment Created
    ↓
Mismatch Detection (automatic)
    ├── Civil ID Mismatch (tenant vs payer)
    ├── Building Mismatch (lease vs payment property)
    ├── KNET MID Mismatch (building vs used credentials)
    └── Duplicate Detection (idempotency key)
    ↓
PaymentAudit record created
    ↓
Admin views Payments/Mismatches
    ↓
Admin resolves → PaymentAudit.IsResolved = true
```

---

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=RAVIWST\\SQLEXPRESS;Database=RentCollectionDB;..."
  },
  "Jwt": {
    "Key": "...",
    "Issuer": "RentCollectionAPI",
    "Audience": "RentCollectionClient",
    "AccessTokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
  },
  "Knet": {
    "TranportalId": "<knet-transportal-id>",
    "TranportalPwd": "<knet-transportal-pwd>",
    "TerminalResourceKey": "<knet-terminal-resource-key>",
    "PaymentApiUrl": "https://kpaytest.com.kw/kpg/PaymentHTTP.htm?param=paymentInit",
    "PaymentApiUrlProd": "https://www.kpay.com.kw/kpg/PaymentHTTP.htm?param=paymentInit",
    "UseSandbox": true,
    "UseTestMode": false,
    "SuccessUrl": "http://localhost:5032/api/payments/knet/response",
    "ErrorUrl": "http://localhost:5032/api/payments/knet/error"
  }
}
```

---

## Middleware Pipeline (Program.cs)

```csharp
// Services
AddControllersWithViews (global AuthorizeFilter)
AddDbContext<ApplicationDbContext> (SQL Server)
AddDistributedMemoryCache + AddSession (2hr timeout)
AddAuthentication (Cookie + JWT Bearer)
AddScoped<IRepository<T>, Repository<T>>()
AddScoped<IKnetPaymentService, KnetPaymentService>()
AddScoped<ITenantLeaseService, TenantLeaseService>()
AddScoped<IPaymentVerificationService, PaymentVerificationService>()
// ... other services

// Pipeline
UseHttpsRedirection
UseStaticFiles
UseSession
UseAuthentication
UseAuthorization
MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}")
```

---

## Third-Party Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.10 | SQL Server ORM |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.11 | Identity framework |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.11 | JWT authentication |
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| AutoMapper | 12.0.1 | Object mapping |
| EPPlus | 7.0.0 | Excel report export |
| Swashbuckle.AspNetCore | 9.0.6 | Swagger/OpenAPI |

---

## Security Features

1. **Password Hashing** - BCrypt with salt
2. **JWT Signing** - HS256 with 32+ character key
3. **HTTPS Enforcement** - Redirect in production
4. **HttpOnly Cookies** - Prevents XSS cookie theft
5. **CSRF Protection** - ASP.NET Core antiforgery (except KNET callbacks)
6. **Role-Based Auth** - Controller-level role restrictions
7. **Session Security** - 2hr timeout, HttpOnly, essential flags
8. **File Validation** - Type and size checks on uploads (5MB limit)
9. **Payment Idempotency** - 24hr duplicate prevention
10. **Audit Logging** - PaymentAudit tracks all payment events
11. **KNET Encryption** - AES-128-CBC for gateway communication

---

## Testing

- **Framework:** xUnit + Moq
- **Test Files:**
  - `AdminWorkflowTests.cs` - Admin CRUD and approval workflows
  - `TenantWorkflowTests.cs` - Tenant login, payment, lease workflows
  - `Helpers/TestDatabaseHelper.cs` - In-memory test data setup

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Clean Architecture layers | Separation of concerns, testability |
| Generic Repository pattern | Consistent data access across entities |
| Cookie + JWT dual auth | MVC pages use cookies, APIs use JWT |
| Session-based tenant auth | Simpler than JWT for portal, no token refresh needed |
| Per-building KNET credentials | Multi-building support with credential isolation |
| Payment → CashPayment/BankTransfer 1:1 | Polymorphic payment details without inheritance |
| PaymentIdempotency table | Prevents duplicate KNET transactions (24hr window) |
| PaymentAudit trail | Complete history for compliance and dispute resolution |
| KNETReconciliation | Bank settlement matching for financial accuracy |
| EPPlus for Excel | Server-side report generation without client dependencies |
