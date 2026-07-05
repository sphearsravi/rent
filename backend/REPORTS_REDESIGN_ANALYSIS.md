# Reports Module - Deep Analysis and Redesign

## Business Context: Kuwait Property Management

### Key Business Requirements
1. **Multi-property portfolio management** - Track performance across multiple properties
2. **Tenant relationship management** - Monitor tenant payment behavior and lease status
3. **Financial compliance** - Track revenue, expenses, and outstanding amounts in KWD
4. **Occupancy optimization** - Maximize property utilization
5. **Maintenance oversight** - Control costs and response times
6. **Payment collection** - Identify late payers and optimize cash flow
7. **Lease lifecycle management** - Track renewals, expirations, and vacancies

---

## Current Database Schema Analysis

### Core Entities and Relationships

```
Company (1) ──→ (Many) Properties
                      │
                      └──→ (Many) Units
                                  │
                                  └──→ (Many) Leases ←──── (Many) Tenants
                                              │
                                              ├──→ (Many) Payments
                                              ├──→ (Many) Invoices
                                              └──→ (Many) MaintenanceRequests

Additional: Documents (linked to any entity)
```

### Available Data Points

**Properties:**
- PropertyName, PACINumber, PropertyType, Address, City
- NumberOfUnits, PropertyValue, PurchaseDate
- Manager assignment

**Units:**
- UnitNumber, UnitType (Apartment/Commercial)
- Bedrooms, Bathrooms, SquareFeet
- MonthlyRent, Status (Available/Occupied/Maintenance)

**Tenants:**
- FirstName, LastName, PACINumber, Email, Phone
- DateOfBirth, EmploymentStatus
- EmergencyContact, Status (Active/Inactive)

**Leases:**
- LeaseStartDate, LeaseEndDate
- MonthlyRent, SecurityDeposit
- PaymentDueDay, Status (Active/Expired/Terminated)
- LateFeeAmount, LateFeeGracePeriod

**Payments:**
- PaymentDate, AmountPaid
- PaymentMethod (Cash/Check/Bank Transfer/Online)
- PaymentType (Rent/Deposit/Late Fee/Maintenance)
- Status (Completed/Pending/Failed)

**Invoices:**
- InvoiceDate, DueDate
- TotalAmount, PaidAmount
- Status (Paid/Pending/Overdue/Partial)
- InvoiceType (Rent/Utilities/Maintenance)

**MaintenanceRequests:**
- RequestDate, CompletedDate
- Category, Priority (Low/Medium/High/Emergency)
- Status (Pending/In Progress/Completed/Cancelled)
- EstimatedCost, ActualCost

**Documents:**
- DocumentType, FileSize, UploadedDate
- RelatedEntityType, RelatedEntityId

---

## Critical Business Questions (What Management Needs to Know)

### 1. Financial Performance
- What is my total rental income this month/quarter/year?
- What is my collection rate (paid vs. invoiced)?
- How much is outstanding (overdue)?
- What are my maintenance costs by property?
- What is my ROI per property?
- Which properties are most/least profitable?

### 2. Occupancy & Utilization
- What is my current occupancy rate?
- How many units are vacant?
- What is the average vacancy period?
- Which properties have highest/lowest occupancy?
- What is the unit type distribution (residential vs. commercial)?

### 3. Tenant Management
- Who are my late payers?
- Which tenants have expiring leases (next 30/60/90 days)?
- What is the tenant turnover rate?
- Which tenants have multiple active leases?
- What is the average lease duration?

### 4. Payment Analysis
- What is the payment collection timeline?
- Which payment methods are most used?
- What is the average payment delay?
- How much in late fees was collected?
- Payment trends over time

### 5. Maintenance Oversight
- What is the average maintenance response time?
- Which properties have most maintenance requests?
- What are the common maintenance issues?
- What is the maintenance cost per property?
- Emergency vs. routine maintenance ratio

### 6. Property Portfolio Health
- Property-by-property performance comparison
- Revenue per square foot
- Cost per unit
- Net operating income (NOI)
- Property aging analysis

---

## Redesigned Report Structure

### Report 1: **Executive Dashboard Report**
**Purpose:** High-level overview of entire portfolio

