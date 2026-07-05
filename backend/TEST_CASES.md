# RentCollection — Test Case Document

**Product:** ALRTAM Property Management Suite (VIIJAR)
**Version under test:** Sprint 23 (2026-06)
**Test environments:**
- **Local dev:** http://localhost:5032 (against `RAVIWST\SQLEXPRESS\RentCollectionDB`)
- **SmarterASP demo:** http://<deploy-host> (against `<database>`)

> Companion to [SYSTEM_WORKFLOW.md](SYSTEM_WORKFLOW.md). Run the smoke
> tests after every deploy. Run the full pack before any milestone release.

---

## How to use this document

Each test case has:
- **ID** — stable identifier (use in bug reports)
- **Priority** — P0 (blocks release) · P1 (must work) · P2 (nice-to-have)
- **Preconditions** — state the system must be in before you start
- **Steps** — what you click / type
- **Expected** — what you should see
- **Result** — tester fills in: Pass / Fail / Blocked
- **Notes** — actual result if Fail, screenshot path, bug ticket number

Print a copy or duplicate per test cycle. **One environment per run** —
don't mix local-dev and SmarterASP results in the same sheet.

### Test accounts
| Role           | Email              | Password   | Notes                       |
| -------------- | ------------------ | ---------- | --------------------------- |
| Admin (demo)   | admin@test.com     | Admin@123  | Seeded in the .bak          |
| Tenant (demo)  | tenant@test.com    | Tenant@123 | Seeded in the .bak          |

### Tester sign-off
| Cycle      | Tester    | Build hash | Date       | Pass | Fail | Blocked | Sign-off |
| ---------- | --------- | ---------- | ---------- | ---- | ---- | ------- | -------- |
| Cycle 1    |           |            |            |      |      |         |          |
| Cycle 2    |           |            |            |      |      |         |          |

---

## 0. Smoke pack (run after every deploy — 5 minutes)

| ID     | Title                                                | Priority | Steps                                                                       | Expected                                                          | Result |
| ------ | ---------------------------------------------------- | -------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------- | ------ |
| SMK-01 | Login page renders                                   | P0       | Visit `/Account/Login`                                                      | Page loads, ALRTAM logo + Civil ID + Email fields visible         |        |
| SMK-02 | Admin can sign in                                    | P0       | Click "Fill demo credentials" → Sign in                                     | Redirects to `/Dashboard`, sidebar visible, user name top-right   |        |
| SMK-03 | Dashboard KPIs load without error                    | P0       | After login                                                                 | Tiles show numbers (not "—" or error icons)                       |        |
| SMK-04 | Static asset — ALRTAM logo top-right                 | P1       | After login, look at header                                                 | Logo visible on right of top header                               |        |
| SMK-05 | Tenant Create page renders (form-revamp)             | P0       | Click sidebar → Tenants → "Add Tenant"                                      | 3 zones visible: Essentials (open) + More details (collapsed) + Documents |        |
| SMK-06 | Tenant portal login renders                          | P1       | Go to `/TenantPortal/Login`                                                 | Different login page, tenant-styled                               |        |
| SMK-07 | Logout works                                         | P1       | Top-right user dropdown → Logout                                            | Redirects back to login                                           |        |

---

## 1. Authentication

| ID     | Title                                                 | Priority | Preconditions | Steps                                                                                     | Expected                                                           | Result |
| ------ | ----------------------------------------------------- | -------- | ------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------ | ------ |
| AUTH-01 | Valid admin login                                    | P0       | Logged out    | Enter `admin@test.com` / `Admin@123` → Sign in                                            | Redirected to Dashboard                                            |        |
| AUTH-02 | Invalid password                                     | P0       | Logged out    | Enter `admin@test.com` / `wrong`                                                          | Same page, red error "Invalid email or password"                   |        |
| AUTH-03 | Empty email                                          | P1       | Logged out    | Leave email blank → Sign in                                                               | Inline validation chip "Required" below the field                  |        |
| AUTH-04 | Demo credentials button auto-fills                   | P1       | Logged out    | Click "Fill demo credentials"                                                             | Email + password fields populated                                  |        |
| AUTH-05 | Show / hide password toggle                          | P2       | Logged out    | Type password → click eye icon                                                            | Password becomes visible; icon flips to crossed-eye                |        |
| AUTH-06 | Remember-me cookie persists 12 hours                 | P1       | Logged in     | Tick "Remember me" → Sign in → close browser → reopen → visit `/Dashboard`                | Still logged in (no redirect to login)                             |        |
| AUTH-07 | Forgot password link reachable                       | P2       | Logged out    | Click "Forgot password?"                                                                  | `/Account/ForgotPassword` opens, email input shown                 |        |
| AUTH-08 | Tenant cannot login at staff portal                  | P0       | Logged out    | At `/Account/Login` enter tenant credentials                                              | Rejected: "This account is a tenant; use the tenant portal"        |        |
| AUTH-09 | Logout drops auth cookie                             | P0       | Logged in     | Top-right → Logout                                                                        | Cookie removed, redirected to login, sidebar gone                  |        |
| AUTH-10 | Direct URL while logged out redirects to login       | P0       | Logged out    | Visit `/Properties` directly                                                              | 302 to `/Account/Login?ReturnUrl=/Properties`                      |        |

