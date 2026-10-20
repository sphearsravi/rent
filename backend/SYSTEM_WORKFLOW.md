# RentCollection — System Workflow

**Product:** ALRTAM Property Management Suite
**Branding:** VIIJAR — *Pay Rent Easy*
**Target market:** Kuwait residential & commercial landlords
**Stack:** ASP.NET Core 8 · SQL Server · Hangfire · QuestPDF

This document explains, from a business-user perspective, what the system
does, who uses it, and how data flows from move-in to rent collection to
move-out. Read it first; the technical docs ([ARCHITECTURE.md](ARCHITECTURE.md),
[APP_FLOW.md](APP_FLOW.md)) go deeper after.

---

## 1. What the system is for

ALRTAM owns and manages residential + commercial property in Kuwait. The
RentCollection system is the **single source of truth** for:

- **Inventory** — who owns what (Companies → Properties → Units)
- **Tenancy** — who lives where (Tenants → Leases → Units)
- **Money** — what's owed and what's been paid (Invoices → Payments → K-NET / Bank / Cash)
- **Maintenance** — what's broken and who's fixing it (Requests → Vendors → Photos)
- **Compliance** — what proves it (Documents linked to any of the above)
- **Communication** — what was sent and when (Notifications, Audit Log)

Two front doors:
- **Staff portal** at `/` — admins, accountants, managers operate the business
- **Tenant portal** at `/TenantPortal/Login` — tenants pay rent, raise maintenance, upload documents

---

## 2. Who uses it (roles)

| Role          | What they see                                       | Typical user        |
| ------------- | --------------------------------------------------- | ------------------- |
| **SuperAdmin**| Everything; can manage Companies and Users          | System owner        |
| **Admin**     | Everything within their Company                     | ALRTAM operations head |
| **Manager**   | Properties, Units, Tenants, Leases, Maintenance     | Building manager    |
| **Accountant**| Payments, Invoices, Reports, Approvals              | Finance team        |
| **Tenant**    | Their own dashboard, invoices, payments, requests   | The renter          |

Roles drive the sidebar menu and every controller action. A Manager
clicking `/Payments` gets redirected; an Accountant clicking `/Maintenance`
can view but not create.

The active user's **Company** is read from their JWT/session claim and used
to filter every query — Company A's admin can never see Company B's
tenants. This is enforced server-side, not just hidden in the UI.

---

## 3. The big picture (data hierarchy)

```
Company
   └── Property (building, with PACI ID and Governorate)
         └── Unit (flat / shop, with monthly rent ceiling)
               └── Lease (one active per Unit at a time)
                     └── Tenant (the renter)
                           ├── Invoices (rent / deposit / insurance / late-fee)
                           ├── Payments (Cash / Bank Transfer / K-NET)
                           ├── Maintenance Requests
                           └── Documents (Civil ID, contracts, receipts…)
```

Two things never move:
1. **Property → Unit** is fixed inventory. You create both up front.
2. **Lease → Unit** is exclusive at any given time. A Unit can't have two
   Active leases. The system enforces this and the daily reconcile job
   keeps `Unit.Status` (`Vacant` / `Occupied`) in sync.

---

## 4. The end-to-end workflow

### Step 1 — Onboard a property

**Who:** Admin or Manager
**Where:** `/Properties/Create`

Fields in the new revamped form ([Sprint 23](RentCollection.Web/Views/Properties/Create.cshtml)):
- **Essentials** (required to save): Property Name, Building PACI, Type
  (Apartment / House / Commercial / Condo), Governorate, Number of Units,
  Company
- **More details** (collapsed by default): Full address (Area / Block /
  Street / Building Number), Purchase Date, Property Value, Property Manager
- **Documents** (optional): Title deed, building permit, fire-safety cert,
  floor plan, insurance policy — anything you want kept on file

After save, the system **auto-prompts you to create the Units** below
that property (or you do it later via `/Units/Create`).

### Step 2 — Onboard a tenant

**Who:** Admin or Manager
**Where:** `/Tenants/Create`

The form is split into three zones:
- **Essentials** — First/Last Name, Email, Phone, **Client Civil ID
  (PACI)**, Building PACI
