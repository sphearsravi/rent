# Reports Implementation Plan

## Overview
This document outlines the implementation plan for all 21 reports in the Rent Collection System. Each report will have:
- Controller action for data retrieval
- HTML view with grid display
- Excel export functionality
- Date range filtering
- KWD currency formatting (3 decimals)

## Current Status
✅ Reports Dashboard with 5 sections created
✅ Owner Summary Report removed
✅ Tax/GST Report removed
⏳ 21 reports need full implementation

## Reports List

### Section 1: Tenant & Property Reports (5 reports)

#### 1. Tenant Summary Report
**Route:** `/Reports/TenantSummary`
**Data:**
- Tenant Name, PACI Number, Email, Phone
- Current Lease Status (Active/Inactive)
- Property & Unit Number
- Monthly Rent Amount
- Outstanding Balance
- Total Payments Made

**Controller Action:** `TenantSummary(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportTenantSummary(DateTime? fromDate, DateTime? toDate)`

#### 2. Tenant Ledger Report
**Route:** `/Reports/TenantLedger`
**Data:**
- Select Tenant Dropdown
- Transaction Date, Type (Invoice/Payment)
- Description
- Debit/Credit amounts
- Running Balance
- Payment Method

**Controller Action:** `TenantLedger(int? tenantId, DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportTenantLedger(int? tenantId, DateTime? fromDate, DateTime? toDate)`

#### 3. Vacant Units Report
**Route:** `/Reports/VacantUnits`
**Data:**
- Property Name & Address
- Unit Number
- Unit Type (Apartment/Villa/etc.)
- Square Feet
- Expected Monthly Rent
- Days Vacant
- Last Tenant Name
- Last Lease End Date

**Controller Action:** `VacantUnits()`
**Excel Export:** `ExportVacantUnits()`

#### 4. Lease Expiry Report
**Route:** `/Reports/LeaseExpiry`
**Data:**
- Tenant Name, Phone
- Property & Unit
- Lease Start Date
- Lease End Date
- Days Until Expiry
- Monthly Rent
- Status (Expiring Soon/Expired)
- Auto-Renewal Status

**Controller Action:** `LeaseExpiry(int daysAhead = 90)`
**Excel Export:** `ExportLeaseExpiry(int daysAhead = 90)`

#### 5. Tenant Activity Log
**Route:** `/Reports/TenantActivity`
**Data:**
- Tenant Name
- Activity Type (Login/Payment/Document Upload)
- Activity Date & Time
- IP Address (for logins)
- Description
- Status

**Controller Action:** `TenantActivity(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportTenantActivity(DateTime? fromDate, DateTime? toDate)`

---

### Section 2: Rent Collection & Payment Reports (5 reports)

#### 6. Rent Collection Summary
**Route:** `/Reports/RentCollectionSummary`
**Data:**
- Property Name
- Total Units, Occupied Units
- Expected Rent (monthly)
- Collected Rent
- Outstanding Rent
- Collection Rate %
- On-Time Payment %

**Controller Action:** `RentCollectionSummary(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportRentCollectionSummary(DateTime? fromDate, DateTime? toDate)`

#### 7. Rent Due Report
**Route:** `/Reports/RentDue`
**Data:**
- Tenant Name, Phone
- Property & Unit
- Invoice Number
- Due Date
- Days Overdue
- Amount Due
- Status (Pending/Overdue)

**Controller Action:** `RentDue()`
**Excel Export:** `ExportRentDue()`

#### 8. Payment Receipt Report
**Route:** `/Reports/PaymentReceipt`
**Data:**
- Receipt Number
- Payment Date
- Tenant Name
- Invoice Number
- Amount Paid (KWD)
- Payment Method (Cash/Bank Transfer/Check/Card)
- Reference Number
- Received By

**Controller Action:** `PaymentReceipt(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportPaymentReceipt(DateTime? fromDate, DateTime? toDate)`

#### 9. Advance Payment Report
**Route:** `/Reports/AdvancePayment`
**Data:**
- Tenant Name
- Property & Unit
- Advance Payment Date
- Amount Paid
- Period Covered (months)
- Remaining Balance
- Status

**Controller Action:** `AdvancePayment(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportAdvancePayment(DateTime? fromDate, DateTime? toDate)`