---

## 2. Property module

| ID     | Title                                       | Priority | Preconditions      | Steps                                                                                                                | Expected                                                                  | Result |
| ------ | ------------------------------------------- | -------- | ------------------ | -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- | ------ |
| PROP-01 | Property list loads                        | P0       | Logged in as Admin | Sidebar → Properties                                                                                                 | Grid of cards / table of properties with KPI tiles at top                 |        |
| PROP-02 | Create property — Essentials only          | P0       | On `/Properties`   | Click "Add Property" → fill Name, PACI, Type=Apartment, Governorate=Al Asimah, Units=10, Company → Save              | Redirected to Index, new property shows in list                           |        |
| PROP-03 | Create property — required field missing   | P0       | On Create form     | Leave PACI blank → Save                                                                                              | Inline error chip "Required" under PACI; no save                          |        |
| PROP-04 | "More details" toggle opens optional zone  | P1       | On Create form     | Click "More details" caret                                                                                           | Section expands, Address/Valuation/Management fields shown                |        |
| PROP-05 | Save & add another                         | P1       | On Create form     | Fill Essentials → click "Save & add another"                                                                         | Form clears, green chip "Property X saved. Add another below."            |        |
| PROP-06 | Draft autosave + restore                   | P2       | On Create form     | Type Name + PACI → close tab → reopen Create form                                                                    | Fields pre-populated, green "Draft restored" chip                         |        |
| PROP-07 | Upload property document on create         | P1       | On Create form     | Fill Essentials → Documents zone → pick type=Deed + select PDF → Save                                                | Property saved, Document appears in `/Documents` linked to this property  |        |
| PROP-08 | Edit property                              | P1       | Property exists    | Open property → Edit → change Name → Save                                                                            | List shows new name                                                       |        |
| PROP-09 | Property details page                      | P1       | Property exists    | Click row                                                                                                            | Details page shows all fields, attached units list, attached documents    |        |
| PROP-10 | Delete property fails when units exist     | P1       | Property w/ units  | Try Delete                                                                                                           | Blocked with error: "Cannot delete property with active units"            |        |
| PROP-11 | Duplicate PACI rejected                    | P0       | Property w/ PACI=X | Try to create another property with PACI=X                                                                           | Friendly error, not 500: "A property with this PACI already exists"       |        |

---

## 3. Unit module

| ID     | Title                                  | Priority | Preconditions          | Steps                                                                          | Expected                                                                | Result |
| ------ | -------------------------------------- | -------- | ---------------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------- | ------ |
| UNIT-01 | Unit list loads                       | P0       | Logged in              | Sidebar → Units                                                                | Table loads, Add button visible top-right                               |        |
| UNIT-02 | Create unit without rent              | P1       | Property exists        | New Unit → fill Number + Bedrooms, leave MonthlyRent blank → Save              | Saves successfully (rent is optional at unit-level)                     |        |
| UNIT-03 | Duplicate (Property, UnitNumber) blocked | P0     | Unit exists            | Try to create another unit with same Property + UnitNumber                     | Friendly error, not SqlException                                        |        |
| UNIT-04 | Unit status reflects active lease     | P0       | Active lease on unit   | Open the unit's details page                                                   | Status = "Occupied" (not "Vacant")                                      |        |
| UNIT-05 | Lease pre-fill from Unit              | P1       | Vacant unit            | Open vacant unit → click "Create Lease"                                        | `/Leases/Create?unitId=X` opens with Property + Unit pre-selected       |        |

