# Invoice Generation & Advanced Reports Guide

## Setup: Generate Initial Invoices

Before using the invoice system, generate invoices for all active leases:

### Run SQL Script
```bash
sqlcmd -S RAVIWST\SQLEXPRESS -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\GenerateInitialInvoices.sql"
```

**Or using SSMS:**
1. Open SQL Server Management Studio
2. Connect to `RAVIWST\SQLEXPRESS`
3. Open file: `GenerateInitialInvoices.sql`
4. Execute (F5)

This script will:
- Generate invoices for current month for all active leases
- Generate invoices for previous month (some marked as Paid, some as Overdue)
- Display summary statistics

---

## Invoice Management

### Access Invoices
Navigate to: **Financial → Invoices**

### Features

#### 1. Invoice Dashboard
**Summary Cards:**
- **Total Invoices** - Count of all invoices
- **Pending Amount** - Total unpaid amount in KWD
- **Paid Amount** - Total paid amount in KWD
- **Overdue Count** - Number of overdue invoices

**Filter Tabs:**
- All - Show all invoices
- Pending - Yellow badge, awaiting payment
- Paid - Green badge, payment completed
- Overdue - Red badge, past due date

#### 2. Generate Monthly Invoices
**Button:** "Generate Monthly Invoices" (top of page)

**How it works:**
1. Click the button
2. System automatically:
   - Finds all active leases
   - Checks for existing invoices for current month
   - Creates new invoices for leases without current month invoice
   - Sets due date based on lease PaymentDueDay
   - Calculates total from MonthlyRent
3. Shows success message with count of invoices generated

**Invoice Details:**
- Invoice Type: "Rent"
- Total Amount: Monthly rent from lease (KWD with 3 decimals)
- Paid Amount: 0.000 initially
- Status: "Pending"
- Due Date: Based on PaymentDueDay from lease
- Notes: "Monthly rent for [Month Year] - Unit [UnitNumber]"

#### 3. Invoice Actions

**View Details:**
- Click "View Details" button
- See complete invoice information
- Tenant details with PACI number
- Property and unit information
- Payment summary showing Total, Paid, and Balance Due
- Payment history if payments exist

**Mark as Paid:**
- Available for Pending/Overdue invoices
- Click "Mark as Paid" button
- Updates: PaidAmount = TotalAmount, Status = "Paid"
- Success message displayed

**Mark as Overdue:**
- Available for Pending invoices past due date
- Click "Mark as Overdue" button
- Updates: Status = "Overdue"
- Red badge applied

**Delete Invoice:**
- Click "Delete" button
- Confirmation page with full details
- Permanent deletion after confirmation

---

## Advanced Reports with Date Filters

### Access Reports
Navigate to: **Financial → Reports**

### New Report Interface

#### Report Configuration Section

**1. Select Report Type**
Dropdown with 7 options:
- **Tenant Report** - Export all tenants
- **Property Report** - Export all properties
- **Lease Report** - Export all leases
- **Payment Report** - Monthly payment analysis
- **Maintenance Report** - Export maintenance requests
- **Invoice Report** - Monthly invoice analysis
- **Revenue Summary** - Monthly revenue breakdown

**2. Date Range Filters**

**Manual Selection:**
- **From Date** - Select start date
- **To Date** - Select end date

**Quick Filters (Buttons):**
- **This Month** - Current month (1st to today)
- **Last Month** - Previous month (1st to last day)
- **This Quarter** - Current quarter (Jan-Mar, Apr-Jun, Jul-Sep, Oct-Dec)
- **This Year** - January 1st to today

#### Generate Report Button
1. Select report type
2. Choose date range
3. Click "Generate Report"
4. Data grid appears with monthly breakdown

#### Export to Excel Button
- Appears after generating report
- Downloads Excel file with selected data
- Professional formatting with blue headers
- KWD currency with 3 decimals

---

## Data Grid Examples

### 1. Payment Report Grid

**Columns:**
- Month (e.g., "Oct 2024")
- Total Payments (count)
- Total Amount (KWD XXX.XXX)
- Avg Payment (KWD XXX.XXX)
- Payment Count

**Totals Row:**
- Shows sum of all months
- Grand total in KWD

**Example:**
```
Month      | Total Payments | Total Amount  | Avg Payment | Payment Count
-----------|----------------|---------------|-------------|---------------
Sep 2024   | 15            | KWD 5,250.000 | KWD 350.000| 15
Oct 2024   | 17            | KWD 6,800.000 | KWD 400.000| 17
Nov 2024   | 12            | KWD 4,500.000 | KWD 375.000| 12
-----------|----------------|---------------|-------------|---------------
TOTAL      | 44            | KWD 16,550.000| KWD 376.136| 44
```

### 2. Revenue Summary Grid

**Columns:**
- Month
- Property Count
- Total Rent (KWD) - from invoices
- Payments Received (KWD)
- Outstanding (KWD)

**Calculation:**
- Outstanding = Total Rent - Payments Received

**Example:**
```
Month      | Properties | Total Rent    | Payments Recv | Outstanding
-----------|------------|---------------|---------------|-------------
Sep 2024   | 5         | KWD 7,200.000 | KWD 6,800.000| KWD 400.000
Oct 2024   | 5         | KWD 7,200.000 | KWD 7,200.000| KWD 0.000
Nov 2024   | 5         | KWD 7,200.000 | KWD 5,500.000| KWD 1,700.000
-----------|------------|---------------|---------------|-------------
TOTAL      | 5         | KWD 21,600.000| KWD 19,500.000| KWD 2,100.000
```

### 3. Invoice Report Grid