- **More details** (collapsed) — DOB, Government ID, Employment Status,
  Emergency Contact, Property/Unit
- **Documents** — Civil ID scan, Passport, Visa, Salary Certificate, etc.

**Killer feature — Civil ID auto-fill** (Sprint 21):
- Type the 12-digit Civil ID → system parses it (PACI checksum algorithm)
  → fills Date of Birth automatically
- Or click **Scan Civil ID card** → upload a photo → Google Gemini Vision
  reads it and pre-fills First Name, Last Name, Civil ID, DOB
- AI-extracted fields show an amber border so the operator knows to
  double-check before saving

**Auto-CompanyId** (Sprint 22): The CompanyId field is no longer asked
on the form. The system reads it from the logged-in admin's session
claim and assigns automatically — preventing cross-company data leaks.

### Step 3 — Create the lease

**Who:** Admin, Manager (or Accountant for deposit-pending state)
**Where:** `/Leases/Create`

Form structure:
- **Essentials** — Building → Unit (cascaded, type-ahead search) →
  Tenant, Start/End Date, Monthly Rent, Security Deposit
- **More details** (collapsed) — Payment Due Day, Late Fee Amount &
  Grace Period, Lease Type (Fixed/Month-to-Month/Annual), Insurance
  (Amount, Frequency, Start Date)
- **Documents** — Signed lease PDF, deposit receipt, etc.

**Lease lifecycle:**
1. If **Security Deposit > 0** → lease lands as **Pending** until the
   deposit invoice is collected. The system blocks rent invoicing while
   Pending.
2. **Pending → Active** happens via the **Activate** action, which
   atomically: marks the lease Active, sets the unit to Occupied, and
   issues the deposit invoice.
3. **Active → Expired** happens automatically when end date passes
   (daily Hangfire job).
4. **Active → Terminated** happens manually via the Terminate action
   (with a required reason).
5. **Active → Renewed** when a new lease is chained off the old one.

### Step 4 — Auto-invoicing

The system **never asks an operator to "send an invoice"** — Hangfire
generates them on schedule:

- **Daily 02:00 UTC** — for every Active lease whose `PaymentDueDay`
  matches today, create a `Rent` invoice with the monthly rent amount
  and a due date.
- **Insurance** — based on Frequency (Monthly / Annual / OneTime),
  generated on the InsuranceStartDate cadence.
- **Late fee** — applied after the grace period if the invoice is still
  unpaid, capped at the lease's `LateFeeAmount`.

Invoices land in `/Invoices`, visible to the Accountant and to the
Tenant in their portal.

### Step 5 — Tenant pays

Three payment paths, all reconciled into the same `Payments` table:

#### 5a. K-NET (Kuwait local card payment)
- Tenant logs into `/TenantPortal/Login` → opens an unpaid invoice →
  clicks **Pay with K-NET**
- System hits the K-NET gateway with a `TranportalId`/`TranportalPwd`
  signature → tenant is redirected to K-NET's hosted form → returns to
  `/api/payments/knet/response` with the result
- On success: payment recorded, invoice marked Paid, receipt issued via
  email/SMS automatically
- All requests/responses logged for audit + dispute handling
- A daily 03:00 UTC reconcile job re-checks any "stuck in unknown" K-NET
  payments against the gateway and self-heals

#### 5b. Bank Transfer
- Tenant logs in → uploads transfer-confirmation screenshot
- Stays in **Pending Approval** status
- Accountant opens `/PaymentApproval`, reviews the screenshot, marks
  Approve or Reject (with note)
- On Approve: payment moves to Confirmed, invoice marked Paid, receipt
  sent

#### 5c. Cash (staff-recorded)
- Tenant pays at the office → staff opens the invoice → records the
  cash payment + receipt number
- No approval needed (assumes staff oversight)
- Payment immediately Confirmed

### Step 6 — Receipts & notifications

After every confirmed payment, a background job:
1. Generates a PDF receipt (QuestPDF) with ALRTAM branding
2. Saves it to the tenant's Documents
3. Sends it to the tenant by email (SMTP) and SMS (where configured)
4. Notifies the Accountant + Manager dashboards (real-time bell icon)