---

## 4. Tenant module + Civil ID enrichment

| ID     | Title                                                  | Priority | Preconditions      | Steps                                                                                                        | Expected                                                                            | Result |
| ------ | ------------------------------------------------------ | -------- | ------------------ | ------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- | ------ |
| TEN-01  | Tenant list loads                                     | P0       | Logged in          | Sidebar → Tenants                                                                                            | Table with KPIs at top                                                              |        |
| TEN-02  | Civil ID format validation — 12 digits                | P0       | On Create form     | Type Civil ID = `12345` → Tab out                                                                            | Red chip "Civil ID must be 12 digits"                                               |        |
| TEN-03  | Civil ID checksum + DOB auto-fill                     | P0       | On Create form     | Enter valid 12-digit Civil ID                                                                                | Green chip "✓ Civil ID validated — DOB pre-filled", DOB field populated             |        |
| TEN-04  | Civil ID checksum mismatch warning                    | P1       | On Create form     | Enter a 12-digit ID with wrong checksum                                                                      | Amber chip "⚠ Checksum mismatch — double-check the number", DOB still pre-filled    |        |
| TEN-05  | Civil ID scan — requires consent                      | P1       | Gemini enabled     | Click "Scan card" without ticking consent                                                                    | Red message "Please tick the consent box first"                                     |        |
| TEN-06  | Civil ID scan — Gemini fills name + DOB               | P1       | Gemini enabled     | Tick consent → click "Scan card" → upload Civil ID image                                                     | Status "Reading card..." → green "✓ Card read." → First/Last Name, DOB filled with amber border |        |
| TEN-07  | Civil ID scan — file too large rejected               | P1       | Gemini enabled     | Upload a 6 MB file                                                                                           | Red "File too large (max 5 MB)"                                                     |        |
| TEN-08  | Civil ID enrichment disabled message                  | P2       | Gemini disabled    | Try to scan                                                                                                  | "Civil ID enrichment is disabled. Contact admin to enable it."                      |        |
| TEN-09  | Required Essentials block save                        | P0       | On Create form     | Leave First Name blank → Save                                                                                | Red chip on First Name; no save                                                     |        |
| TEN-10  | CompanyId auto-assigned (no field visible)            | P0       | Logged in as Admin | New Tenant form                                                                                              | No Company picker visible; on save, tenant gets admin's CompanyId                   |        |
| TEN-11  | Tenant document upload on create                      | P1       | On Create form     | Add document Type=Civil ID + file → Save                                                                     | Document linked to tenant, visible in `/Documents`                                  |        |
| TEN-12  | Save & add another                                    | P1       | On Create form     | Click "Save & add another"                                                                                   | Form clears, green "Tenant X saved. Add another below."                             |        |
| TEN-13  | Edit tenant                                           | P1       | Tenant exists      | Open tenant → Edit → change phone → Save                                                                     | Updated phone shown in details                                                      |        |
| TEN-14  | Cross-company isolation                               | P0       | 2 admins, 2 cos    | Admin from Co A logs in → opens `/Tenants/Details/{id-from-Co-B}`                                            | 404 or 403 — never Co B's data                                                      |        |

---

## 5. Lease module