#### 10. Partial Payment Report
**Route:** `/Reports/PartialPayment`
**Data:**
- Invoice Number
- Tenant Name
- Property & Unit
- Total Invoice Amount
- Amount Paid
- Outstanding Balance
- Payment Date
- Days Since Invoice

**Controller Action:** `PartialPayment(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportPartialPayment(DateTime? fromDate, DateTime? toDate)`

---

### Section 3: Accounting & Finance Reports (4 reports)

#### 11. Monthly Revenue Report
**Route:** `/Reports/MonthlyRevenue`
**Data:**
- Month/Year
- Total Invoices Generated
- Total Amount Billed
- Total Payments Received
- Outstanding Amount
- Collection Rate %
- Revenue Growth % (vs previous month)

**Controller Action:** `MonthlyRevenue(int year = current year)`
**Excel Export:** `ExportMonthlyRevenue(int year = current year)`

#### 12. Expense Report
**Route:** `/Reports/Expense`
**Data:**
- Expense Date
- Property Name (if applicable)
- Category (Maintenance/Utilities/Insurance/Tax/Other)
- Vendor Name
- Description
- Amount (KWD)
- Payment Method
- Status (Paid/Pending)

**Controller Action:** `Expense(DateTime? fromDate, DateTime? toDate, int? propertyId)`
**Excel Export:** `ExportExpense(DateTime? fromDate, DateTime? toDate, int? propertyId)`

#### 13. Profit & Loss Statement
**Route:** `/Reports/ProfitLoss`
**Data:**
- Period (Monthly/Quarterly/Yearly)
- **Income:**
  - Rental Income
  - Late Fees
  - Other Income
  - **Total Income**
- **Expenses:**
  - Maintenance Costs
  - Property Taxes
  - Insurance
  - Utilities
  - Management Fees
  - Other Expenses
  - **Total Expenses**
- **Net Profit/Loss**
- Profit Margin %

**Controller Action:** `ProfitLoss(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportProfitLoss(DateTime? fromDate, DateTime? toDate)`

#### 14. Balance Sheet
**Route:** `/Reports/BalanceSheet`
**Data:**
- As of Date
- **Assets:**
  - Properties (total value)
  - Accounts Receivable (outstanding invoices)
  - Security Deposits Held
  - Cash/Bank Balance
  - **Total Assets**
- **Liabilities:**
  - Security Deposits Payable
  - Advance Rent Received
  - Accounts Payable
  - **Total Liabilities**
- **Equity:**
  - Owner's Equity
  - Retained Earnings
  - **Total Equity**

**Controller Action:** `BalanceSheet(DateTime? asOfDate)`
**Excel Export:** `ExportBalanceSheet(DateTime? asOfDate)`

---

### Section 4: Property Performance Reports (4 reports)

#### 15. Occupancy Rate Report
**Route:** `/Reports/OccupancyRate`
**Data:**
- Property Name
- Total Units
- Occupied Units
- Vacant Units
- Occupancy Rate %
- Average Rent per Unit
- Potential Monthly Revenue
- Actual Monthly Revenue
- Revenue Loss Due to Vacancy

**Controller Action:** `OccupancyRate(DateTime? asOfDate)`
**Excel Export:** `ExportOccupancyRate(DateTime? asOfDate)`

#### 16. Rent Growth Report
**Route:** `/Reports/RentGrowth`
**Data:**
- Property Name & Unit
- Previous Rent Amount
- Current Rent Amount
- Increase Amount (KWD)
- Increase Percentage %
- Effective Date
- Year-over-Year Growth
- Average Market Rent (if available)

**Controller Action:** `RentGrowth(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportRentGrowth(DateTime? fromDate, DateTime? toDate)`

#### 17. Collection Efficiency Report
**Route:** `/Reports/CollectionEfficiency`
**Data:**
- Property Name
- Total Billed Amount
- Total Collected Amount
- Outstanding Amount
- Collection Rate %
- Average Days to Collect
- On-Time Payment Rate %
- Late Payment Count
- Rating (Excellent/Good/Fair/Poor)

**Controller Action:** `CollectionEfficiency(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportCollectionEfficiency(DateTime? fromDate, DateTime? toDate)`

#### 18. Maintenance Cost Report
**Route:** `/Reports/MaintenanceCost`
**Data:**
- Property Name
- Maintenance Category (Plumbing/Electrical/HVAC/General)
- Request Date
- Completion Date
- Description
- Estimated Cost
- Actual Cost
- Status (Pending/In Progress/Completed)
- Cost Variance

