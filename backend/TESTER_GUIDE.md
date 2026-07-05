# RentCollection — Tester Walkthrough Guide

**A friendly, click-by-click guide to testing the ALRTAM property
management app.** No development experience needed — if you can use a
web browser, you can test this.

If you'd rather see the formal checklist with pass/fail boxes, see
[TEST_CASES.md](TEST_CASES.md). This guide is the narrative version —
written like someone is walking you through the app for the first time.

---

## Before you start

### What you need

1. **A web browser** — Chrome, Edge, or Firefox (latest version)
2. **The app URL:**
   - Demo: http://demo.example.com
   - Local: http://localhost:5032
3. **A test login.** Use these for the demo:
   - **Admin:** `admin@test.com` / `Admin@123`
   - **Tenant:** `tenant@test.com` / `Tenant@123`
4. **A notepad** (paper or digital) to write down anything weird you see
5. **About 90 minutes** for the full walkthrough. You can stop and
   resume at any section.

### Ground rules

- **If something looks wrong, write it down.** Don't try to "fix" the
  app — just note what you did, what you expected, and what happened.
- **A blank screen, a 500 error, or a never-loading button is always a
  bug.** Tell us even if you think it might be your internet.
- **Take screenshots** of anything strange (Windows: `Win+Shift+S`).
  Save them in a folder named with today's date.
- **You can break things on the demo site.** That's what it's for.
  Create test tenants with names like "TEST DELETE ME" so the team can
  clean up afterwards.

---

## Part 1 — The first five minutes (is the app alive?)

### 1.1 Open the login page

Type the URL into your browser. You should land on a dark page with a
gold ALRATM logo, a white card in the middle, and two fields: "Username
or Email" and "Password".

**What good looks like:**
- The card slides up gently when the page loads (animated)
- The ALRTAM logo is sharp, not blurry
- There's a "Sign in" button, a "Forgot password?" link, and a "Tenant
  portal login" button at the bottom

**Write it down if:**
- The page shows raw HTML or an error
- The logo is missing (you see a generic icon instead)
- The page takes more than 5 seconds to load

### 1.2 Sign in as Admin

Click the **"Fill demo credentials"** button (the dotted-border button
at the bottom of the form). The email and password fields should fill
in automatically with `admin@test.com` / `Admin@123`.

Click **"Sign in"**.

**What good looks like:**
- The page changes to the Dashboard within 2 seconds
- A left sidebar appears with VIIJAR logo + menu items
- The top-right shows your name ("admin") + an ALRATM logo

**Write it down if:**
- You're bounced back to the login page (the cookie didn't stick)
- You see "Invalid credentials" (the demo password was changed)
- You see a 500 error page (the server is broken)

### 1.3 Look around the dashboard

The Dashboard should show several "KPI tiles" at the top with numbers:
- Occupancy %
- Total Properties
- Active Leases
- Open Maintenance

**What good looks like:**
- Each tile shows a number (not "—" or "N/A")
- Numbers feel sensible (e.g., Occupancy is 0-100%)
- No red error boxes anywhere

### 1.4 Click around the sidebar

Click each item in the left sidebar one at a time:
- Dashboard
- Properties
- Tenants
- Units
- Leases
- Payments
- Invoices
- Reports
- Maintenance
- Documents
- Users (admin only)
- Audit Log (admin only)

**For each one:** does the page load? Is there a heading? Is the "Add
[item]" button at the top-right visible?

**Write it down if:** any page returns an error, looks blank, or shows
broken layout (wrapped text, overlapping buttons, etc.)

### 1.5 Sign out

Click your name at the top-right, then click "Logout". You should land
back on the login page. **Try to navigate to `/Dashboard` directly by
typing it in the URL bar.** You should be bounced back to login —
that's the security working correctly.

---

## Part 2 — Add a property

This tests the redesigned property form.

### 2.1 Open the Create form

Sign in as admin again. Click **Properties** in the sidebar, then click
the **"Add Property"** button (top-right of the page, usually with a
"+" icon).

**What good looks like:**
- A page opens titled "Create New Property"
- You see a clearly labeled **"Essentials"** section at the top with
  fields: Property Name, Building PACI, Property Type, Governorate,
  Number of Units, Company
- Below Essentials, there's a **"More details"** toggle (collapsed by
  default) for optional things like address and valuation