All of this is queued in the `Notifications` table with `Pending` /
`Sent` / `Failed` status and an `Attempts` counter — retried up to 5
times if the email server is down.

### Step 7 — Maintenance requests

**Who initiates:** Tenant (via portal) or Manager (on their behalf)
**Where:** `/Maintenance`

Flow:
1. Tenant raises a request from their portal, attaches photos
2. Manager triages: assigns priority, picks a Vendor from the
   `/Vendors` list, sets target completion date
3. Vendor visits, work is done, Manager closes the request with
   completion photos and cost
4. Cost optionally rolls into a "Maintenance Recovery" invoice if the
   lease terms allow chargebacks

Maintenance is also where the daily KPI "Average Resolution Time"
on the Dashboard comes from.

### Step 8 — Move-out

**Who:** Manager
**Where:** Lease detail page → **Terminate** action

Steps the system performs atomically:
1. Lease status → `Terminated`
2. Unit status → `Vacant` (eligible for next lease)
3. Stops future rent invoice generation
4. Calculates security deposit refund (deposit minus damages from
   maintenance requests minus outstanding invoices)
5. Issues a `Refund` record to the Accountant queue
6. Once refund is paid out, generates final close-out PDF report

---

## 5. What the dashboard shows

`/Dashboard` is the at-a-glance view per role:

- **Admin / Manager**: Occupancy %, total Units, Active Leases,
  upcoming Lease expiries (30 days), open Maintenance requests by
  priority, current month's rent target vs collected
- **Accountant**: Today's collections by channel, pending payment
  approvals, overdue invoice count, K-NET reconciliation status
- **Tenant**: Their lease summary, next rent due (amount + date),
  unpaid invoice count, open maintenance requests, recent payments

KPI tiles refresh via a server-side query each page load — no
JavaScript polling.

---

## 6. Reports

`/Reports` produces Excel (EPPlus) and PDF (QuestPDF) outputs:

- **Rent collection by month** — by Property, by Unit, by Tenant
- **Outstanding invoices** — aged buckets (0-30, 31-60, 61-90, 90+ days)
- **Occupancy report** — historical, by Property
- **Maintenance cost** — by Vendor, by Property
- **K-NET vs Bank vs Cash** — channel breakdown
- **Tenant ledger** — full history per tenant (one-click export)

Every report is filterable by Date Range, Property, Company.

---

## 7. Documents (cross-cutting)

A polymorphic Document store:
- One **Document** row per uploaded file (path, MIME, size, uploader)
- One or more **DocumentLink** rows tying it to:
  - A Tenant (Civil ID, passport)
  - A Lease (signed contract, deposit receipt)
  - A Property (deed, permit, floor plan)
  - A Maintenance Request (before/after photos)
  - A Payment (bank transfer screenshot)

That means one file can be reused across entities (e.g. a Civil ID
attached to both Tenant and their first Lease). Deleting a Document
deletes all its links; deleting a Link doesn't touch the Document.

`/Documents` is the cross-entity browser — useful for "find me every
Civil ID we have on file" or "all expired insurance certificates."

---

## 8. Audit & compliance

Every state-changing action (Create / Update / Delete / Approve /
Terminate / Refund / Login) writes one row to `AuditLogs` with:

- Timestamp (UTC)
- Actor (UserId + IP)
- Action (string enum)
- Entity Type + Entity Id
- Before / After JSON snapshots

The Admin can browse this at `/Audit`. Auditors love it; the table
grows fast — there's a monthly archive job to keep it lean.

---

## 9. Background automation (Hangfire jobs)

Visible to admins at `/hangfire` (the Hangfire dashboard):

| Job                            | Schedule         | What it does                                |
| ------------------------------ | ---------------- | ------------------------------------------- |
| Daily rent invoicing           | 02:00 UTC        | Issues today's rent invoices                |
| Insurance invoicing            | 02:15 UTC        | Issues insurance per frequency              |
| Late-fee assessment            | 02:30 UTC        | Adds late fees past grace period            |
| Lease expiry sweep             | 02:45 UTC        | Marks expired leases, vacates units         |
| Unit status reconcile          | 01:30 UTC        | Aligns Unit.Status with active leases       |
| K-NET reconciliation           | 03:00 UTC        | Re-checks ambiguous K-NET responses         |
| Notification dispatcher        | every 1 minute   | Sends pending emails/SMS, retries failures  |
| Hangfire heartbeat             | every 30 seconds | Keeps the job server alive                  |