| ID     | Title                                                  | Priority | Preconditions             | Steps                                                                                                 | Expected                                                                            | Result |
| ------ | ------------------------------------------------------ | -------- | ------------------------- | ----------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- | ------ |
| LSE-01  | Lease list loads                                      | P0       | Logged in                 | Sidebar → Leases                                                                                      | Active leases table loads                                                           |        |
| LSE-02  | Create lease — happy path (no deposit)                | P0       | Vacant unit + tenant      | New Lease → pick Building → Unit (cascade) → Tenant → dates → rent=300 → deposit=0 → Save             | Lease saved as Active; unit status → Occupied; redirect to Index                    |        |
| LSE-03  | Create lease with deposit → Pending                   | P0       | Vacant unit + tenant      | Same as LSE-02 but deposit=500                                                                        | Lease saved as Pending; banner explains "issue deposit invoice via Activate"        |        |
| LSE-04  | Building → Unit cascade filters correctly             | P1       | Multiple buildings        | Pick Building A → check Unit dropdown                                                                 | Only Building A's vacant units shown                                                |        |
| LSE-05  | Cannot lease occupied unit                            | P0       | Occupied unit             | Try to create another lease on same unit                                                              | Friendly error, not duplicate-key SqlException                                      |        |
| LSE-06  | End date must be after start date                     | P0       | New form                  | Set end date before start date → Save                                                                 | Validation error chip                                                               |        |
| LSE-07  | Insurance — Frequency None means no insurance         | P1       | New form                  | Insurance Frequency=None → Save                                                                       | Saves; no insurance invoices created                                                |        |
| LSE-08  | Insurance — Monthly                                   | P1       | New form                  | Frequency=Monthly, Amount=10 → Save → wait for daily job (or trigger manually)                        | Monthly insurance invoice issued separate from rent                                 |        |
| LSE-09  | Activate Pending lease                                | P0       | Pending lease             | Open lease → Activate                                                                                 | Status → Active; deposit invoice issued; unit → Occupied                            |        |
| LSE-10  | Terminate active lease                                | P0       | Active lease              | Open lease → Terminate → enter reason → Confirm                                                       | Status → Terminated; unit → Vacant; refund record queued                            |        |
| LSE-11  | Lease document upload on create                       | P1       | New form                  | Add Signed Lease PDF → Save                                                                           | PDF linked to lease in `/Documents`                                                 |        |
| LSE-12  | Save & add another (Lease)                            | P2       | New form                  | Click "Save & add another"                                                                            | Form clears, green chip "Lease #X saved..."                                         |        |

---

## 6. Invoices

| ID     | Title                                       | Priority | Preconditions      | Steps                                                                  | Expected                                                                  | Result |
| ------ | ------------------------------------------- | -------- | ------------------ | ---------------------------------------------------------------------- | ------------------------------------------------------------------------- | ------ |
| INV-01  | Invoice list loads                         | P0       | Logged in          | Sidebar → Invoices                                                     | Table, filters (Status, Date, Tenant), totals KPIs                        |        |
| INV-02  | Daily rent invoicing creates invoice       | P0       | Active lease, DueDay=today | Wait for 02:00 UTC job (or trigger manually via Hangfire dashboard) | New invoice of type Rent for today's amount appears                       |        |
| INV-03  | No double-invoicing if job runs twice      | P0       | After INV-02       | Trigger the job again                                                  | No duplicate invoice (idempotency check)                                  |        |
| INV-04  | Late fee added after grace period          | P1       | Unpaid invoice past due | Wait for late-fee job (02:30 UTC)                                  | New invoice type=LateFee, amount = LateFeeAmount                          |        |
| INV-05  | Invoice PDF generates                      | P1       | Open invoice       | Click "Download PDF"                                                   | Branded PDF (gold + black, ALRTAM logo, 3-decimal KWD)                    |        |
| INV-06  | Invoice email queued                       | P1       | New invoice issued | Check `Notifications` table or admin notifications panel               | Pending notification of channel=Email exists                              |        |

---

## 7. Payments

### 7a. Cash payment (staff-recorded)

| ID     | Title                            | Priority | Preconditions    | Steps                                                                                | Expected                                                  | Result |
| ------ | -------------------------------- | -------- | ---------------- | ------------------------------------------------------------------------------------ | --------------------------------------------------------- | ------ |
| PAY-C-01 | Record cash payment            | P0       | Unpaid invoice   | Open invoice → "Record Cash" → enter amount + receipt no → Save                      | Payment Confirmed, invoice → Paid, balance = 0            |        |
| PAY-C-02 | Cash partial payment           | P1       | Unpaid invoice (300) | Enter amount=200                                                                | Invoice status PartiallyPaid, balance=100                 |        |
| PAY-C-03 | Cash overpayment rejected      | P1       | Unpaid invoice (300) | Enter amount=400                                                                | Friendly error "Amount exceeds invoice balance"           |        |

### 7b. Bank Transfer (tenant-uploaded, accountant-approved)