- At the bottom, a **"Documents"** section where you can attach files
- Action bar at the bottom with **Cancel**, **Save & add another**,
  and **Create Property** buttons

### 2.2 Test "what happens when I don't fill in required fields"

Without typing anything, click **"Create Property"**.

**Expected:** You stay on the form. Red text appears under the empty
required fields, OR a red banner at the top says fields are missing.
**No** 500 error.

### 2.3 Fill in only the Essentials

Type:
- **Property Name:** `Test Tower DELETE ME`
- **Building PACI:** `99999001` (use a unique number)
- **Property Type:** Apartment
- **Governorate:** Al Asimah
- **Number of Units:** `5`
- **Company:** pick any option

**Don't open the "More details" toggle.** Click **"Create Property"**.

**Expected:** You're redirected to the Properties list, and your new
property appears in the table.

### 2.4 Test "Save & add another"

Click **Add Property** again. Fill in the Essentials with a different
name (`Test Tower 2 DELETE ME`, PACI `99999002`). This time click
**"Save & add another"** instead of "Create Property".

**Expected:** The form clears, and a green banner appears at the top
saying something like *"Property Test Tower 2 DELETE ME saved. Add
another below."* You're still on the Create form, ready for the next
one. (This is for batch entry.)

### 2.5 Test the "More details" toggle

Click the **"More details"** toggle. It should expand to show:
- An **Address** section (Address, Area, Block, Street, Building Number)
- A **Valuation** section (Purchase Date, Property Value)
- A **Management** section (Property Manager dropdown)

**Expected:** The arrow rotates. Sections appear smoothly. Click the
toggle again — they collapse.

### 2.6 Test the draft autosave

While the "More details" is still expanded, type something in
the Address field. **Don't save.** Close the browser tab.

Now open Properties → Add Property again.

**Expected:** A small green chip appears near the top saying *"Draft
restored"*. The Address field is pre-filled with what you typed before.
(This protects you against accidental tab closes.)

---

## Part 3 — Add a tenant (with Civil ID magic)

This tests the Civil ID auto-fill — the most distinctive feature.

### 3.1 Open the Tenant create form

**Tenants** in sidebar → **"Add Tenant"** button.

**What good looks like:** Same three-zone layout as the Property form
(Essentials, More details, Documents).

### 3.2 Test the Civil ID parser (no scan needed)

In the **Essentials** zone, find the **Client Civil ID (PACI)** field.
Type a valid 12-digit Kuwait Civil ID — for example:
- `283021501234` (a test one — replace with a real format if needed)

**As soon as you type the 12th digit**, watch the small status text
under the field.

**Expected one of three:**
- **Green: "✓ Civil ID validated — DOB pre-filled"** — perfect Civil ID.
  Also: the Date of Birth field in "More details" should now be filled.
- **Amber: "⚠ Checksum mismatch — double-check the number"** — the
  format is right but the checksum is wrong. DOB is still pre-filled.
- **Red: "✗ Invalid format"** — something's off.

**Try a deliberately broken one:** `12345` (only 5 digits) — wait a
beat. Should show red "Civil ID must be 12 digits".

### 3.3 Test the Civil ID scan (Gemini AI)

This only works if Civil ID enrichment is enabled. The demo site has it
disabled by default for cost reasons. **Skip this if you see "Civil ID
enrichment is disabled" on clicking Scan card.**

If enabled:

1. Click the **"Scan card"** button (next to the Civil ID field)
2. **You'll get a "Please tick the consent box first" warning** —
   that's by design (privacy law in Kuwait requires explicit consent
   before sending PII to a third-party AI service)
3. Tick the **"Consent to send image to Google Gemini"** checkbox
4. Click **"Scan card"** again → file picker opens
5. Upload a clear photo of a Kuwait Civil ID (front side)

**Expected:**
- Status text says "Reading card..." (2-5 seconds)
- Then green "✓ Card read. Review each amber-bordered field before saving."
- **First Name**, **Last Name**, **Civil ID**, and **Date of Birth**
  fields all auto-fill with an **amber border** around them. This
  amber border means "AI filled this — please double-check before
  saving." It goes away as soon as you click into the field.

**Write it down if:**
- The scan never finishes (spins forever)
- The fields fill with nonsense
- Fields don't get the amber border

### 3.4 Fill the rest and save