All jobs are idempotent — running them twice does not double-invoice.

---

## 10. Security model in two sentences

- Auth is cookie-based for the staff portal (`RentCollectionAuth` cookie,
  12-hour sliding expiry) and JWT-based for the tenant mobile app
  (60-minute access token, 30-day refresh token).
- Authorization is enforced **at every controller method** with
  `[Authorize(Roles = "...")]` AND **filtered by CompanyId at every
  query** — the UI hides menus, the server hides data.

For demo deployments without TLS (e.g. the SmarterASP `ktempurl.com`
URL), `Demo:LoginEnabled=true` in `appsettings.json` relaxes the cookie
`Secure` flag — set to `false` once HTTPS is provisioned.

---

## 11. The form revamp (Sprint 23)

The Tenant / Lease / Property create forms were redesigned around three
ideas:

1. **Essentials first, everything else collapsed.** New operators get
   shown the 4-5 fields they actually have to fill. Advanced fields hide
   behind a "More details" toggle that remembers the operator's choice in
   `sessionStorage`.
2. **Save & add another.** A second submit button posts the form and
   bounces the operator straight back to a fresh Create form — built for
   batch onboarding of multiple Tenants or Properties in one sitting.
3. **Draft autosave.** Every keystroke autosaves to `sessionStorage`.
   A tab reload or accidental back-navigation doesn't lose work — the
   next time you open the form, a green "Draft restored" chip appears.

Shared styling lives in [forms.css](RentCollection.Web/wwwroot/css/forms.css);
shared behaviour in [form-helpers.js](RentCollection.Web/wwwroot/js/form-helpers.js).
Adding new forms to this style is opt-in: add `class="form-revamp"` to
the `<form>` wrapper and use the `.fr-zone` / `.fr-field` markup.

---

## 12. Deployment topology

**Development** (Ravi's workstation): `localhost:5032`, SQL Express
(`RAVIWST\SQLEXPRESS\RentCollectionDB`), Hangfire SQL in the same DB.

**Production / Demo** (SmarterASP.NET shared hosting):
- Site: `http://<DEMO_HOST>`
- App pool: `alrtam-001` (.NET 8, **must be 64-bit** — 32-bit breaks
  SQLite/EPPlus native deps)
- DB: SmarterASP MSSQL (`<DB_HOST>`), connection string in
  `appsettings.json` (rotate after every transcript that touches it!)
- Secrets to set via env var on real prod: `Jwt__Key`, `Gemini__ApiKey`,
  `Knet__TranportalPwd`. Never hard-code these.
- FTP deploy: drop `app_offline.htm` at site root → upload changed DLLs
  → delete `app_offline.htm`. This safely releases the IIS DLL locks
  for the new build.

See [project_rentcollection_smarterasp](.) memory for the gotcha list
(Sentry needs `Dsn=""`, 32-bit pool is wrong default, etc.).

---

## 13. Glossary

| Term            | Meaning                                                |
| --------------- | ------------------------------------------------------ |
| **PACI**        | Public Authority for Civil Information (Kuwait). Both buildings and citizens have a unique PACI ID. |
| **Civil ID**    | The 12-digit citizen PACI ID. Encodes century + DOB + gender + checksum. |
| **K-NET**       | Kuwait's national debit-card payment network. The standard online payment rail in Kuwait. |
| **Governorate** | Kuwait's six provinces (Al Asimah, Hawalli, Farwaniya, Ahmadi, Jahra, Mubarak Al Kabeer). |
| **KWD**         | Kuwaiti Dinar. **3 decimal places** (not 2) — the system enforces this throughout. |
| **Hangfire**    | Background job framework. Powers all scheduled tasks. |
| **QuestPDF**    | PDF generation library. Used for invoices, receipts, reports. |
| **EPPlus**      | Excel file generation library. Used for downloadable reports. |

---

*Document version: 2026-06-04 · Sprint 23*