| ID     | Title                                    | Priority | Preconditions             | Steps                                                                                | Expected                                                          | Result |
| ------ | ---------------------------------------- | -------- | ------------------------- | ------------------------------------------------------------------------------------ | ----------------------------------------------------------------- | ------ |
| PAY-B-01 | Tenant uploads transfer screenshot    | P0       | Tenant logged in, invoice | Open invoice → Pay by Bank Transfer → upload PNG → Submit                            | Payment record Pending Approval; invoice status unchanged         |        |
| PAY-B-02 | Accountant sees in approval queue     | P0       | After PAY-B-01            | Accountant → `/PaymentApproval`                                                      | Row shows tenant, amount, screenshot link                         |        |
| PAY-B-03 | Approve bank transfer                 | P0       | Pending in queue          | Open row → Approve                                                                   | Payment Confirmed; invoice Paid; receipt email queued             |        |
| PAY-B-04 | Reject bank transfer with note        | P1       | Pending in queue          | Open row → Reject → enter note "wrong account" → Submit                              | Payment Rejected; tenant sees note in portal                      |        |

### 7c. K-NET (Kuwait gateway)

| ID     | Title                                | Priority | Preconditions             | Steps                                                                          | Expected                                                                    | Result |
| ------ | ------------------------------------ | -------- | ------------------------- | ------------------------------------------------------------------------------ | --------------------------------------------------------------------------- | ------ |
| PAY-K-01 | K-NET — sandbox happy path         | P0       | UseSandbox=true, invoice  | Tenant → Pay with K-NET → on K-NET form use test card → return                 | Returned to `/api/payments/knet/response`, payment Confirmed                |        |
| PAY-K-02 | K-NET — cancelled by user          | P1       | Same                      | On K-NET form, click Cancel                                                    | Returned to error URL, invoice unchanged, no payment record                 |        |
| PAY-K-03 | K-NET — duplicate transaction guard | P0      | Same                      | Submit same payment twice within 1s                                            | Second submission rejected by PaymentIdempotencies                          |        |
| PAY-K-04 | K-NET reconciliation job recovers  | P1       | "Unknown" K-NET payment   | Wait for 03:00 UTC reconcile job                                               | Status updated based on gateway truth (Success/Failed/Cancelled)            |        |

---

## 8. Maintenance

| ID     | Title                                  | Priority | Preconditions     | Steps                                                                                | Expected                                                | Result |
| ------ | -------------------------------------- | -------- | ----------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------- | ------ |
| MNT-01  | Tenant raises request                 | P0       | Tenant logged in  | Tenant portal → Maintenance → New → fill issue + upload photo → Submit               | Request appears in `/Maintenance` with status=Open      |        |
| MNT-02  | Manager assigns vendor                | P0       | Open request      | Open request → Assign → pick vendor → priority → ETA → Save                          | Status → Assigned, vendor name shown                    |        |
| MNT-03  | Mark complete with photos             | P1       | Assigned request  | Open → Mark Complete → upload after-photos + cost → Save                             | Status → Completed; cost stored                         |        |
| MNT-04  | Photos visible to tenant              | P1       | Completed request | Tenant portal → request                                                              | Tenant sees before+after photos                         |        |
| MNT-05  | Dashboard "open maint" KPI accurate   | P2       | Several requests  | Compare KPI count to `/Maintenance` filtered to Open                                 | Numbers match                                           |        |

---

## 9. Documents

| ID     | Title                              | Priority | Preconditions    | Steps                                                              | Expected                                                          | Result |
| ------ | ---------------------------------- | -------- | ---------------- | ------------------------------------------------------------------ | ----------------------------------------------------------------- | ------ |
| DOC-01 | Document browser loads             | P1       | Logged in        | Sidebar → Documents                                                | List of all documents, filter by Type/Entity                      |        |
| DOC-02 | Download document                  | P1       | Document exists  | Click row → Download                                               | File downloads, opens in browser/app                              |        |
| DOC-03 | Document linked to multiple entities | P2     | Civil ID on tenant + lease | Open document details                                    | "Linked to" list shows both tenant and lease                      |        |
| DOC-04 | Delete document removes file       | P1       | Document exists  | Delete                                                             | File gone from disk + DB; cascade removes DocumentLink rows       |        |
| DOC-05 | Reject oversized upload            | P1       | New form         | Upload 12 MB file                                                  | Friendly error, not 500                                           |        |
| DOC-06 | Reject wrong extension             | P1       | New form         | Upload .exe                                                        | Friendly error                                                    |        |

---

## 10. Reports