**Columns:**
- Month
- Invoices Generated (count)
- Total Amount (KWD)
- Paid (KWD)
- Pending (KWD)
- Overdue (KWD)

**Example:**
```
Month      | Generated | Total Amount  | Paid          | Pending       | Overdue
-----------|-----------|---------------|---------------|---------------|-------------
Sep 2024   | 17       | KWD 7,200.000 | KWD 6,800.000| KWD 0.000    | KWD 400.000
Oct 2024   | 17       | KWD 7,200.000 | KWD 7,200.000| KWD 0.000    | KWD 0.000
Nov 2024   | 17       | KWD 7,200.000 | KWD 0.000    | KWD 7,200.000| KWD 0.000
-----------|-----------|---------------|---------------|---------------|-------------
TOTAL      | 51       | KWD 21,600.000| KWD 14,000.000| KWD 7,200.000| KWD 400.000
```

---

## Excel Export Features

### All Export Reports Include:

**Formatting:**
- Professional blue header row (#3b82f6)
- White text on headers
- Borders on all cells
- Auto-fitted columns
- Currency formatted as "KWD" with 3 decimals (###,##0.000)

**Date-Filtered Exports:**
All standard exports (Tenant, Property, Lease, Payment, Maintenance) now accept date range:
- TenantReport?fromDate=2024-10-01&toDate=2024-10-31
- PropertyReport?fromDate=2024-10-01&toDate=2024-10-31
- LeaseReport?fromDate=2024-10-01&toDate=2024-10-31
- PaymentReport?fromDate=2024-10-01&toDate=2024-10-31
- MaintenanceReport?fromDate=2024-10-01&toDate=2024-10-31

**New Export Reports:**
- InvoiceReport - All invoices with amounts in KWD
- RevenueSummary - Monthly revenue breakdown with collection rates

**Filename Format:**
- TenantReport_20241102_143025.xlsx
- RevenueSummary_20241102_143025.xlsx
- InvoiceReport_20241102_143025.xlsx

---

## Workflow Examples

### Monthly Invoice Generation Workflow

**Step 1: Beginning of Month**
1. Navigate to Invoices
2. Click "Generate Monthly Invoices"
3. System creates invoices for all active leases

**Step 2: Review Invoices**
1. Check summary cards for total pending amount
2. View list of all generated invoices
3. Verify amounts match lease agreements

**Step 3: After Payments Received**
1. Find invoice for specific tenant
2. Click "Mark as Paid"
3. Invoice status updates to "Paid"
4. Paid amount card updates

**Step 4: Handle Overdue Invoices**
1. Use "Overdue" filter tab
2. Review invoices past due date
3. Click "Mark as Overdue" if needed
4. Follow up with tenants

### Monthly Reporting Workflow

**Step 1: Generate Payment Report**
1. Go to Reports
2. Select "Payment Report" from dropdown
3. Click "This Month" quick filter
4. Click "Generate Report"
5. Review monthly payment grid

**Step 2: Generate Revenue Summary**
1. Select "Revenue Summary" from dropdown
2. Click "This Quarter" quick filter
3. Click "Generate Report"
4. Review property performance and outstanding amounts

**Step 3: Export for Records**
1. Click "Export to Excel"
2. Save file for accounting records
3. Share with management

**Step 4: Invoice Analysis**
1. Select "Invoice Report" from dropdown
2. Set custom date range (e.g., last 6 months)
3. Click "Generate Report"
4. Analyze paid vs pending vs overdue trends

---

## Testing Checklist

### Invoice Module
- [ ] Dashboard loads with correct summary cards
- [ ] All invoices display in table
- [ ] Filter tabs work (All, Pending, Paid, Overdue)
- [ ] "Generate Monthly Invoices" creates new invoices
- [ ] Duplicate invoices not created for same month
- [ ] Invoice amounts match lease MonthlyRent
- [ ] Due dates calculated correctly from PaymentDueDay
- [ ] "Mark as Paid" updates status and amounts
- [ ] "Mark as Overdue" changes status correctly
- [ ] Invoice Details shows all information
- [ ] Delete invoice works with confirmation
- [ ] All amounts display in KWD with 3 decimals

### Reports Module
- [ ] Report type dropdown shows all 7 options
- [ ] Date range pickers work
- [ ] Quick filter buttons set correct dates
- [ ] "Generate Report" displays data grid
- [ ] Payment Report shows monthly breakdown
- [ ] Revenue Summary calculates correctly
- [ ] Invoice Report aggregates by month
- [ ] Totals row calculates correctly
- [ ] "Export to Excel" downloads file
- [ ] Excel file opens correctly
- [ ] Excel currency formatted as KWD with 3 decimals
- [ ] Excel headers have blue background
- [ ] Quick export cards work for all report types

---

## Technical Notes

### Automatic Invoice Generation Logic
```
For each active lease:
  - Check if invoice exists for current month
  - If not exists:
    - Create invoice with:
      - Invoice Date = Today
      - Due Date = PaymentDueDay of current month
      - Total Amount = MonthlyRent
      - Paid Amount = 0
      - Status = "Pending"
      - Type = "Rent"
```

### Status Transitions
- **Pending** → Paid (via "Mark as Paid")
- **Pending** → Overdue (via "Mark as Overdue" or automatic)
- **Overdue** → Paid (via "Mark as Paid")

### Report Data Aggregation
- Data grouped by Month-Year
- Amounts summed for each month
- Averages calculated from monthly totals
- Outstanding = Total - Paid

---

**System Version:** 1.0 Kuwait Edition with Invoice Automation
**Last Updated:** November 2, 2025
**Currency:** Kuwaiti Dinar (KWD) - 3 decimal places