**Key Metrics:**
- Total Properties, Total Units, Occupied Units, Vacant Units
- Occupancy Rate %
- Total Monthly Rent (Potential), Total Monthly Rent (Actual)
- Total Outstanding Amount
- Total Maintenance Costs (This Month)
- Active Tenants Count, Expiring Leases (Next 30 Days)

**Breakdown By:**
- Current Month vs. Previous Month
- Year-to-Date totals
- Property Type (Apartment vs. Commercial)

**Excel Columns:**
```
Metric | Current Value | Previous Month | Change % | YTD Total | Notes
```

---

### Report 2: **Property Performance Report**
**Purpose:** Individual property analysis and comparison

**For Each Property:**
- Property Name, PACI Number, Location
- Total Units, Occupied Units, Vacant Units, Occupancy Rate %
- Total Monthly Rent (Potential), Total Monthly Rent (Actual), Collection Rate %
- Outstanding Amount, Overdue Amount
- Maintenance Requests (Total, Pending, Completed)
- Maintenance Costs (This Month, YTD)
- Net Revenue (Rent - Maintenance)
- Revenue per Square Foot

**Group By:** Property
**Sort By:** Net Revenue (Descending)

**Excel Columns:**
```
Property Name | PACI | Location | Total Units | Occupied | Vacant | Occupancy % |
Potential Rent (KWD) | Actual Rent (KWD) | Collection % | Outstanding (KWD) |
Maintenance Cost (KWD) | Net Revenue (KWD) | Revenue/SqFt
```

---

### Report 3: **Tenant Payment Analysis Report**
**Purpose:** Monitor tenant payment behavior and identify issues

**For Each Tenant:**
- Tenant Name, PACI Number, Contact
- Property Name, Unit Number
- Lease Start Date, Lease End Date, Status
- Monthly Rent Amount
- Total Invoices, Total Paid, Total Outstanding
- Last Payment Date, Last Payment Amount
- Payment Status (Current/Late/Defaulted)
- Days Overdue (if applicable)
- Late Fees Charged
- Payment History Score (Good/Fair/Poor)

**Filter By:**
- Payment Status (All/Current/Late/Defaulted)
- Property
- Date Range

**Sort By:** Outstanding Amount (Descending)

**Excel Columns:**
```
Tenant Name | PACI | Phone | Property | Unit | Lease Status | Monthly Rent (KWD) |
Total Invoiced (KWD) | Total Paid (KWD) | Outstanding (KWD) | Days Overdue |
Late Fees (KWD) | Last Payment Date | Payment Score | Action Required
```

---

### Report 4: **Cash Flow Report**
**Purpose:** Track money in and money out

**Monthly Breakdown:**
- Rent Collected (KWD)
- Security Deposits Collected (KWD)
- Late Fees Collected (KWD)
- Other Income (KWD)
- **Total Income (KWD)**

- Maintenance Expenses (KWD)
- Property Expenses (KWD)
- **Total Expenses (KWD)**

- **Net Cash Flow (KWD)**

**Cumulative Running Total**

**Excel Columns:**
```
Month | Rent Income | Deposits | Late Fees | Other Income | Total Income |
Maintenance Exp | Property Exp | Total Expenses | Net Cash Flow | Cumulative Total
```

---

### Report 5: **Lease Expiration Report**
**Purpose:** Proactive lease renewal management

**For Each Expiring Lease:**
- Tenant Name, PACI, Phone, Email
- Property Name, Unit Number, Unit Type
- Lease Start Date, Lease End Date
- Days Until Expiration
- Current Monthly Rent
- Lease Duration (Months)
- Renewal Status (Not Contacted/In Discussion/Renewed/Terminating)
- Tenant Payment History (Good/Fair/Poor)
- Action Required

**Filter By:**
- Expiration Period (Next 30/60/90 days)
- Property
- Renewal Status

**Sort By:** Days Until Expiration (Ascending)

**Excel Columns:**
```
Tenant Name | PACI | Contact | Property | Unit | Lease End Date | Days Until Expiry |
Current Rent (KWD) | Payment History | Renewal Status | Last Contact Date | Notes | Action
```

---

### Report 6: **Occupancy Analysis Report**
**Purpose:** Maximize unit utilization

**By Property:**
- Property Name, Total Units
- Occupied Units, Vacant Units, Under Maintenance
- Occupancy Rate %, Vacancy Rate %
- Average Rent per Unit (KWD)
- Total Potential Revenue (KWD)
- Total Actual Revenue (KWD)
- Revenue Loss Due to Vacancy (KWD)

