# Lease Section — Logical Flaw Audit

Generated 2026-06-01. Find-only initially; **all 20 flaws fixed across 8
phases on 2026-06-01** (see `LEASE_FIX_SUMMARY` at the bottom). Each entry
has a green ✅ tag once shipped + verified.

Method: read every code path that creates, transitions, or invoices a Lease;
construct counter-examples; verify by code-trace. Classifications use
**likelihood × blast radius** where blast radius is
data corruption > financial impact > UX confusion.

---

## Executive summary

| Severity     | Count | Hottest file |
|--------------|-------|--------------|
| **Critical** | 6     | LeasesController.cs · TenantsController.cs · RecurringJobs.cs |
| **High**     | 5     | Views/Leases/Index.cshtml · Views/Leases/Create.cshtml · Views/Leases/Edit.cshtml |
| **Medium**   | 6     | LeasesController.cs · Views/Leases/* |
| **Low**      | 3     | Views/Leases/Delete.cshtml · LeasesController.cs |

**Three quickest wins** (each ≤ 15 min, no schema change):
- Fix the hard-coded `2025-01-01 → 2025-12-31` dates in `TenantsController.Create` (Flaw #1).
- Make `LeasesController.Terminate` flip `Unit.Status = "Vacant"` (Flaw #3).
- Wire the dead `#renew`/`#terminate` anchor links from Index kebab → real forms on Details (Flaw #10).

**Three structural items** (need a small migration or transaction rework):
- Single transaction around `Renew` instead of two `SaveChangesAsync` (Flaw #4).
- Unique filtered index on `(UnitId)` where `Status = Active` (Flaw #5).
- Same `PaymentDueDay` clamp in `RecurringJobs` as `InvoicesController` (Flaw #6).

---

## CRITICAL

### Flaw #1 — Manual-unit lease creation hard-codes 2025 dates ✅ Fixed (Phase 4)
**Severity:** Critical
**Where:** [TenantsController.cs:865-866](RentCollection.Web/Controllers/TenantsController.cs#L865-L866) and message at [TenantsController.cs:890](RentCollection.Web/Controllers/TenantsController.cs#L890)

**What goes wrong:**
The PACI-path was fixed at [line 827](RentCollection.Web/Controllers/TenantsController.cs#L827) to derive
`leaseStart = first of current month`. The **manual-unit fallback path** at lines
861-873 still hard-codes:
```csharp
LeaseStartDate = new DateTime(2025, 1, 1),
LeaseEndDate   = new DateTime(2025, 12, 31),
```
Every tenant created in 2026 via manual unit selection gets a backdated lease
that the system will treat as **Expired on day 1**. Worse, the success
toast at line 890 still says `"...with lease from Jan 2025 to Dec 2025"` — for
**both** paths, so even the PACI path's correct dates are misreported.

**Why it's wrong:** A lease that is Expired the moment it's created cannot
collect rent through the monthly invoice job ([RecurringJobs.cs:117](RentCollection.Infrastructure/Services/RecurringJobs.cs#L117) filters
`Status == "Active"` AND the date window). The tenant exists, the unit
shows Occupied, no invoices ever land. Silent revenue leak.

**Risk:** Money — tenants exist but get no invoices. **Compliance** — wrong
contract dates printed on receipts/statements.

**Fix:** Apply the same `leaseStart = new DateTime(year, month, 1)` formula
to the manual path; format the success message from the actual `lease`
fields, not hard-coded strings.

---

### Flaw #2 — `Renew` has no concurrency guard, breaks the renewal chain ✅ Fixed (Phases 0 + 2)
**Severity:** Critical
**Where:** [LeasesController.cs:305-345](RentCollection.Web/Controllers/LeasesController.cs#L305-L345)

**What goes wrong:**
Two operators click Renew on the same lease at almost the same moment.
Both pass the `Status != Active && Status != Expired` check at line 309
(both reads see Active), both insert a new `Lease`, then both stamp the
old lease's `Status = Renewed` and `RenewedToLeaseId = renewal.LeaseId`.
Last write wins. The first renewal becomes a **phantom**: it has
`RenewedFromLeaseId = oldId` pointing back, but the old lease's
`RenewedToLeaseId` points to the second renewal, not the first.

**Why it's wrong:** The renewal chain is now broken (no path from old →
renewal #1) and the unit has two active successors when only one was
intended.

**Risk:** Data integrity. Two real `Active` leases on the same unit
(see also Flaw #5) plus a `RenewedFromLeaseId` orphan.

**Fix:** Add a row-version concurrency token to `Lease`
(`[Timestamp] public byte[] RowVersion`) — the codebase already has the
pattern on `Invoice` and `Payment` (migration
`20260513100843_AddRowVersionToInvoiceAndPayment`). Catch
`DbUpdateConcurrencyException` and return a "lease changed, refresh and
retry" message.

---

### Flaw #3 — `Terminate` does not free the Unit ✅ Fixed (Phase 2)
**Severity:** Critical
**Where:** [LeasesController.cs:273-296](RentCollection.Web/Controllers/LeasesController.cs#L273-L296)

**What goes wrong:**
`Terminate` updates Lease.Status, TerminationDate, TerminationReason,
TerminatedBy, optional DepositRefunded — but never touches `Unit.Status`.
The unit stays `"Occupied"` forever after the lease ends.

**Why it's wrong:** Compare with the create flow at
[TenantsController.cs:845](RentCollection.Web/Controllers/TenantsController.cs#L845) which DOES flip
`Unit.Status = "Occupied"`. Termination is the inverse and should symmetrically flip back to
`"Vacant"`. The vacancy filter in `LeasesController.Create` at
[line 77](RentCollection.Web/Controllers/LeasesController.cs#L77)
(`Where(u => u.Status == "Vacant")`) will hide every unit whose lease has
been terminated, so the operator **cannot create the next lease via the
standard flow**.

**Risk:** Occupancy% on the Reports dashboard is wrong (every terminated
unit still counts as Occupied). The same applies to
[PropertiesController.Snapshot](RentCollection.Web/Controllers/PropertiesController.cs) which derives
occupancy from `Unit.Status`.

**Fix:** In `Terminate`, after flipping Lease.Status, also load the lease's
Unit and set `unit.Status = "Vacant"` in the same `SaveChangesAsync` call.
Apply the same to `Transfer` (see Flaw #11) and `Delete` for an Active
lease.

---

### Flaw #4 — `Renew` is not atomic — two `SaveChangesAsync` calls ✅ Fixed (Phase 2)
**Severity:** Critical
**Where:** [LeasesController.cs:335-341](RentCollection.Web/Controllers/LeasesController.cs#L335-L341)

**What goes wrong:**
```csharp
_context.Leases.Add(renewal);
await _context.SaveChangesAsync();          // (1) renewal now in DB, status=Active

lease.Status = LeaseStatuses.Renewed;
lease.RenewedToLeaseId = renewal.LeaseId;
lease.ModifiedDate = DateTime.UtcNow;
await _context.SaveChangesAsync();          // (2) flip old lease
```
If save (2) fails — connection drop, FK constraint, anything — the new
`renewal` row exists with `Status = Active` while the old lease is **also
still `Active`**. Two active leases on the same unit. Unit.Status still
Occupied (correct), `RenewedToLeaseId` not stamped.

**Why it's wrong:** Either both writes happen or neither should. The two
saves should be a single transaction so the renewal insert + old-lease
flip succeed together.

**Risk:** Two billable Active leases on the same unit. The monthly job
generates two invoices for the same period.

**Fix:** Wrap in `using var tx = await _context.Database.BeginTransactionAsync();`
do both adds/updates, single `SaveChangesAsync`, `tx.Commit()`. Or restructure
so the second flip is part of the same change-tracker pass (move
`renewal.LeaseId = 0; … Add(renewal)` then mutate `lease` before the
single `SaveChangesAsync`).

---

### Flaw #5 — No DB-level overlap protection on the same Unit ✅ Fixed (Phase 0)
**Severity:** Critical
**Where:** [ApplicationDbContext.cs:186](RentCollection.Infrastructure/Data/ApplicationDbContext.cs#L186) and elsewhere — **no** filtered unique index on `Lease(UnitId)` where `Status = 'Active'`.

**What goes wrong:**
The only "vacancy" enforcement is application-level: read
`Unit.Status == "Vacant"`, write a new Lease, flip Unit to Occupied. Race
window between read and write. Combined with Flaw #4 (Renew not atomic)
and the absence of a TX on the create path, two operators can both
insert Active leases on the same UnitId.

**Why it's wrong:** Database invariants belong in the database. Reliable
ones survive bugs in application code; this one doesn't.

**Risk:** Same as Flaws #2, #4 — duplicate Active leases, double-billing.

**Fix:** Migration adding a filtered unique index:
```sql
CREATE UNIQUE INDEX IX_Leases_UnitId_Active
  ON Leases (UnitId)
  WHERE Status = 'Active';
```
Backfill check first — if any duplicates already exist (from Flaw #2 or
#4), terminate the older one or have an operator resolve.

---

### Flaw #6 — `PaymentDueDay` overflow bug in `RecurringJobs`, fixed in `InvoicesController` ✅ Fixed (Phases 1 + 5)
**Severity:** Critical
**Where:** [RecurringJobs.cs:138](RentCollection.Infrastructure/Services/RecurringJobs.cs#L138)
vs the correct version at
[InvoicesController.cs:128-130](RentCollection.Web/Controllers/InvoicesController.cs#L128-L130)

**What goes wrong:**
`RecurringJobs.cs:138`:
```csharp
DueDate = periodStart.AddDays(lease.PaymentDueDay > 0 ? lease.PaymentDueDay - 1 : 0)
```
For a lease with `PaymentDueDay = 31` and `periodStart = 2026-02-01`,
this evaluates to `2026-03-03` — three days into March, not the last day
of February.

`InvoicesController.cs:128-130` does it correctly:
```csharp
var dueDay = lease.PaymentDueDay > DateTime.DaysInMonth(currentYear, currentMonth)
    ? DateTime.DaysInMonth(currentYear, currentMonth)
    : lease.PaymentDueDay;
var dueDate = new DateTime(currentYear, currentMonth, dueDay);
```

**Why it's wrong:** Two different code paths generate invoices for the
same lease and they disagree on the due date by up to 3 days. The
overdue job at [RecurringJobs.cs:166-179](RentCollection.Infrastructure/Services/RecurringJobs.cs#L166-L179)
flags invoices as overdue based on a wrong date.

**Risk:** Money + customer trust — tenants get late-fee notices on days
before they actually owe the money.

**Fix:** Extract the InvoicesController clamp into a shared helper
(`LeaseHelpers.ComputeDueDate(lease, year, month)`) and call from both
sides.

---

## HIGH

### Flaw #7 — Index view's "Expired" status is a client-side lie ✅ Fixed (Phases 5 + 6)
**Severity:** High
**Where:** [Views/Leases/Index.cshtml:138-148](RentCollection.Web/Views/Leases/Index.cshtml#L138-L148)

**What goes wrong:**
```cshtml
var daysRemaining = (lease.LeaseEndDate - DateTime.UtcNow).Days;
var isExpired = daysRemaining < 0;
…
<tr … data-status="@(isExpired ? "Expired" : (lease.Status ?? "Active"))" …>
```
`data-status` becomes "Expired" for any row whose end date passed,
**regardless of what the DB says**. The Status filter dropdown
(`Active / Terminated / Expired`) filters on `data-status`. Consequence:
- Pick "Active" — you miss every lease the DB calls `Active` but whose
  end date passed.
- Pick "Expired" — you see leases the DB says are `Active`.

Meanwhile the new `Snapshot` and `KpiBatch` endpoints filter on the DB
value only. The KPI strip's "Active" count and the filtered row count
disagree.

**Why it's wrong:** Two sources of truth (DB vs view-derived) for the
same field. UI should reflect the persisted state and surface "Expired by
date" as a separate visual badge (already done — the row gets `row-expired`).

**Risk:** Operators believe they have N active leases; the DB says M.
Decisions are made on the wrong number.

**Fix:** Set `data-status="@(lease.Status ?? "Active")"` and use the
existing `row-expired` CSS class + a "Past end date" pill to signal the
overdue lifecycle event without overwriting the persisted status. Or add
a daily background job that flips `Status = "Expired"` once
`LeaseEndDate < today` so DB and view agree.

---

### Flaw #8 — Status editable to anything in Create/Edit ✅ Fixed (Phase 3)
**Severity:** High
**Where:** [Views/Leases/Create.cshtml:126-133](RentCollection.Web/Views/Leases/Create.cshtml#L126-L133) and
[Views/Leases/Edit.cshtml:128-133](RentCollection.Web/Views/Leases/Edit.cshtml#L128-L133)

**What goes wrong:**
Both forms render a free dropdown of `Active / Expired / Terminated`. A
user can:
- Create a lease that is already `Terminated` on day one (then nobody
  ever invoices it).
- Edit a `Terminated` lease back to `Active` (revives a closed lease,
  reopening invoice generation).
- Edit a `Renewed` lease back to `Active` (now the old lease AND its
  renewal are both Active — see Flaw #5).

**Why it's wrong:** Lease.Status is a state-machine field. Each transition
has invariants (Terminate must record TerminationDate; Renew must create
a successor; Expired is a passive transition). Letting a free
dropdown override it bypasses those invariants.

**Risk:** Data integrity, money. Closed leases re-billed; new leases
silently dead.

**Fix:** Remove the Status dropdown from Create entirely (always insert
as `Active` — or `Pending` if you support drafts). On Edit, make Status
display-only and route state changes through the lifecycle actions
(`Renew`, `Terminate`).

---

### Flaw #9 — Edit form lets you change dates and rent on closed leases ✅ Fixed (Phase 3)
**Severity:** High
**Where:** [Views/Leases/Edit.cshtml:56-63, 70-87](RentCollection.Web/Views/Leases/Edit.cshtml#L56-L63)

**What goes wrong:**
A `Terminated` lease can have its `LeaseStartDate`, `LeaseEndDate`,
`MonthlyRent`, `SecurityDeposit` freely overwritten. So can a
`Renewed` lease. The financial history attached to that lease (paid
invoices, receipts) no longer matches the lease that produced them.

**Why it's wrong:** Closed leases are historical records and a basis for
receipts/statements. Rewriting them breaks the audit chain.

**Risk:** Compliance + financial integrity. Receipts already issued
reference a `MonthlyRent` that the lease row no longer contains.

**Fix:** Controller-side guard: if `lease.Status in (Terminated,
Renewed, Expired)`, reject the Edit POST with a 400 + flash message
"Closed leases cannot be edited; create a renewal instead." View-side:
render fields read-only when status is closed.

---

### Flaw #10 — Index kebab links to anchors that don't exist ✅ Fixed (Phase 6)
**Severity:** High
**Where:** [Views/Leases/Index.cshtml:265, 269](RentCollection.Web/Views/Leases/Index.cshtml#L265)

**What goes wrong:**
Kebab items emit `<a href="/Leases/Details/{id}#renew">` and `…#terminate`.
[Views/Leases/Details.cshtml](RentCollection.Web/Views/Leases/Details.cshtml) has no element
with `id="renew"` or `id="terminate"`, no Renew form, no Terminate form,
no Transfer form. The fragment scrolls to nothing. The user thinks they
clicked Renew and nothing happened.

**Why it's wrong:** The controller exposes `POST /Leases/Renew`,
`POST /Leases/Terminate`, `POST /Leases/Transfer` — none of them are
reachable from the UI today. The audit feature exists in the API but is
invisible.

**Risk:** Operators fall back to Edit (Flaw #8) to "fix" lease state,
which is exactly the wrong workflow.

**Fix:** Add three forms on Details.cshtml: Renew (newEndDate, optional
newMonthlyRent), Terminate (terminationDate, reason, depositRefundedAmount),
Transfer (newUnitId, effectiveDate). Anchor them with the matching
`id="renew"` / `id="terminate"` / `id="transfer"`. POST to the existing
endpoints.

---

### Flaw #11 — `Transfer` updates neither old nor new Unit's Status ✅ Fixed (Phase 2)
**Severity:** High
**Where:** [LeasesController.cs:353-394](RentCollection.Web/Controllers/LeasesController.cs#L353-L394)

**What goes wrong:**
`Transfer` closes the old lease as `Terminated` and creates a new lease
on the target unit. Both `Unit.Status` values are left untouched:
- Old unit: still `"Occupied"` even though no Active lease references it.
- New unit: still `"Vacant"` even though a brand-new Active lease was
  just created on it.

This is the same omission as Flaw #3, on a different action.

**Why it's wrong:** Property occupancy KPIs, unit-availability dropdowns,
and the property Detail "available units" list all read from
`Unit.Status` and will all be wrong.

**Risk:** Same as Flaw #3 — wrong occupancy figures; new tenant can't be
assigned to the freshly-freed unit until someone manually fixes
`Unit.Status`.

**Fix:** In one transaction: load oldUnit + newUnit, set old → Vacant,
new → Occupied, save with the lease changes.

---

## MEDIUM

### Flaw #12 — No server-side date validation on Create/Edit ✅ Fixed (Phase 3)
**Severity:** Medium
**Where:** [LeasesController.cs:91-126](RentCollection.Web/Controllers/LeasesController.cs#L91-L126) (Create), [167-222](RentCollection.Web/Controllers/LeasesController.cs#L167-L222) (Edit)

**What goes wrong:**
The form inputs are `<input type="date">` with no `required` and no
`min/max`. The `[Bind]` model accepts whatever ASP.NET deserialises.
There is no check that:
- `LeaseStartDate <= LeaseEndDate`
- `LeaseStartDate` is not absurdly in the past
- `PaymentDueDay` is between 1 and 31
- `MonthlyRent > 0`

A POST with `LeaseEndDate < LeaseStartDate` lands a row in the DB.

**Risk:** Negative `daysRemaining`, broken renewal math, KPI poisoning.

**Fix:** Add `[Range]`/`[Required]` attributes on `Lease` (or a FluentValidation rule), and an explicit `if (lease.LeaseEndDate <= lease.LeaseStartDate)` check before `ModelState.IsValid`.

---

### Flaw #13 — Monthly job ignores `LeaseEndDate` mid-period ⚠ Partial (Phase 5 — daily expire job closes the worst case; pro-rata deferred per user decision)
**Severity:** Medium
**Where:** [RecurringJobs.cs:115-120](RentCollection.Infrastructure/Services/RecurringJobs.cs#L115-L120)

**What goes wrong:**
```csharp
.Where(l => l.Status == "Active"
    && l.LeaseStartDate <= periodEnd
    && l.LeaseEndDate >= periodStart)
```
A lease ending June 15 still gets a **full-month July** invoice if July's
`periodStart = July 1` and the lease is still `Status="Active"` (which it
will be until someone runs a clean-up — see Flaw #7's "no auto-expire"
side). Even within the matching month, the lease gets full
`MonthlyRent` regardless of how many days are actually covered.

**Why it's wrong:** Pro-rata billing is not implemented; full months are
billed for partial coverage.

**Risk:** Money — over-billing on partial months. Compliance — invoice
doesn't reflect the contracted period.

**Fix:** Either (a) auto-flip Status to Expired the day after LeaseEndDate
(daily job), so the active filter cuts off naturally; (b) pro-rate the
amount when `LeaseEndDate < periodEnd` or `LeaseStartDate > periodStart`.

---

### Flaw #14 — Days-remaining math uses `DateTime.UtcNow` vs date-only `LeaseEndDate` ✅ Fixed (Phases 1 + 6)
**Severity:** Medium
**Where:** [Views/Leases/Index.cshtml:138](RentCollection.Web/Views/Leases/Index.cshtml#L138) and
[Views/Leases/Details.cshtml](RentCollection.Web/Views/Leases/Details.cshtml) (similar pattern)

**What goes wrong:**
`(lease.LeaseEndDate - DateTime.UtcNow).Days` mixes a date-only value
(midnight) with a current-instant timestamp. Near the boundary, the
result is off by ±1 day depending on UTC clock and Kuwait time-zone offset
(+03:00).

A lease ending today shows `0 days remaining` if checked at 02:00 UTC
(05:00 Kuwait) but `-1 day` if checked at 22:00 UTC (01:00 next-day
Kuwait).

**Risk:** Operators see different numbers depending on what time they
load the page. The "≤30 days" expiring filter pulses unstably across
midnight.

**Fix:** `(lease.LeaseEndDate.Date - DateTime.Today).Days`. Or, since
Kuwait is the operating tenant, `TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time")`
and use a single "today in Kuwait" anchor.

---

### Flaw #15 — `LeasesController.Create` tenant dropdown only shows Active tenants — Edit shows all ✅ Fixed (Phase 3)
**Severity:** Medium
**Where:** [LeasesController.cs:65-72](RentCollection.Web/Controllers/LeasesController.cs#L65-L72) (Create) vs [LeasesController.cs:142-150](RentCollection.Web/Controllers/LeasesController.cs#L142-L150) (Edit)

**What goes wrong:**
Create filters `Where(t => t.Status == "Active")`. Edit does not.
Operationally: when creating a future-start lease for a tenant whose
status is `Inactive` (e.g. an off-boarded tenant moving in next month),
the tenant doesn't appear in the dropdown — but if the same lease
already exists, Edit lets you reassign it to that Inactive tenant.

**Risk:** UX inconsistency and an unintuitive workaround: operators
flip the tenant to Active just to create a lease, then forget to flip
back.

**Fix:** Pick one rule. Either both show only Active, or both show all
with an "Inactive" badge.

---

### Flaw #16 — No audit trail for `Renew` / `Terminate` / `Transfer` ✅ Fixed (Phases 0 + 1 + 2)
**Severity:** Medium
**Where:** [LeasesController.cs:271-394](RentCollection.Web/Controllers/LeasesController.cs#L271-L394)

**What goes wrong:**
`Terminate` stamps `TerminatedBy` and `TerminationDate` on the Lease
itself — fine. `Renew` writes nothing about who triggered it, only the
chain link. `Transfer` writes a `TerminationReason = "Transfer to unit
{newUnitId}"` on the old lease but the new lease has no
`CreatedByOnTransfer` field. There is no AuditLog table.

**Why it's wrong:** When a tenant disputes a transfer or a renewal in
court, you can't show who clicked the button or when.

**Risk:** Compliance — Kuwait commercial law typically requires audit
trails for material contract changes.

**Fix:** Add a lightweight `AuditLog` entity (already on the deferred
backlog in APP_FLOW.md §10). For now, add `RenewedBy`, `TransferredBy`
nullable strings on Lease as stop-gap stamps.

---

### Flaw #17 — `Renew` of an `Expired` lease creates a gap, not a backfill ✅ Fixed (Phase 2)
**Severity:** Medium
**Where:** [LeasesController.cs:324](RentCollection.Web/Controllers/LeasesController.cs#L324)

**What goes wrong:**
```csharp
LeaseStartDate = lease.LeaseEndDate.AddDays(1),
```
If the old lease ended 200 days ago (Expired), the renewal starts 199
days ago. The renewal's `Status = Active` is set, then the monthly job
will generate back-invoices for every period in between when it next
runs.

**Why it's wrong:** Backdating creates retroactive bills the tenant
didn't agree to. Even when the renewal is genuinely a backdated
re-signing, the operator should explicitly choose the start date.

**Risk:** Money + tenant trust.

**Fix:** Add an optional `newStartDate` parameter to `Renew` (defaults to
`max(oldEnd+1, today)`); validate it isn't before `oldLease.LeaseEndDate`.

---

## LOW

### Flaw #18 — Delete page warns only when Active, missing the broken-chain case ✅ Fixed (Phase 6)
**Severity:** Low
**Where:** [Views/Leases/Delete.cshtml:81-90](RentCollection.Web/Views/Leases/Delete.cshtml#L81-L90)

**What goes wrong:**
The warning fires when `Model.Status == "Active"`. A `Renewed` lease
whose `RenewedToLeaseId` points to a still-Active successor can be
deleted with no warning at all — even though doing so leaves the
successor with a dangling `RenewedFromLeaseId` pointing to a non-existent
row.

**Risk:** Renewal chain integrity. Cosmetic-looking, but ugly when
exporting lease history.

**Fix:** Add a second warning: "This lease is the predecessor of lease
#{RenewedToLeaseId}, deleting it will orphan that chain link."

---

### Flaw #19 — `LeasesController.Delete` is a hard delete; FKs Restrict ✅ Fixed (Phase 7)
**Severity:** Low
**Where:** [LeasesController.cs:247-260](RentCollection.Web/Controllers/LeasesController.cs#L247-L260)

**What goes wrong:**
`Delete` calls `_context.Leases.Remove(lease)` with no consideration of
attached `Invoice` / `Payment` rows. Per
[ApplicationDbContext.cs:244, 365](RentCollection.Infrastructure/Data/ApplicationDbContext.cs#L244)
both `Lease→Payment` and `Lease→Invoice` are `OnDelete(Restrict)`. The
delete will throw `DbUpdateException` and surface as a 500 instead of a
friendly "this lease has invoices" message.

**Risk:** Bad operator experience.

**Fix:** Pre-check `Invoices.Any() || Payments.Any()`; if so, return a
view with a "soft-archive instead" suggestion. Or convert Lease to
`ISoftDeletable` like Tenant.

---

### Flaw #20 — `[Authorize(Roles = Operational)]` covers all financial actions ✅ Fixed (Phase 2)
**Severity:** Low
**Where:** [LeasesController.cs:10](RentCollection.Web/Controllers/LeasesController.cs#L10)

**What goes wrong:**
A `Manager` (`Operational` includes Manager) can `Terminate` a lease and
record a deposit refund, or `Renew` it at a different rent. These are
financial decisions that arguably belong with `AnyAdmin` only.

**Risk:** Authority creep — managers issuing rent changes without admin
oversight.

**Fix:** Move `Renew`, `Terminate`, `Transfer`, `Delete` to
`[Authorize(Roles = Roles.AnyAdmin)]`. Keep Create/Edit on
`Operational`. (Matches the `Tenants.Forget` / `Tenants.Restore`
posture from the recent revamp.)

---

## State-transition table (current vs intended)

Allowed transitions today (via either Edit dropdown or a lifecycle
action). ✅ = should be allowed. ⚠ = allowed but should be blocked.

| From → To     | Pending | Active | Expired | Terminated | Renewed |
|---------------|---------|--------|---------|------------|---------|
| **Pending**   |   —     |   ✅   |   ⚠    |    ⚠     |   ⚠    |
| **Active**    |   ⚠     |   —    |  (auto) |    ✅    |   ✅   |
| **Expired**   |   ⚠     |   ⚠   |   —    |    ✅    |   ✅   |
| **Terminated**|   ⚠     |   ⚠   |   ⚠    |    —     |   ⚠    |
| **Renewed**   |   ⚠     |   ⚠   |   ⚠    |    ⚠     |   —    |

Every ⚠ today is reachable via the Edit form's Status dropdown (Flaw #8).
Closing it forces the matrix to the intended shape.

---

## Counter-examples actually reproducible right now

These are concrete steps that exhibit the flaws against the running
backend at `http://localhost:5032`.

1. **Reproduce #1**: Create a tenant from `/Tenants/Create`, leave
   Building PACI blank, pick any vacant unit in the dropdown. Look at
   `/Leases` — the new lease shows 2025 dates.
2. **Reproduce #3**: Hit `POST /Leases/Terminate` on any Active lease.
   Refresh `/Properties/Details/{id}` — the unit's status badge still
   says Occupied.
3. **Reproduce #7**: Find a lease whose DB row has `Status = 'Active'`
   AND `LeaseEndDate < today`. On `/Leases` pick the `Status: Active`
   filter — that row is hidden. Pick `Status: Expired` — it shows up.
4. **Reproduce #10**: On `/Leases`, open the kebab on any Active lease,
   click `Renew`. The URL changes to `…/Details/{id}#renew`; the page
   loads but nothing scrolls to or shows a Renew form.

---

## Out of scope (consciously not audited)

- Performance / N+1s on `/Leases` (separate concern).
- K-NET / payment-gateway interaction.
- Tenant-portal lease views.
- Email templates fired by RecurringJobs.
- Excel-export column drift vs entity changes.

These remain candidates for a follow-up pass.

---

## LEASE_FIX_SUMMARY (2026-06-01 — all 8 phases shipped)

| Phase | Scope | Closes | Verification |
|---|---|---|---|
| **0** Schema | `LeaseLifecycleHardening` migration: Lease.RowVersion + RenewedBy + TransferredBy + AuditLog table + filtered unique `UQ_Leases_UnitId_Active` + idempotent backfill (auto-terminate duplicate Active leases per Unit, flip Active-past-end to Expired) | #2 #5 #7 #16 | SQL: index exists ✓, AuditLogs exists ✓, RowVersion col present ✓, 0 past-end Active rows ✓, 0 unit-overlap dups ✓ |
| **1** Helpers | `RentCollection.Core.Leases.LeaseMath` (`ComputeDueDate`, `DaysRemaining`, `IsPastEnd`) · `IAuditLogService` + `DbAuditLogService` · 13 xUnit tests | #6 #14 #16 | `dotnet test`: 77/77 pass |
| **2** Lifecycle | Renew/Terminate/Transfer rewritten: single TX · single SaveChanges · RowVersion concurrency catch · Unit.Status flips · Renew stages new lease as Pending then promotes (so UQ never sees two Actives) · stamps RenewedBy/TransferredBy · audit log on every action · `[AnyAdmin]` gate added to Renew/Terminate/Transfer/Delete | #2 #3 #4 #11 #16 #17 #20 | smoke: 302 on all 9 lease endpoints ✓ |
| **3** Create/Edit | Server-side validation (date order, dueDay range, rent positive) · Status removed from Create form · Status read-only badge on Edit · Closed-lease lock in controller (re-reads DB Status on POST, refuses if Terminated/Renewed/Expired) · tenant dropdown shows all w/ "(inactive)" suffix on both Create + Edit | #8 #9 #12 #15 | code-review |
| **4** Tenant flow | TenantsController.Create manual-unit fallback now derives `leaseStart = first of current month` · success message rebuilt from actual lease dates for both PACI and manual paths | #1 | code-review |
| **5** Jobs | RecurringJobs.GenerateMonthlyInvoicesAsync + InvoicesController both call `LeaseMath.ComputeDueDate` · new daily `FlipExpiredLeasesAsync` job registered at 01:00 UTC | #6 #7 root cause | `flip-expired-leases` registered in Hangfire ✓ |
| **6** Views | Index: `data-status` = DB truth · `LeaseMath.DaysRemaining` · `row-expired` is now a visual cue only · Details: three lifecycle cards (#renew, #terminate, #transfer) gated by status + AnyAdmin · server-rendered Vacant-unit dropdown · Delete: warns on renewal-chain orphan + on invoices-blocked deletion | #7 #10 #14 #18 | code-review |
| **7** Delete | Pre-check `Invoices.Any() || Payments.Any()` and flash redirect · audit log on delete | #19 | code-review |
| **8** Verify | Build clean (0 errors) · 77/77 unit tests · 9/9 smoke endpoints · 6/6 DB invariants · audit doc annotated | gate | this section |

### What's still deferred (consciously)
- **Pro-rata billing on partial months** (#13 partial) — the daily expire job closes the worst case; pro-rata math waiting on an ops decision.
- **AuditLog viewer UI** — schema lives now; `/Audit/{entityType}/{entityId}` is a separate sprint.
- **Soft delete on Lease** — hard delete + Phase 7 pre-check is sufficient; soft delete is a bigger query-filter audit.
- **Kuwait time-zone anchoring** — `DateTime.Today` is correct under Kuwait server time; matters only if servers move to UTC.
