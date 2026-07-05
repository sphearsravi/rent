# VIIJAR - Rent Collection System: Page-by-Page Guide

## Table of Contents
1. [Admin Portal](#admin-portal)
2. [Tenant Portal](#tenant-portal)
3. [Authentication Pages](#authentication-pages)
4. [System Pages](#system-pages)

---

# Admin Portal

## Dashboard (`/Dashboard`)
**Purpose:** Central command center for property managers. Gives a quick snapshot of the entire rental business at a glance.

**What's on the page:**
- 6 KPI stat cards: Total Properties, Occupied/Total Units with occupancy % bar, Active Tenants, Revenue This Month (KWD), Pending Payments (KWD), Open Maintenance Requests
- Recent Payments table showing tenant name, property/unit, amount, date, and status
- Upcoming Lease Expirations list color-coded by urgency (green >14 days, orange 7-14 days, red <=7 days)
- Quick Action buttons: Add Property, Add Tenant, Record Payment, New Maintenance, View Reports

---

## Properties (`/Properties`)

### Property List (`/Properties`)
**Purpose:** Browse and manage all rental properties in the system.

**What's on the page:**
- Search bar (search by name, PACI number, address, or area)
- Filter dropdowns: Property Type, Governorate, Status (Active/Inactive)
- Property cards in a grid, each showing: Property name, PACI badge, address, type, total units, occupancy %, property value (KWD), occupied vs vacant progress bar
- View Details and Edit buttons on each card
- Result count updates as you filter

### Create Property (`/Properties/Create`)
**Purpose:** Add a new rental property to the system.

**What's on the page:**
- Basic Information: Property Name, PACI Number (required), Property Type (Apartment/House/Commercial/Condo), Number of Units, Purchase Date, Property Value
- Address Information: Address, Governorate (6 Kuwait governorates with Arabic names), Area, Block, Building Number, Street
- Management: Company dropdown, Property Manager dropdown
- Property Documents: Upload multiple documents (Deed, Title, Building Permit, Occupancy Certificate, Insurance, Fire Safety, Electrical, Plumbing, Municipality License, Property Survey, Floor Plan). Accepts PDF, JPG, PNG, DOC, DOCX, DWG up to 10MB

### Property Details (`/Properties/Details/{id}`)
**Purpose:** View complete information about a single property including its tenants.

**What's on the page:**
- Property Information card: Name, PACI Number, Type, Units, Purchase Date, Value, Created Date
- Address Information card: Full address with Governorate, Area, Block
- Property Manager card: Manager name, email, phone (if assigned)
- Financial Summary card: Property value, occupied/total units, occupancy rate (color-coded badge), company
- Current Tenants table: Unit number, tenant name, email, phone, PACI number, lease start/end dates, monthly rent

### Edit Property (`/Properties/Edit/{id}`)
**Purpose:** Update property information including KNET payment credentials.

**What's on the page:**
- Same fields as Create page, pre-populated with current values
- Additional KNET Configuration section: KnetTranportalId, KnetTranportalPwd, KnetTerminalResourceKey (for building-specific KNET payment credentials)

### Delete Property (`/Properties/Delete/{id}`)
**Purpose:** Confirm deletion of a property with safety warnings.

**What's on the page:**
- Red warning banner: "This action cannot be undone"
- Full property summary (name, PACI, type, address, units, value, manager, company)
- Active Tenants alert (if occupied units exist, warns to terminate leases first)
- Units deletion warning (shows count of units that will be deleted)
- Confirm Delete, Cancel, and View Details buttons

---

## Tenants (`/Tenants`)

### Tenant List (`/Tenants`)
**Purpose:** View and manage all tenants across all properties.

**What's on the page:**
- Search bar (name, email, phone, PACI number)
- Property dropdown filter and Status filter
- Table with: Tenant avatar (initials), name, email, phone, Client PACI Number badge, Building PACI Number badge, property with area/governorate, status badge, tenant ID
- View and Edit action buttons per row
- Result count with dynamic filtering

### Create Tenant (`/Tenants/Create`)
**Purpose:** Register a new tenant in the system.

**What's on the page:**
- Personal Information: First Name, Last Name, PACI Number (required), Building PACI Number (required), Date of Birth, Government ID, Employment Status
- Contact Information: Email (required), Phone (required)
- Emergency Contact: Name and Phone
- Company Assignment: Company dropdown (required)
- Property & Unit Assignment: Property dropdown (loads from API), Unit dropdown (shows only vacant units, cascades from property selection), property details alert
- Documents: Upload multiple tenant documents (Civil ID, Passport, Visa, Insurance Certificate, Employment Letter, Other). Accepts PDF, JPG, PNG, DOC, DOCX

### Edit Tenant (`/Tenants/Edit/{id}`)
**Purpose:** Update tenant personal and contact information.

**What's on the page:**
- Personal Information: First Name, Last Name, PACI Number, Date of Birth, Government ID, Employment Status
- Contact Information: Email, Phone
- Emergency Contact: Name, Phone
- Settings: Company dropdown, Status (Active/Inactive)
- Note: No property/unit assignment or document upload (those are managed through Leases and Details pages)

### Tenant Details (`/Tenants/Details/{id}`)
**Purpose:** Comprehensive view of a tenant's entire rental history and documents.

**What's on the page:**
- Personal Information card: Full name, DOB, Civil ID, PACI Number, status badge
- Contact Information card: Email (clickable), Phone (clickable), Emergency contact
- Current Lease card: Property, Unit, Monthly Rent, Lease Period, Payment Due Day (only if active lease)
- Company card: Company Name, Email
- Lease History table: All leases with property, unit, dates, rent, status
- Payment History table: Last 10 payments with date, amount, method, status
- Lease Agreement cards: Color-coded (green=active, yellow=expiring within 30 days, red=expired) with days remaining, rent, deposit, due day, action buttons
- Tenant Documents grid: Uploaded docs with type icons (PDF=red, image=purple, Word=blue), download/view buttons
- Lease Documents grid: Lease-specific documents

### Delete Tenant (`/Tenants/Delete/{id}`)
**Purpose:** Confirm tenant deletion with safety checks.

**What's on the page:**
- Red warning banner
- Tenant summary: Name, PACI, Email, Phone, DOB, Company, Active/Total Leases, Status
- Active leases danger alert (recommends terminating leases first)
- Confirm Delete, Cancel, View Details buttons

---

## Leases (`/Leases`)

### Lease List (`/Leases`)
**Purpose:** View all leases organized by property with occupancy and financial summaries.

**What's on the page:**
- Search bar (tenant name, email, PACI, unit number)
- Building/Property dropdown filter and Status filter (Active/Terminated/Expired)
- Leases grouped by property, each property section showing: Property image, name, type, address, active tenant count, total units, monthly income
- Per-lease rows: Tenant avatar and name, unit, contact links, lease period with days remaining, monthly rent, status badge
- Expired leases highlighted in red background
- Expiring soon badges (orange, within 30 days)
- View Details and Edit buttons

### Create Lease (`/Leases/Create`)
**Purpose:** Create a new lease agreement between a tenant and a unit.

**What's on the page:**
- Tenant & Unit: Tenant dropdown, Unit dropdown
- Lease Duration: Start Date, End Date
- Financial Details: Monthly Rent ($ prefix), Security Deposit, Payment Due Day (1-31), Late Fee Amount, Grace Period (days)
- Lease Settings: Status (Active/Expired/Terminated), Lease Type (Fixed/Month-to-Month/Annual)

### Edit Lease (`/Leases/Edit/{id}`)
**Purpose:** Modify an existing lease agreement.

**What's on the page:**
- Same fields as Create, pre-populated with current values
- Hidden fields: LeaseId, CreatedDate (preserved for audit)

### Lease Details (`/Leases/Details/{id}`)
**Purpose:** View complete lease information with payment and invoice history.

**What's on the page:**
- Tenant Information card: Name, email (clickable), phone (clickable), status badge
- Property & Unit card: Property name, address, unit number, type, bedrooms, bathrooms
- Lease Period card: Start/end dates, duration in days, days remaining, lease type, status. Dynamic alerts for expired (red) or expiring within 30 days (orange)
- Financial Details card: Monthly rent (large green text), security deposit, payment due day, late fees, grace period, created/modified dates
- Payment History table: Date, amount, method, status, transaction reference. Shows total payments count and total amount
- Invoice History table: Invoice ID, issue/due dates, amount, status

### Delete Lease (`/Leases/Delete/{id}`)
**Purpose:** Confirm lease deletion with warnings about associated data.

**What's on the page:**
- Red warning banner
- Lease summary: Tenant, email, property, unit, lease period, rent (green highlight), deposit, status, payment count, invoice count
- Active lease danger alert
- Payment history warning (if payments exist)
- Confirm Delete, Cancel, View Details buttons

---

## Invoices (`/Invoices`)

### Invoice List (`/Invoices`)
**Purpose:** Manage all invoices with status tracking and batch generation.

**What's on the page:**
- Summary cards: Total Invoices, Pending Amount (KWD), Paid Amount (KWD), Overdue Count
- Search bar (invoice ID, tenant name, email, property, unit)
- Property dropdown filter
- Status tabs: All, Pending, Paid, Overdue
- Table: Invoice number (padded 5 digits), tenant name, property/unit, invoice date, due date, amount (KWD), paid amount, status badge
- Per-row actions: View Details, Mark as Paid, Mark as Overdue, Delete
- "Generate Monthly Invoices" button (POST with anti-forgery token)

### Invoice Details (`/Invoices/Details/{id}`)
**Purpose:** View a single invoice with print support.

**What's on the page:**
- Invoice header: Invoice number, date, due date, status badge
- Tenant Information card: Name, email, phone, PACI number
- Property & Unit card: Property name, unit number, address, governorate
- Invoice Items table: Description, type, amount
- Payment Summary: Subtotal, Tax, Total Amount, Paid Amount, Balance Due (highlighted)
- Notes section (if any)
- Print button (opens browser print dialog with print-optimized CSS)
- Mark as Paid button (if not already paid)

### Delete Invoice (`/Invoices/Delete/{id}`)
**Purpose:** Confirm invoice deletion.

**What's on the page:**
- Red warning banner
- Invoice summary: Invoice number, dates, status, type, amounts (blue=total, green=paid, red=balance)
- Tenant and Property information cards
- Notes section (if any)
- Cancel and Confirm Delete buttons

---

## Payments (`/Payments`)

### Payment List (`/Payments`)
**Purpose:** Admin view of all payments with mismatch detection for fraud prevention and reconciliation.

**What's on the page:**
- Summary cards: Total Received (KWD), This Month (KWD), Total Transactions, Mismatches (with warning badge)
- Filters: Civil ID search, Property dropdown, Status (Completed/Pending/Failed), Mismatch filter
- Table: Date, Tenant name/Civil ID, Property/Unit, Amount (KWD), Payment Method (icon: card=KNET, university=Bank Transfer, bills=Cash), Status badge, KNET Info
- Mismatch highlighting: Rows with mismatches have orange background. Shows 3 mismatch types: Civil ID Mismatch, Building Mismatch, KNET MID Mismatch
- Navigation links: Mismatches, Reconciliation, Record Payment

---

## Payment Approval (`/PaymentApproval`)

### Pending Approvals (`/PaymentApproval`)
**Purpose:** Review and approve/reject Cash and Bank Transfer payments that need manual verification.

**What's on the page:**
- Stat cards: Pending Approvals (count), Approved Today, Rejected Today
- Table: Payment date, tenant avatar/name/email, property/unit, amount (KWD), payment type badge (Cash/Bank Transfer), transaction reference (monospace), submitted date
- Inline Approve/Reject buttons with confirmation dialogs
- View History link

### Approval Details (`/PaymentApproval/Details/{id}`)
**Purpose:** Detailed review of a single payment before approving or rejecting.

**What's on the page:**
- Payment Information card: ID, Date, Amount, Type, Reference, Status, Receipt
- Tenant Information card: Name, email, phone, PACI
- Property Information card: Property name, unit, monthly rent
- Bank Transfer Details (conditional): Bank name, transfer date, reference, proof image/file
- Cash Payment Details (conditional): Collected by, receipt number
- Invoice Information (conditional): Invoice ID, due date, amounts
- Approve modal: Confirmation with payment summary
- Reject modal: Requires rejection reason (textarea, required field)

### Approval History (`/PaymentApproval/History`)
**Purpose:** View historical record of all approved and rejected payments.

**What's on the page:**
- Filter card: Status dropdown (All/Approved/Rejected), From Date, To Date
- Table: Payment date, tenant name, property/unit, amount (KWD), payment type (icon), status (green checkmark or red X), approver name, approval/rejection date
- Record count footer
- Clear Filters button (preserves filter state in URL parameters)

---

## Maintenance (`/Maintenance`)

### Maintenance List (`/Maintenance`)
**Purpose:** Track all maintenance requests across all properties.

**What's on the page:**
- Table: Request ID, Property/Unit, Tenant, Request Type (with type-specific icons), Priority badge, Status badge, Request Date, Assigned To (with staff avatar)
- View, Edit, Delete action buttons per row

### Create Maintenance Request (`/Maintenance/Create`)
**Purpose:** Log a new maintenance request.

**What's on the page:**
- Property Information: Property dropdown, Unit dropdown, Tenant dropdown (optional)
- Request Details: Category, Priority (default: Medium), Estimated Cost (KWD), Description textarea, Assigned To dropdown

### Edit Maintenance Request (`/Maintenance/Edit/{id}`)
**Purpose:** Update maintenance request status and track completion.

**What's on the page:**
- Property Information: Property, Unit, Tenant
- Request Details: Category, Priority, Estimated Cost, Description, Status (Open/In Progress/Completed/Cancelled), Assigned To
- Completion Details: Completed Date, Actual Cost (KWD), Notes textarea

### Maintenance Details (`/Maintenance/Details/{id}`)
**Purpose:** View full maintenance request information with completion tracking.

**What's on the page:**
- Status and Priority badges at top with request date
- Property Information: Name, address, governorate
- Request Details: Category, tenant contact info (email, phone), description, estimated cost (green)
- Completion Details (shown only if completed): Completed date, actual cost, duration (calculated in days), notes
- Audit Information: Created date, modified date

### Delete Maintenance Request (`/Maintenance/Delete/{id}`)
**Purpose:** Confirm deletion of a maintenance request.

**What's on the page:**
- Amber warning banner
- Request summary: ID, Status, Property, Unit, Tenant, Type, Priority, Date, Assigned To, Cost
- Description (if present)
- Confirm Delete and Cancel buttons

---

## Reports (`/Reports`)

### Reports Hub (`/Reports`)
**Purpose:** Central navigation page to access all reports.

**What's on the page:**
- Date range filter (From Date, To Date)
- Quick filter buttons: This Month, Last Month, This Quarter, This Year
- 5 report cards with View and Export buttons:
  1. Executive Dashboard
  2. Property Performance
  3. Tenant Payment Analysis
  4. Invoice Status & Aging
  5. Payment Report (Grid View)

### Executive Dashboard (`/Reports/ExecutiveDashboard`)
**Purpose:** High-level portfolio overview for management decision-making.

**What's on the page:**
- 4 KPI cards: Total Properties & Units, Occupancy Rate %, Collection Rate %, Outstanding Amount (KWD)
- Financial Summary table: Potential Monthly Rent, Actual Monthly Rent, YTD Revenue (blue), YTD Maintenance Costs (red), Net Revenue YTD (green)
- Tenant Summary card: Active Tenants, Active Leases, Average Rent per Tenant
- Maintenance Summary card: Total Requests, Total Cost, Average Cost per Request
- Export to Excel button

### Property Performance (`/Reports/PropertyPerformance`)
**Purpose:** Compare performance metrics across all properties side by side.

**What's on the page:**
- Date filter
- Sortable table (click headers to sort): Property Name, PACI Number, Location, Total/Occupied/Vacant Units, Occupancy % (badge), Potential & Actual Rent (KWD), Collection % (badge), Outstanding (KWD), Maintenance Cost & Count, Net Revenue (green if positive, red if negative), Revenue per SqFt
- Footer row with totals across all properties
- Record count badge

### Tenant Payment Analysis (`/Reports/TenantPaymentAnalysis`)
**Purpose:** Analyze tenant payment behavior and identify problematic payers.

**What's on the page:**
- Date filter
- Payment Score Legend: Excellent (green), Good (blue), Fair (orange), Poor (red)
- Sortable table: Tenant Name, PACI Number, Email, Phone, Active Leases, Total Billed/Paid/Outstanding (KWD), Payment Rate %, Payment Score (color badge), Average Days to Pay, Total Payments, Last Payment Date & Amount
- Footer totals
- Summary Stats: Count of Excellent/Good/Fair/Poor payers

### Invoice Status & Aging (`/Reports/InvoiceStatus`)
**Purpose:** Track invoice aging to identify overdue accounts and collection priorities.

**What's on the page:**
- Date filter
- Summary Stats: Total Invoices, Paid (with amount), Partial (with outstanding), Unpaid (with amount)
- Aging Summary cards (4 buckets): 0-30 days Current (green), 31-60 days (blue), 61-90 days (yellow), 90+ days (red) - each shows count and amount
- Sortable table: Invoice Number, Tenant, Property, Unit, Invoice/Due Dates, Total/Paid/Outstanding (KWD), Status badge, Days Overdue (color-coded by severity), Aging Bucket
- Footer totals

### Payment Report (`/Reports/PaymentReport`)
**Purpose:** Detailed transaction-level payment report with advanced filtering.

**What's on the page:**
- 6 filter fields: From Date, To Date, Property dropdown, Payment Type (KNET/Cash/BankTransfer), Status (Completed/Pending/Failed), Tenant dropdown
- Apply Filters, Reset, Export Excel buttons
- Stats cards: Total Payments (count & KWD), Completed (count & KWD), Pending (count & KWD)
- Payment Type Breakdown cards: KNET (count & KWD), Cash (count & KWD), Bank Transfer (count & KWD)
- Transaction table: ID, Date, Tenant, Property, Unit, Amount (KWD), Method badge, Status badge, Reference

---

## Documents (`/Documents`)

### Document List (`/Documents`)
**Purpose:** Browse and manage all uploaded documents in the system.

**What's on the page:**
- Document cards in a responsive grid: File icon (color-coded by type: PDF=red, Word=blue, Excel=green, PowerPoint=orange, Image=purple, Archive=amber), Document Name, Type, Size, Description
- Related entity link (Type & ID for cross-referencing)
- Upload metadata: Uploaded By, Upload Date
- Download and Delete buttons (delete has confirmation)

### Upload Document (`/Documents/Upload`)
**Purpose:** Upload a new document with optional entity linking.

**What's on the page:**
- File upload area with drag-and-drop support (max 50MB)
- Document Type dropdown: Lease Agreement, Invoice, Receipt, ID Document, Contract, Report, Other
- Description textarea
- Entity Linking section (optional): Entity Type selector (Tenant, Property, Lease, Maintenance Request), Dynamic entity dropdown based on selected type
- File preview after selection (icon + size)

---

## Users (`/Users`)

### User List (`/Users`)
**Purpose:** Manage admin portal user accounts.

**What's on the page:**
- Search by name or email
- Role dropdown filter, Status filter (Active/Inactive)
- Table: User avatar (initials), Name, Email (with verification checkmark), Phone, Role badge, Status badge (Active/Inactive), Last Login timestamp
- View, Edit, Delete action buttons

### Create User (`/Users/Create`)
**Purpose:** Create a new admin portal user account.

**What's on the page:**
- Left column (form): Username, Email, First Name, Last Name, Phone, Password (required), Role (User/Accountant/Manager/Admin), IsActive checkbox, EmailVerified checkbox
- Right column (info panel): Required fields checklist, Password security details (PBKDF2 hashing, SHA256, 10,000 iterations, 16-byte salt), Default settings

### Edit User (`/Users/Edit/{id}`)
**Purpose:** Update user account details.

**What's on the page:**
- Same layout as Create
- Password field is optional ("leave blank to keep current password")
- Info panel shows current account status badges (Active/Inactive, Verified/Not Verified)

### User Details (`/Users/Details/{id}`)
**Purpose:** View user profile and activity information.

**What's on the page:**
- Profile card: Large avatar with initials, full name, email, role/status/verified badges
- Personal Information grid: Username, Email, First/Last Name, Phone, Role
- Activity Information grid: Created Date, Last Modified, Last Login
- Account Status sidebar: Account status badge, email verification badge
- Quick Actions sidebar: Edit User, Delete User buttons

### Delete User (`/Users/Delete/{id}`)
**Purpose:** Confirm user account deletion.

**What's on the page:**
- Red warning header
- Alert emphasizing irreversibility
- User profile preview: Avatar, name, email, role badge
- User Information grid: Username, Email, Phone, Status, Created Date, Last Login
- Cancel and Confirm Delete buttons

---

# Tenant Portal

The Tenant Portal is a separate standalone interface (no shared admin layout) for tenants to manage their rentals and payments.

## Tenant Login (`/TenantPortal/Login`)
**Purpose:** Authenticate tenants using their Civil ID and password.

**What's on the page:**
- Civil ID input field
- Password field
- Remember Me checkbox
- Demo Login button (pre-fills test credentials)
- Purple gradient background with animated slide-up card

## Tenant Dashboard (`/TenantPortal/Dashboard`)
**Purpose:** Tenant's home page showing their leases, payments, and maintenance requests.

**What's on the page:**
- Welcome greeting with tenant name
- Stats grid: Active lease count, payment count, open maintenance requests
- Property cards: Each lease showing property name, unit, monthly rent, lease period, payment due day, late fees, grace period
- Recent Payments table: Date, amount, method, status
- Maintenance Requests list with status
- New Maintenance Request modal
- Documents section with type-specific icons

## Make Payment (`/TenantPortal/MakePayment`)
**Purpose:** Main payment page where tenants select an invoice and choose a payment method.

**What's on the page:**
- Invoice selection grid: Shows all pending invoices with amount, due date, property
- Payment Method modal with 3 options:
  - **KNET:** Calls `/api/payments/knet/initiate`, then redirects tenant to KNET gateway (or TestPayment page in test mode)
  - **Bank Transfer:** Form with bank dropdown (9 Kuwaiti banks), transfer date, reference number, proof file upload (5MB max with image preview), notes
  - **Cash:** Records cash payment
- Loading/processing indicators

## Make Payment Simple (`/TenantPortal/MakePaymentSimple`)
**Purpose:** Simplified payment interface with fewer options.

**What's on the page:**
- Invoice cards with status badges
- Bank transfer modal only (no KNET or cash)
- Simpler UI with inline styles

## Make Payment New (`/TenantPortal/MakePayment_New`)
**Purpose:** Enhanced version of the payment page with improved UI.

**What's on the page:**
- Same functionality as MakePayment
- Enhanced gradient navbar
- Full KNET integration
- Bank dropdown with 9 Kuwaiti banks
- Bootstrap 5 modals

## Payment History (`/TenantPortal/PaymentHistory`)
**Purpose:** View all past payments made by the tenant.

**What's on the page:**
- Stats row: Total payments count, total amount (KWD), completed count, pending count
- Searchable/filterable payment table: Date, Property, Amount (KWD), Payment Method (icon: card/bank/cash/check), Reference, Status badge
- Empty state card when no payments

## My Details (`/TenantPortal/MyDetails`)
**Purpose:** View personal information on file (read-only).

**What's on the page:**
- Personal Information: Full name, PACI Number, Date of Birth, Government ID, Employment Status
- Contact Information: Email, Phone
- Emergency Contact: Name, Phone
- Company Information: Company name

## Payment Success (`/TenantPortal/PaymentSuccess`)
**Purpose:** Confirmation page after a successful payment.

**What's on the page:**
- Green gradient background
- Animated success checkmark icon (scale-in animation)
- Reference number display
- Payment details: Amount, Date, Method
- Navigation buttons: View Payment History, Back to Dashboard

## Payment Failed (`/TenantPortal/PaymentFailed`)
**Purpose:** Error page shown when a payment fails.

**What's on the page:**
- Red gradient background
- Animated error icon (shake animation)
- Error message details
- 4 troubleshooting tips: Check card details, ensure sufficient funds, contact bank, try different payment method
- Retry and Dashboard buttons

## Test Payment (`/TenantPortal/TestPayment`)
**Purpose:** KNET test mode simulator for development/testing (used when UseTestMode=true).

**What's on the page:**
- Test mode banner
- Simulated card entry form: Pre-filled card number (4111 1111 1111 1111), expiry, CVV
- Invoice and amount display
- Transaction ID
- Two buttons: Simulate Success, Simulate Failure
- Calls `/api/payments/knet/test/complete` then redirects to PaymentSuccess or PaymentFailed
- Processing modal with spinner animation

## Test Bank Transfer (`/TenantPortal/TestBankTransfer`)
**Purpose:** Test form for bank transfer upload during development.

**What's on the page:**
- Invoice ID, Tenant ID, Property ID, Amount fields
- Transfer date, bank dropdown, reference number
- Proof file upload
- Notes textarea
- Submits to `/api/payments/bank/upload`
- Console logging for debugging

## View Receipt (`/TenantPortal/ViewReceipt`)
**Purpose:** Printable payment receipt for tenant records.

**What's on the page:**
- Receipt header with company branding
- Receipt details: Receipt number, date, received from (tenant name), amount (KWD), property, payment method, reference, status
- Company info footer
- Print button with CSS print media queries (hides navigation, optimizes layout for paper)

---

# Authentication Pages

## Admin Login (`/Account/Login`)
**Purpose:** Sign in to the admin portal.

**What's on the page:**
- Email/Username field, Password field, Remember Me checkbox
- Sign In button
- Demo Login button (fills admin@test.com / Admin@123)
- Forgot Password link
- Purple gradient background, standalone page (no sidebar)

## Register (`/Account/Register`)
**Purpose:** Create a new admin portal account.

**What's on the page:**
- First Name, Last Name, Email, Phone Number, Company dropdown (optional), Password, Confirm Password
- Password requirements: 8+ characters, uppercase, lowercase, number, special character
- Link back to Login
- Green gradient background, standalone page

## Forgot Password (`/Account/ForgotPassword`)
**Purpose:** Request a password reset code via email.

**What's on the page:**
- Email address input
- Send Reset Code button
- Back to Login link
- Pink gradient background, standalone page

## Forgot Password Confirmation (`/Account/ForgotPasswordConfirmation`)
**Purpose:** Confirmation that reset email was sent.

**What's on the page:**
- "Check Your Email" success message
- Info note: Code expires in 24 hours, check spam folder
- Back to Login button

## Reset Password (`/Account/ResetPassword`)
**Purpose:** Set a new password using the reset code.

**What's on the page:**
- Email field, New Password, Confirm Password, hidden Code field
- Password requirements displayed
- Back to Login link
- Blue gradient background, standalone page

## Reset Password Confirmation (`/Account/ResetPasswordConfirmation`)
**Purpose:** Confirmation that password was successfully reset.

**What's on the page:**
- "Password Reset Successful" message
- Go to Login button

## Access Denied (`/Account/AccessDenied`)
**Purpose:** Shown when a user tries to access a page they don't have permission for.

**What's on the page:**
- Ban icon
- "Access Denied" heading
- "You don't have permission" message
- Go to Home and Go Back buttons
- Uses admin layout (shown inside the portal with sidebar)

---

# System Pages

## Home (`/`)
**Purpose:** Default landing page (ASP.NET boilerplate).

**What's on the page:**
- Welcome heading
- Link to ASP.NET Core documentation

## Privacy (`/Home/Privacy`)
**Purpose:** Privacy policy placeholder.

**What's on the page:**
- Privacy Policy heading
- Placeholder text (ready for custom content)

## Database Test (`/Test/DbTest`)
**Purpose:** Development tool to verify database connectivity.

**What's on the page:**
- Connection Status: Success (green), Error (red), Failed (yellow)
- Status message
- Connection details table (key-value pairs)
- Stack trace display (scrollable) if error occurs
- Back to Home button

## Error Page (`/Home/Error`)
**Purpose:** Generic error page shown when unhandled exceptions occur.

**What's on the page:**
- "An error occurred" heading
- Request ID (for debugging)
- Development mode instructions