**By Unit Type:**
- Apartments vs. Commercial breakdown
- Bedroom count distribution (Studio/1BR/2BR/3BR+)

**Vacancy Details:**
- Unit Number, Unit Type, Days Vacant
- Last Lease End Date, Reason for Vacancy
- Market Rent (KWD)

**Excel Columns:**
```
Property | Total Units | Occupied | Vacant | Maintenance | Occupancy % |
Avg Rent (KWD) | Potential Revenue (KWD) | Actual Revenue (KWD) | Revenue Loss (KWD)

[Separate Sheet: Vacant Units Detail]
Property | Unit No | Type | Bedrooms | Days Vacant | Last Tenant | Market Rent (KWD) | Status
```

---

### Report 7: **Maintenance Cost Analysis Report**
**Purpose:** Control maintenance expenses

**By Property:**
- Property Name, Total Units
- Total Maintenance Requests (This Month, YTD)
- Completed Requests, Pending Requests
- Average Response Time (Days)
- Total Maintenance Cost (KWD) - This Month, YTD
- Cost per Unit (KWD)
- Cost per Square Foot (KWD)

**By Category:**
- Plumbing, Electrical, HVAC, Appliance, General, Emergency
- Request Count, Total Cost, Average Cost per Request

**By Priority:**
- Emergency vs. High vs. Medium vs. Low
- Response time compliance

**Top 10 Most Expensive Requests**

**Excel Columns:**
```
Property | Total Requests | Completed | Pending | Avg Response Days | Total Cost (KWD) |
Cost per Unit | Cost per SqFt | Trend vs Last Month

[By Category Sheet]
Category | Request Count | Total Cost (KWD) | Avg Cost per Request | % of Total Cost

[Details Sheet]
Property | Unit | Category | Priority | Request Date | Completed Date | Days to Complete | Cost (KWD) | Status
```

---

### Report 8: **Invoice Status Report**
**Purpose:** Track billing and collection

**Summary:**
- Total Invoices Generated
- Total Amount Invoiced (KWD)
- Total Amount Paid (KWD)
- Total Outstanding (KWD)
- Collection Rate %

**By Status:**
- Paid: Count, Amount (KWD)
- Pending: Count, Amount (KWD)
- Overdue: Count, Amount (KWD), Average Days Overdue
- Partial: Count, Amount Invoiced, Amount Paid, Remaining

**Aging Analysis:**
- Current (0-30 days): Count, Amount (KWD)
- 31-60 days: Count, Amount (KWD)
- 61-90 days: Count, Amount (KWD)
- 90+ days: Count, Amount (KWD)

**By Property:**
- Property breakdown of invoice status

**Excel Columns:**
```
Invoice # | Tenant | Property | Unit | Invoice Date | Due Date | Amount (KWD) |
Paid Amount (KWD) | Outstanding (KWD) | Status | Days Overdue | Payment Method

[Summary Sheet]
Status | Count | Total Amount (KWD) | % of Total

[Aging Sheet]
Age Range | Count | Amount (KWD) | % of Total | Action Required
```

---

### Report 9: **Tenant Portfolio Report**
**Purpose:** Complete tenant overview

**For Each Tenant:**
- Tenant Name, PACI, Email, Phone
- Employment Status, Emergency Contact
- Number of Active Leases
- Properties & Units Occupied
- Total Monthly Rent (KWD)
- Total Security Deposits Held (KWD)
- Tenant Since (First Lease Date)
- Total Lifetime Payments (KWD)
- Current Outstanding (KWD)
- Payment Score (Excellent/Good/Fair/Poor)
- Late Payment Count (Last 12 Months)
- Maintenance Requests Filed (Last 12 Months)
- Documents on File (Count)
- Status (Active/Inactive)

**Filter By:**
- Status, Payment Score, Property

**Excel Columns:**
```
Tenant Name | PACI | Contact | Employment Status | Active Leases | Units Occupied |
Total Monthly Rent (KWD) | Deposits Held (KWD) | Tenant Since | Lifetime Payments (KWD) |
Outstanding (KWD) | Payment Score | Late Payments (12M) | Maintenance Requests (12M) | Status
```