**Controller Action:** `MaintenanceCost(DateTime? fromDate, DateTime? toDate, int? propertyId)`
**Excel Export:** `ExportMaintenanceCost(DateTime? fromDate, DateTime? toDate, int? propertyId)`

---

### Section 5: Owner / Admin Reports (3 reports)

#### 19. Multi-Property Comparison Report
**Route:** `/Reports/PropertyComparison`
**Data:**
- Property Name
- Total Units
- Occupancy Rate %
- Total Monthly Rent
- Collection Rate %
- Maintenance Costs
- Net Revenue
- ROI %
- Performance Ranking

**Controller Action:** `PropertyComparison(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportPropertyComparison(DateTime? fromDate, DateTime? toDate)`

#### 20. Pending Approvals Report
**Route:** `/Reports/PendingApprovals`
**Data:**
- Approval Type (Maintenance Request/Lease Renewal/Expense)
- Request Date
- Requested By
- Property/Tenant (if applicable)
- Description
- Amount (if applicable)
- Priority (High/Medium/Low)
- Days Pending
- Status

**Controller Action:** `PendingApprovals()`
**Excel Export:** `ExportPendingApprovals()`

#### 21. System Usage Report
**Route:** `/Reports/SystemUsage`
**Data:**
- User Name
- Role
- Last Login Date
- Total Logins
- Actions Performed (count)
- Documents Uploaded
- Reports Generated
- Active Status
- Account Created Date

**Controller Action:** `SystemUsage(DateTime? fromDate, DateTime? toDate)`
**Excel Export:** `ExportSystemUsage(DateTime? fromDate, DateTime? toDate)`

---

## Implementation Priority

### Phase 1 (High Priority - Most Used)
1. Tenant Summary Report
2. Rent Due Report
3. Payment Receipt Report
4. Monthly Revenue Report
5. Occupancy Rate Report

### Phase 2 (Medium Priority)
6. Vacant Units Report
7. Lease Expiry Report
8. Rent Collection Summary
9. Collection Efficiency Report
10. Maintenance Cost Report

### Phase 3 (Lower Priority - Admin/Analysis)
11. Tenant Ledger Report
12. Partial Payment Report
13. Advance Payment Report
14. Expense Report
15. Profit & Loss Statement
16. Balance Sheet
17. Rent Growth Report
18. Property Comparison
19. Pending Approvals
20. System Usage Report
21. Tenant Activity Log

## Technical Implementation Notes

### Controller Pattern
```csharp
[HttpGet]
public async Task<IActionResult> ReportName(DateTime? fromDate, DateTime? toDate)
{
    var startDate = fromDate ?? DateTime.Today.AddMonths(-1);
    var endDate = toDate ?? DateTime.Today;

    // Query data from database
    var data = await _context.TableName
        .Where(x => x.Date >= startDate && x.Date <= endDate)
        .ToListAsync();

    // Build view model
    var model = data.Select(x => new ReportViewModel { ... }).ToList();

    ViewBag.FromDate = startDate;
    ViewBag.ToDate = endDate;

    return View(model);
}

[HttpGet]
public async Task<IActionResult> ExportReportName(DateTime? fromDate, DateTime? toDate)
{
    // Same data query as above

    // Create Excel with EPPlus
    using var package = new ExcelPackage();
    var worksheet = package.Workbook.Worksheets.Add("Report Name");

    // Headers and data

    // Return file
    return File(package.GetAsByteArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"ReportName_{DateTime.Now:yyyyMMdd}.xlsx");
}
```

### View Pattern
```cshtml
@model List<ReportViewModel>
@{
    ViewData["Title"] = "Report Name";
}

<div class="container-fluid mt-4">
    <!-- Header with Export Button -->
    <!-- Date Range Filter -->
    <!-- Data Grid -->
    <!-- Summary Cards (if applicable) -->
</div>
```

## Next Steps
1. Prioritize which reports to implement first
2. Create controller actions one by one
3. Create corresponding views with grids
4. Test data display and Excel exports
5. Move to next priority report

**Total Estimated Time:** 20-30 hours for all 21 reports
**Per Report:** ~1-1.5 hours each