Whether you used the scanner or typed manually, complete:
- **First Name, Last Name** (the scan may have filled these)
- **Email:** something unique like `test.tenant.99@delete.me`
- **Phone:** `+96599999001`
- **Civil ID** (must be 12 digits)
- **Building PACI:** the PACI of one of the properties you created

Open **"More details"** if you want to add DOB, employment status, etc.

Click **"Create Tenant"**.

**Expected:** Redirected to Tenants list, new tenant visible.

### 3.5 Make sure the Company picker is NOT visible

This is a security check. The form should **not** ask you to pick a
Company. The system auto-assigns based on your login. If you see a
Company dropdown anywhere on the create form, write it down — that's a
regression from Sprint 22.

---

## Part 4 — Create a lease

This connects a tenant to a unit.

### 4.1 Make sure you have at least one vacant unit

**Units** in sidebar. If the list is empty or all units show "Occupied":
- Click **Add Unit**
- Pick the property you created in Part 2
- Unit Number: `1A`
- Bedrooms: `2`
- Click **Create**

You don't need to enter Monthly Rent at the unit level — it's set on
the lease.

### 4.2 Open the Lease create form

**Leases** in sidebar → **"Add Lease"** button. Three zones again.

### 4.3 Test the Building → Unit cascade

In the **Essentials** zone:
1. Click the **Building** dropdown — pick the property you created
2. Click the **Unit** dropdown

**Expected:** The Unit dropdown now only shows vacant units belonging
to that building. If you switch the Building, the Unit list rebuilds.
The dropdowns are searchable — type a few letters to filter.

### 4.4 Pick a tenant and fill dates

- **Tenant:** the one you just created
- **Start Date:** today
- **End Date:** one year from today
- **Monthly Rent:** `300.000` (KWD has 3 decimals — the prefix should
  show "KWD")
- **Security Deposit:** `0` for now (so the lease activates immediately)

### 4.5 Save and watch the unit flip

Click **Create Lease**.

**Expected:**
- Redirected to Leases list
- The new lease shows status **"Active"**
- Go back to **Units**, find the unit you used → status should be
  **"Occupied"** (not Vacant). This is the Sprint 13 fix — if it shows
  Vacant, that's a regression.

### 4.6 Test deposit → Pending flow

Create another lease (use another vacant unit and the same tenant).
This time set **Security Deposit = 500.000**.

**Expected:**
- Lease saves as **"Pending"** (not Active)
- A note explains: *"Lease created as Pending — issue the deposit
  invoice via Activate when ready."*
- The unit is **not** yet Occupied — it's marked Reserved or stays
  Vacant until you Activate

To complete the test: open the lease → click **Activate**. The lease
should flip to Active, the unit to Occupied, and a deposit invoice
should appear in `/Invoices`.

---

## Part 5 — Issue and pay an invoice

### 5.1 Find an invoice for your tenant

**Invoices** in sidebar. Look for an invoice belonging to your test
tenant. If none yet, you can manually trigger the daily-rent-invoicing
job:

1. Visit `/hangfire`
2. **Recurring Jobs** tab
3. Find `daily-rent-invoicing` → click **Trigger now**
4. Wait 10 seconds, refresh `/Invoices`

### 5.2 Pay with Cash

Open the invoice → **"Record Cash Payment"** button.

Fill in:
- Amount: full balance
- Receipt Number: `CASH-TEST-001`
- Date: today

Click **Record**.

**Expected:**
- Payment Confirmed immediately (no approval needed)
- Invoice status → **"Paid"**
- Balance shows 0

### 5.3 Test bank transfer + approval

Log out, log in as the **tenant** (`/TenantPortal/Login` with
`tenant@test.com` / `Tenant@123`).

Open an unpaid invoice → **"Pay with Bank Transfer"** → upload any
image (a screenshot of anything will do for the test) → Submit.

**Expected:** Payment recorded as **"Pending Approval"**. Invoice
unchanged.

Log out, log in as admin → **Payment Approval** in sidebar (or
`/PaymentApproval`).

**Expected:** Your pending bank transfer appears. Click → review the
screenshot → click **Approve**.

**Expected:** Payment → Confirmed. Invoice → Paid.

### 5.4 Test K-NET (sandbox)

K-NET is Kuwait's local card-payment network. Sandbox mode is enabled
in the demo. Log in as tenant → unpaid invoice → **"Pay with K-NET"**.