---

### Report 10: **Revenue Forecast Report**
**Purpose:** Project future income

**By Month (Next 12 Months):**
- Expected Rent Income (from active leases)
- Expected Lease Renewals
- Expected New Leases
- Expected Terminations
- Projected Vacancy Impact
- **Total Projected Revenue (KWD)**

**By Property:**
- Property-level revenue projections

**Risk Factors:**
- Expiring leases without renewal confirmation
- High-risk tenants (poor payment history)
- Units requiring maintenance

**Excel Columns:**
```
Month | Active Leases | Expected Rent (KWD) | Renewals Expected | New Leases Expected |
Terminations Expected | Vacancy Impact (KWD) | Projected Revenue (KWD) | Confidence Level

[By Property Sheet]
Property | Current Monthly Revenue | Projected Monthly Revenue (3M) | (6M) | (12M) | Growth %
```

---

## Report Field Definitions

### Financial Fields
- **Potential Rent:** Sum of all unit rents (occupied + vacant)
- **Actual Rent:** Sum of rents from occupied units only
- **Collection Rate:** (Total Paid / Total Invoiced) × 100%
- **Outstanding:** Total Invoiced - Total Paid
- **Overdue:** Invoices past due date with unpaid balance
- **Net Revenue:** Rent Income - Maintenance Costs - Other Expenses
- **Revenue per SqFt:** Total Rent / Total Square Feet

### Occupancy Fields
- **Occupancy Rate:** (Occupied Units / Total Units) × 100%
- **Vacancy Rate:** (Vacant Units / Total Units) × 100%
- **Days Vacant:** Days since last lease ended
- **Revenue Loss:** (Vacant Units × Average Rent) × Days Vacant / 30

### Tenant Fields
- **Payment Score:**
  - Excellent: No late payments, 100% on-time
  - Good: 1-2 late payments in 12 months
  - Fair: 3-5 late payments in 12 months
  - Poor: 6+ late payments or current default
- **Days Overdue:** Days past invoice due date
- **Lifetime Payments:** Sum of all payments ever made by tenant

### Maintenance Fields
- **Response Time:** Days between request date and completion date
- **Cost per Unit:** Total Maintenance Cost / Total Units
- **Cost per SqFt:** Total Maintenance Cost / Total Square Feet

---

## Implementation Priority

### Phase 1: Essential Reports (Implement First)
1. **Executive Dashboard** - Overall portfolio health
2. **Property Performance** - Individual property tracking
3. **Tenant Payment Analysis** - Collection management
4. **Invoice Status** - Billing oversight

### Phase 2: Operational Reports
5. **Lease Expiration** - Renewal planning
6. **Occupancy Analysis** - Utilization optimization
7. **Cash Flow** - Financial tracking

### Phase 3: Strategic Reports
8. **Maintenance Cost Analysis** - Expense control
9. **Tenant Portfolio** - Relationship management
10. **Revenue Forecast** - Future planning

---

## Date Range Filters (All Reports)

Standard filters for all reports:
- **This Month** - Current calendar month
- **Last Month** - Previous calendar month
- **This Quarter** - Current 3-month period
- **Last Quarter** - Previous 3-month period
- **This Year** - Current calendar year
- **Last Year** - Previous calendar year
- **Custom Range** - User-specified from/to dates
- **Last 30 Days** - Rolling 30-day period
- **Last 90 Days** - Rolling 90-day period
- **Last 12 Months** - Rolling 12-month period

---

## Export Formats

All reports should export to:
1. **Excel (.xlsx)** - Primary format
   - Multiple sheets for complex reports
   - Formatted cells with KWD currency (3 decimals)
   - Column auto-fit
   - Header styling
   - Freeze panes for scrolling

2. **PDF (.pdf)** - For printing/sharing
   - Professional formatting
   - Page headers/footers
   - Company logo
   - Page numbers

3. **CSV (.csv)** - For data import to other systems

---

## Next Steps

1. ✅ Review and approve report structure
2. Create new ReportsController with all 10 reports
3. Create enhanced report views with filters
4. Implement Excel export with EPPlus
5. Add PDF export capability
6. Create sample data for testing
7. Build report scheduling feature (future)
8. Add email delivery of reports (future)

---

**Status:** Analysis complete, awaiting approval to proceed with implementation