| ID     | Title                                 | Priority | Preconditions    | Steps                                                                       | Expected                                                          | Result |
| ------ | ------------------------------------- | -------- | ---------------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------- | ------ |
| RPT-01 | Rent collection by month — Excel     | P1       | Logged in        | Reports → Rent Collection → pick date range → Export Excel                  | .xlsx downloads, opens cleanly, KWD shows 3 decimals              |        |
| RPT-02 | Outstanding invoices — PDF           | P1       | Logged in        | Reports → Outstanding → Export PDF                                          | PDF branded, aged buckets correct                                 |        |
| RPT-03 | Tenant ledger                        | P1       | Tenant w/ history | Open tenant → Ledger                                                       | Chronological list of invoices + payments + running balance       |        |
| RPT-04 | Filter by Property                   | P1       | Multiple props   | Reports → pick Property A → Generate                                        | Only Property A's data                                            |        |
| RPT-05 | Filter respects company isolation    | P0       | Multi-co setup   | Admin of Co A → any report                                                  | Only Co A's data, never Co B's                                    |        |

---

## 11. Tenant portal

| ID     | Title                                | Priority | Preconditions      | Steps                                                                | Expected                                                            | Result |
| ------ | ------------------------------------ | -------- | ------------------ | -------------------------------------------------------------------- | ------------------------------------------------------------------- | ------ |
| TP-01  | Tenant login                         | P0       | Tenant credentials | Visit `/TenantPortal/Login` → sign in                                | Tenant dashboard loads (different shell from staff)                 |        |
| TP-02  | Dashboard — next rent due            | P0       | Tenant w/ lease    | After login                                                          | Amount + due date + days-to-due visible                             |        |
| TP-03  | View unpaid invoice                  | P0       | Unpaid invoice     | Invoices → open                                                      | Amount, due date, Pay button visible                                |        |
| TP-04  | Pay with K-NET button visible        | P0       | Unpaid invoice     | Same as TP-03                                                        | K-NET button present                                                |        |
| TP-05  | Raise maintenance request            | P0       | Tenant logged in   | Maintenance → New → submit                                           | Request created                                                     |        |
| TP-06  | Upload tenant document               | P1       | Tenant logged in   | Documents → Upload                                                   | Document saved, visible in their list                               |        |
| TP-07  | View receipt PDF                     | P1       | Paid invoice       | Invoices → Paid → Download receipt                                   | Branded PDF                                                         |        |
| TP-08  | Cannot see other tenants' data       | P0       | Tenant A logged in | Try URL `/TenantPortal/Invoice/{B's-invoice-id}`                     | 404 / 403                                                           |        |

---

## 12. Role-based access (negative path)

For each row: log in as the named role, attempt the URL, confirm result.

| ID     | Role         | URL                              | Expected           | Result |
| ------ | ------------ | -------------------------------- | ------------------ | ------ |
| RBAC-01 | Manager     | `/Users`                         | 403 / hidden       |        |
| RBAC-02 | Manager     | `/Payments`                      | 403 / hidden       |        |
| RBAC-03 | Manager     | `/Properties`                    | Allowed            |        |
| RBAC-04 | Accountant  | `/Maintenance`                   | View only (no create) |     |
| RBAC-05 | Accountant  | `/Reports`                       | Allowed            |        |
| RBAC-06 | Accountant  | `/Audit`                         | 403                |        |
| RBAC-07 | Admin       | `/hangfire`                      | Allowed            |        |
| RBAC-08 | Manager     | `/hangfire`                      | 403                |        |
| RBAC-09 | Tenant      | `/Dashboard` (staff dashboard)   | 403 / redirect to tenant portal |  |
| RBAC-10 | Anonymous   | `/api/civilid/parse?id=…`        | 401                |        |

---

## 13. Edge cases & negative tests

| ID     | Title                                          | Priority | Steps                                                                            | Expected                                                       | Result |
| ------ | ---------------------------------------------- | -------- | -------------------------------------------------------------------------------- | -------------------------------------------------------------- | ------ |
| EDGE-01 | XSS in tenant name                            | P0       | Create tenant with name `<script>alert(1)</script>`                              | Saved literally, rendered escaped — no JS execution            |        |
| EDGE-02 | SQL injection in search                       | P0       | Search box: `'; DROP TABLE Tenants;--`                                           | No error; tables intact                                        |        |
| EDGE-03 | Long string overflow                          | P1       | Tenant FirstName = 500 chars                                                     | Either truncated or friendly error, not 500                    |        |
| EDGE-04 | Negative rent                                 | P1       | New lease, rent = -100                                                           | Validation error                                               |        |
| EDGE-05 | Invoice paid in a closed period               | P1       | Try to record cash for a Closed period                                           | Blocked with reason                                            |        |
| EDGE-06 | Unicode tenant name (Arabic)                  | P1       | Name = `محمد العلي`                                                              | Saved + displayed correctly                                    |        |
| EDGE-07 | Date in future for DOB                        | P1       | Tenant DOB = 2030-01-01                                                          | Validation error                                               |        |
| EDGE-08 | Concurrent edit (two browsers)                | P2       | Open same tenant in 2 tabs → edit differently → save both                        | Second save warns about stale data, or last-write-wins logged  |        |
| EDGE-09 | Browser back after submit                     | P2       | Submit form → back button                                                        | No duplicate submission                                        |        |
| EDGE-10 | Session expiry mid-edit                       | P2       | Open form → wait 13 hours → submit                                               | Redirect to login, no data loss (form draft restored)          |        |