**Expected:**
- Redirected to the K-NET sandbox page
- Use any test card (the page shows the test card numbers)
- On success: bounced back to the app, payment Confirmed
- On cancel: bounced back, no payment recorded

---

## Part 6 — Maintenance request

### 6.1 Tenant raises a request

Still logged in as the tenant: **Maintenance** in sidebar → **"New
Request"**.

Fill in:
- Title: `TEST — Kitchen tap leaking`
- Description: a sentence or two
- Priority: Normal
- Optionally: upload a photo

Click **Submit**.

**Expected:** Request created. Visible in tenant's maintenance list
with status **"Open"**.

### 6.2 Manager assigns and completes

Log out, log in as admin → **Maintenance** in sidebar.

Find your test request → **Assign**. Pick:
- A Vendor
- Priority
- Target completion date

Save.

**Expected:** Status → **"Assigned"**. The vendor's name appears.

Now click **"Mark Complete"**:
- Upload an "after" photo
- Enter a cost (e.g., `15.000` KWD)

Save.

**Expected:** Status → **"Completed"**. The cost is recorded.

### 6.3 Check tenant sees the resolution

Log back in as the tenant → Maintenance → open the request.

**Expected:** Tenant sees both the original "before" upload and the
"after" photo, plus the completion date.

---

## Part 7 — Run a report

### 7.1 Open Reports

Sidebar → **Reports**. You should see a list of available reports.

### 7.2 Rent Collection by Month

Click **"Rent Collection by Month"**. Pick a date range that includes
today.

Click **Generate** (or **Export to Excel**).

**Expected:** An Excel file downloads. Open it.

**What good looks like:**
- The header shows ALRTAM branding (gold + black)
- KWD amounts have **3 decimal places** (e.g., 300.000, not 300.00)
- Totals at the bottom add up correctly
- No #ERROR or #VALUE cells

### 7.3 Outstanding Invoices

Try the **Outstanding Invoices** report. Export to PDF this time.

**Expected:** PDF downloads. Should have aged buckets (0-30 days, 31-60,
61-90, 90+) and a summary at the top.

---

## Part 8 — Log out and clean up

When you're done:

1. Make a list of all the test entities you created (anything named
   "TEST DELETE ME" or with PACI starting `99999...`)
2. Tell the team so they can purge them from the database
3. Log out

---

## What to write down when you find a bug

For every bug, capture:

```
WHAT I WAS DOING:    e.g. "Trying to save a tenant with civil ID 12345"
WHAT I EXPECTED:     e.g. "A red error saying the ID is too short"
WHAT ACTUALLY HAPPENED: e.g. "A 500 error page appeared"
URL I WAS ON:        copy from browser address bar
TIME (your watch):   e.g. 14:32 on 2026-06-09
SCREENSHOT:          file name (save with date in name)
BROWSER:             Chrome / Edge / Firefox / Safari
```

Send these to the dev team. The more detail, the faster the fix.

---

## Cheat sheet — fastest path to find regressions

If you only have **15 minutes**, run this:

1. Login as admin (Part 1.2)
2. Click every sidebar item (Part 1.4)
3. Create one property (Part 2.3)
4. Create one tenant (Part 3.4) — test Civil ID parser (Part 3.2)
5. Create one lease (Part 4.4)
6. Record a cash payment (Part 5.2)
7. Logout, login as tenant, view dashboard

If anything in those seven steps fails, the build is not safe to ship.

---

## Glossary

| Term            | What it means in plain English                                              |
| --------------- | --------------------------------------------------------------------------- |
| **PACI**        | Kuwait's national ID system. Both citizens and buildings have a 12-digit PACI number. |
| **Civil ID**    | Same as the citizen's PACI number — the front of their ID card.             |
| **K-NET**       | The standard online payment network in Kuwait. Like Visa/Mastercard but local. |
| **KWD**         | Kuwaiti Dinar. Three decimal places: `300.000` (not `300.00`).              |
| **Governorate** | A Kuwait state/province. There are six.                                     |
| **Hangfire**    | The background-job system. Runs nightly tasks like invoicing.               |
| **Lease**       | One legal renting agreement between a tenant and a unit.                    |
| **Active / Pending / Terminated** | Lease lifecycle states. Pending = waiting for deposit, Active = currently renting, Terminated = ended. |

---

*Guide version: 2026-06-09 · Sprint 23*
*If anything in this guide doesn't match what you see in the app, the guide is wrong — please report it.*