---

## 14. Performance & UX (manual feel)

| ID     | Title                                | Pass criteria                                       | Result |
| ------ | ------------------------------------ | --------------------------------------------------- | ------ |
| PERF-01 | Login page first paint              | < 1.5 s on dev, < 3 s on SmarterASP                 |        |
| PERF-02 | Dashboard load                      | < 2 s                                               |        |
| PERF-03 | Tenant list with 500 rows           | < 3 s; pagination works                             |        |
| PERF-04 | Civil ID parse round-trip           | < 500 ms                                            |        |
| PERF-05 | Civil ID Gemini scan                | < 6 s end-to-end                                    |        |
| PERF-06 | Invoice PDF generation              | < 2 s                                               |        |

---

## 15. Cross-browser sanity

Run SMK-01 through SMK-07 in each:

| Browser            | Smoke pass? | Notes |
| ------------------ | ----------- | ----- |
| Chrome (latest)    |             |       |
| Edge (latest)      |             |       |
| Firefox (latest)   |             |       |
| Safari (iOS)       |             |       |
| Chrome (Android)   |             |       |

---

## 16. Bug reporting template

When a test fails, log a bug with this template:

```
TITLE:        [Module] Short description of what's broken
TEST CASE ID: TC-XX-NN
ENVIRONMENT:  local dev | SmarterASP | other
BUILD HASH:   (from /Account/Login footer)
SEVERITY:     P0 / P1 / P2
STEPS:        1.
              2.
              3.
EXPECTED:     what the test case said
ACTUAL:       what actually happened
SCREENSHOT:   path or attach
LOG SNIPPET:  from logs/stdout_*.log if relevant
```

---

## Appendix A — How to trigger Hangfire jobs manually

Some tests (INV-02, INV-04, PAY-K-04) wait on background jobs that
normally run nightly. To trigger them now:

1. Log in as Admin
2. Open `/hangfire` (Hangfire dashboard)
3. Go to **Recurring Jobs**
4. Find the job (e.g. `daily-rent-invoicing`) → click **Trigger now**
5. Watch the **Succeeded** column for the job to finish (~10 seconds)

---

## Appendix B — Useful SQL for verification

Run from MSSQL Manager or SSMS connected to the test DB:

```sql
-- Latest invoices for a tenant
SELECT TOP 20 InvoiceId, InvoiceType, Amount, DueDate, Status
FROM Invoices WHERE TenantId = ?
ORDER BY InvoiceId DESC;

-- Pending notifications (should drain within 1 minute)
SELECT NotificationId, Channel, Status, Attempts, LastError
FROM Notifications WHERE Status = 'Pending';

-- Audit trail of a specific entity
SELECT Timestamp, ActorUserId, Action, [Before], [After]
FROM AuditLogs
WHERE EntityType = 'Tenant' AND EntityId = ?
ORDER BY Timestamp DESC;

-- Hangfire failures (should be empty in steady state)
SELECT * FROM HangFire.Job WHERE StateName = 'Failed' ORDER BY Id DESC;
```

---

*Document version: 2026-06-09 · Sprint 23*
*Maintainer: keep test IDs stable — append new ones, don't renumber.*
