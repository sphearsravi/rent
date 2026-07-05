# Cash & Bank Transfer Payment Module - Implementation Guide

## Overview
This guide provides complete implementation instructions for the Cash and Bank Transfer payment modules in the Rent Collection System, following SOLID design principles.

## Version Information
- **Version**: 2.2
- **Date**: 2025-11-13
- **Technology Stack**: ASP.NET Core 9 MVC + Web API, SQL Server 2022
- **Architecture**: Clean Architecture with SOLID Principles

---

## Table of Contents
1. [Architecture Overview](#architecture-overview)
2. [Database Setup](#database-setup)
3. [Dependency Injection Configuration](#dependency-injection-configuration)
4. [API Endpoints](#api-endpoints)
5. [Admin UI Implementation](#admin-ui-implementation)
6. [Tenant Portal Implementation](#tenant-portal-implementation)
7. [Testing the Implementation](#testing-the-implementation)
8. [Security Considerations](#security-considerations)

---

## Architecture Overview

### SOLID Principles Implementation

#### 1. Single Responsibility Principle (SRP)
- **CashPaymentService**: Handles only cash payment operations
- **BankTransferService**: Handles only bank transfer operations
- **FileStorageService**: Handles only file upload/download operations

#### 2. Open/Closed Principle (OCP)
- Payment types are extensible through `PaymentType` enum
- New payment methods can be added without modifying existing code
- Services implement interfaces for easy extension

#### 3. Liskov Substitution Principle (LSP)
- All payment services can be substituted through `IPaymentService` interface
- Derived classes maintain the contract of their base interfaces

#### 4. Interface Segregation Principle (ISP)
- Separate interfaces for different payment operations
- Clients only depend on methods they use

#### 5. Dependency Inversion Principle (DIP)
- Controllers depend on abstractions (interfaces), not concrete implementations
- Repository pattern abstracts data access

### Layer Structure

```
RentCollectionSystem/
├── RentCollection.Core/              # Domain Layer
│   ├── Entities/
│   │   ├── Payment.cs
│   │   ├── CashPayment.cs
│   │   ├── BankTransfer.cs
│   │   └── Property.cs (updated)
│   └── Enums/
│       ├── PaymentType.cs
│       ├── PaymentStatus.cs
│       └── BankTransferStatus.cs
│
├── RentCollection.Application/       # Application Layer
│   ├── Interfaces/
│   │   ├── IPaymentService.cs
│   │   ├── ICashPaymentService.cs
│   │   ├── IBankTransferService.cs
│   │   ├── IFileStorageService.cs
│   │   └── IRepository.cs
│   ├── Services/
│   │   ├── PaymentService.cs
│   │   ├── CashPaymentService.cs
│   │   └── BankTransferService.cs
│   └── DTOs/
│       ├── CashPaymentDto.cs
│       └── BankTransferDto.cs
│
├── RentCollection.Infrastructure/    # Infrastructure Layer
│   ├── Data/
│   │   └── ApplicationDbContext.cs (updated)
│   ├── Repositories/
│   │   └── Repository.cs
│   └── Services/
│       └── FileStorageService.cs
│
├── RentCollection.API/               # API Layer
│   └── Controllers/
│       ├── CashPaymentController.cs
│       └── BankTransferController.cs
│
└── RentCollection.Web/               # Web Layer (MVC)
    ├── Controllers/
    │   ├── CashPaymentController.cs
    │   └── BankTransferController.cs
    └── Views/
        ├── CashPayment/
        └── BankTransfer/
```

---

## Database Setup

### Step 1: Run the Migration Script

Execute the SQL script located at `Database/AddCashAndBankTransferPayments.sql`:

```bash
sqlcmd -S YOUR_SERVER -d RentCollectionDB -i Database/AddCashAndBankTransferPayments.sql
```

Or using SQL Server Management Studio:
1. Open the script file
2. Connect to your database
3. Execute the script

### Step 2: Verify Tables Created

Run this query to verify:

```sql
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('CashPayments', 'BankTransfers');
```

### New Database Tables

#### Payments Table (Updated)
- Added: `InvoiceId`, `PropertyId`, `PaymentType`, `Status`
- Added: `CollectedBy`, `ApprovedBy`, `ApprovedDate`, `ReceiptNumber`

#### CashPayments Table
| Column | Type | Description |
|--------|------|-------------|
| CashPaymentId | INT (PK) | Primary key |
| PaymentId | INT (FK) | Reference to Payments table |
| TenantId | INT (FK) | Reference to Tenants table |
| PropertyId | INT (FK) | Reference to Properties table |
| Amount | DECIMAL(18,2) | Payment amount |
| PaymentDate | DATETIME2 | Date of cash payment |
| CollectedBy | NVARCHAR(200) | Admin who collected cash |
| ReceiptNumber | NVARCHAR(100) | Generated receipt number |
| CreatedBy | NVARCHAR(200) | User who created record |

#### BankTransfers Table
| Column | Type | Description |
|--------|------|-------------|
| BankTransferId | INT (PK) | Primary key |
| PaymentId | INT (FK) | Reference to Payments table |
| TenantId | INT (FK) | Reference to Tenants table |
| PropertyId | INT (FK) | Reference to Properties table |
| Amount | DECIMAL(18,2) | Transfer amount |
| BankName | NVARCHAR(200) | Bank name |
| TransferDate | DATETIME2 | Date of transfer |
| ReferenceNumber | NVARCHAR(200) | Transaction reference |
| ProofFilePath | NVARCHAR(500) | Path to uploaded proof |
| Status | INT | 1=Pending, 2=Approved, 3=Rejected |
| SubmittedBy | NVARCHAR(200) | Tenant username |
| ApprovedBy | NVARCHAR(200) | Admin who approved/rejected |

---

## Dependency Injection Configuration

### Update Program.cs or Startup.cs

Add the following service registrations:

```csharp
// In RentCollection.Web/Program.cs or Startup.cs

using RentCollection.Application.Interfaces;
using RentCollection.Application.Services;
using RentCollection.Infrastructure.Repositories;
using RentCollection.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ... existing services ...

// Register Generic Repository
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// Register Payment Services
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICashPaymentService, CashPaymentService>();
builder.Services.AddScoped<IBankTransferService, BankTransferService>();

// Register File Storage Service
builder.Services.AddScoped<IFileStorageService>(provider =>
{
    var webHostEnvironment = provider.GetRequiredService<IWebHostEnvironment>();
    var uploadPath = Path.Combine(webHostEnvironment.WebRootPath, "uploads");
    return new FileStorageService(uploadPath);
});

var app = builder.Build();
```

### Update API Project (RentCollection.API/Program.cs)

```csharp
// Add the same service registrations as above
// Plus configure file upload settings

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5 * 1024 * 1024; // 5MB
});
```

---

## API Endpoints

### Cash Payment Endpoints

#### 1. Record Cash Payment
```http
POST /api/payments/cash/add
Authorization: Bearer {admin_token}
Content-Type: application/json

{
  "invoiceId": 123,
  "tenantId": 45,
  "propertyId": 10,
  "amount": 500.00,
  "paymentDate": "2025-11-13T10:30:00",
  "receiptNumber": "CASH-20251113-00001",
  "notes": "Payment for November rent"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Cash payment recorded successfully",
  "data": {
    "paymentId": 789,
    "cashPaymentId": 45,
    "receiptNumber": "CASH-20251113-00001",
    "amount": 500.00,
    "paymentDate": "2025-11-13T10:30:00",
    "status": "Completed",
    "tenantName": "John Doe",
    "propertyName": "Building A"
  }
}
```

#### 2. Get Cash Payments by Property
```http
GET /api/payments/cash/property/{propertyId}?startDate=2025-11-01&endDate=2025-11-30
Authorization: Bearer {admin_token}
```

#### 3. Get Cash Payments by Date
```http
GET /api/payments/cash/date/2025-11-13
Authorization: Bearer {admin_token}
```

### Bank Transfer Endpoints

#### 1. Submit Bank Transfer (Tenant)
```http
POST /api/payments/bank/upload
Authorization: Bearer {tenant_token}
Content-Type: multipart/form-data

FormData:
- tenantId: 45
- propertyId: 10
- invoiceId: 123
- amount: 500.00
- transferDate: 2025-11-13
- bankName: National Bank of Kuwait
- referenceNumber: TXN123456
- proofFile: [FILE]
- notes: Transfer for November rent
```

**Response:**
```json
{
  "success": true,
  "message": "Bank transfer submitted successfully. Awaiting admin verification.",
  "data": {
    "bankTransferId": 67,
    "paymentId": 790,
    "tenantId": 45,
    "tenantName": "John Doe",
    "propertyId": 10,
    "propertyName": "Building A",
    "amount": 500.00,
    "bankName": "National Bank of Kuwait",
    "transferDate": "2025-11-13T00:00:00",
    "referenceNumber": "TXN123456",
    "status": "PendingVerification",
    "submittedDate": "2025-11-13T10:45:00"
  }
}
```

#### 2. Get Pending Bank Transfers (Admin)
```http
GET /api/payments/bank/list
Authorization: Bearer {admin_token}
```

#### 3. Approve Bank Transfer
```http
POST /api/payments/bank/approve/67
Authorization: Bearer {admin_token}
```

#### 4. Reject Bank Transfer
```http
POST /api/payments/bank/reject/67
Authorization: Bearer {admin_token}
Content-Type: application/json

{
  "bankTransferId": 67,
  "approve": false,
  "rejectionReason": "Transfer amount does not match invoice"
}
```

#### 5. Download Transfer Proof
```http
GET /api/payments/bank/67/proof
Authorization: Bearer {admin_token}
```

---

## Admin UI Implementation

### Cash Payment Entry Page

Create `Views/CashPayment/Add.cshtml`:

```html
@model RentCollection.Application.DTOs.CashPaymentDto

<div class="container mt-4">
    <div class="card">
        <div class="card-header bg-success text-white">
            <h4>💵 Record Cash Payment</h4>
        </div>
        <div class="card-body">
            <form id="cashPaymentForm" method="post" asp-action="Add">
                <div class="row">
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="TenantId">Tenant</label>
                            <select asp-for="TenantId" class="form-control" required>
                                <option value="">-- Select Tenant --</option>
                                <!-- Populate from ViewBag.Tenants -->
                            </select>
                        </div>
                    </div>
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="PropertyId">Property</label>
                            <select asp-for="PropertyId" class="form-control" required>
                                <option value="">-- Select Property --</option>
                                <!-- Populate from ViewBag.Properties -->
                            </select>
                        </div>
                    </div>
                </div>

                <div class="row">
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="InvoiceId">Invoice (Optional)</label>
                            <select asp-for="InvoiceId" class="form-control">
                                <option value="">-- Select Invoice --</option>
                                <!-- Dynamically loaded based on tenant -->
                            </select>
                        </div>
                    </div>
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="Amount">Amount (KD)</label>
                            <input asp-for="Amount" type="number" step="0.01" class="form-control" required />
                        </div>
                    </div>
                </div>

                <div class="row">
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="PaymentDate">Payment Date</label>
                            <input asp-for="PaymentDate" type="date" class="form-control" required />
                        </div>
                    </div>
                    <div class="col-md-6">
                        <div class="form-group">
                            <label asp-for="ReceiptNumber">Receipt Number (Auto-generated if empty)</label>
                            <input asp-for="ReceiptNumber" type="text" class="form-control" />
                        </div>
                    </div>
                </div>

                <div class="form-group">
                    <label asp-for="Notes">Notes</label>
                    <textarea asp-for="Notes" class="form-control" rows="3"></textarea>
                </div>

                <div class="mt-3">
                    <button type="submit" class="btn btn-success">
                        <i class="fas fa-check"></i> Record Payment
                    </button>
                    <a href="@Url.Action("Index", "Payments")" class="btn btn-secondary">
                        <i class="fas fa-times"></i> Cancel
                    </a>
                </div>
            </form>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        $(document).ready(function() {
            // Auto-populate today's date
            $('#PaymentDate').val(new Date().toISOString().split('T')[0]);

            // Load invoices when tenant is selected
            $('#TenantId').change(function() {
                var tenantId = $(this).val();
                if (tenantId) {
                    $.get('/api/invoices/tenant/' + tenantId, function(data) {
                        var invoiceSelect = $('#InvoiceId');
                        invoiceSelect.empty();
                        invoiceSelect.append('<option value="">-- Select Invoice --</option>');
                        $.each(data.data, function(i, invoice) {
                            invoiceSelect.append($('<option></option>')
                                .val(invoice.invoiceId)
                                .text('Invoice #' + invoice.invoiceId + ' - KD ' + invoice.totalAmount));
                        });
                    });
                }
            });
        });
    </script>
}
```

### Bank Transfer Approval Page

Create `Views/BankTransfer/Approvals.cshtml`:

```html
@model IEnumerable<RentCollection.Application.DTOs.BankTransferResponseDto>

<div class="container mt-4">
    <div class="card">
        <div class="card-header bg-primary text-white">
            <h4>🏦 Bank Transfer Approvals</h4>
        </div>
        <div class="card-body">
            <table class="table table-striped" id="bankTransfersTable">
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>Tenant</th>
                        <th>Property</th>
                        <th>Amount</th>
                        <th>Bank</th>
                        <th>Transfer Date</th>
                        <th>Submitted Date</th>
                        <th>Proof</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var transfer in Model)
                    {
                        <tr>
                            <td>@transfer.BankTransferId</td>
                            <td>@transfer.TenantName</td>
                            <td>@transfer.PropertyName</td>
                            <td>KD @transfer.Amount</td>
                            <td>@transfer.BankName</td>
                            <td>@transfer.TransferDate.ToString("dd-MM-yyyy")</td>
                            <td>@transfer.SubmittedDate.ToString("dd-MM-yyyy HH:mm")</td>
                            <td>
                                <a href="@Url.Action("ViewProof", new { id = transfer.BankTransferId })"
                                   target="_blank" class="btn btn-sm btn-info">
                                    <i class="fas fa-file-image"></i> View
                                </a>
                            </td>
                            <td>
                                <button class="btn btn-sm btn-success approve-btn"
                                        data-id="@transfer.BankTransferId">
                                    <i class="fas fa-check"></i> Approve
                                </button>
                                <button class="btn btn-sm btn-danger reject-btn"
                                        data-id="@transfer.BankTransferId">
                                    <i class="fas fa-times"></i> Reject
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

<!-- Rejection Modal -->
<div class="modal fade" id="rejectionModal" tabindex="-1">
    <div class="modal-dialog">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">Reject Bank Transfer</h5>
                <button type="button" class="close" data-dismiss="modal">&times;</button>
            </div>
            <div class="modal-body">
                <div class="form-group">
                    <label>Rejection Reason</label>
                    <textarea id="rejectionReason" class="form-control" rows="3" required></textarea>
                </div>
            </div>
            <div class="modal-footer">
                <button type="button" class="btn btn-secondary" data-dismiss="modal">Cancel</button>
                <button type="button" class="btn btn-danger" id="confirmReject">Reject Transfer</button>
            </div>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        let selectedTransferId = null;

        $('.approve-btn').click(function() {
            const id = $(this).data('id');
            if (confirm('Are you sure you want to approve this bank transfer?')) {
                $.post('/api/payments/bank/approve/' + id, function(response) {
                    if (response.success) {
                        alert('Bank transfer approved successfully!');
                        location.reload();
                    } else {
                        alert('Error: ' + response.message);
                    }
                }).fail(function() {
                    alert('Failed to approve bank transfer.');
                });
            }
        });

        $('.reject-btn').click(function() {
            selectedTransferId = $(this).data('id');
            $('#rejectionModal').modal('show');
        });

        $('#confirmReject').click(function() {
            const reason = $('#rejectionReason').val();
            if (!reason) {
                alert('Please enter a rejection reason');
                return;
            }

            $.ajax({
                url: '/api/payments/bank/reject/' + selectedTransferId,
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({
                    bankTransferId: selectedTransferId,
                    approve: false,
                    rejectionReason: reason
                }),
                success: function(response) {
                    if (response.success) {
                        alert('Bank transfer rejected');
                        location.reload();
                    } else {
                        alert('Error: ' + response.message);
                    }
                },
                error: function() {
                    alert('Failed to reject bank transfer');
                }
            });
        });

        // Initialize DataTable
        $('#bankTransfersTable').DataTable({
            order: [[6, 'desc']]
        });
    </script>
}
```

---

## Tenant Portal Implementation

### Payment Options Page

Create `Views/TenantPortal/PayInvoice.cshtml`:

```html
@model RentCollection.Core.Entities.Invoice

<div class="container mt-4">
    <div class="card">
        <div class="card-header bg-info text-white">
            <h4>💳 Pay Invoice #@Model.InvoiceId</h4>
        </div>
        <div class="card-body">
            <div class="invoice-details mb-4">
                <h5>Invoice Details</h5>
                <p><strong>Amount:</strong> KD @Model.TotalAmount</p>
                <p><strong>Due Date:</strong> @Model.DueDate.ToString("dd-MM-yyyy")</p>
                <p><strong>Status:</strong> @Model.Status</p>
            </div>

            <h5>Select Payment Method:</h5>
            <div class="row mt-3">
                <!-- KNET Payment -->
                <div class="col-md-4">
                    <div class="card payment-option">
                        <div class="card-body text-center">
                            <i class="fas fa-credit-card fa-3x text-primary mb-3"></i>
                            <h5>KNET</h5>
                            <p>Pay online using KNET</p>
                            <button class="btn btn-primary" onclick="payWithKNET(@Model.InvoiceId)">
                                Pay Now
                            </button>
                        </div>
                    </div>
                </div>

                <!-- Bank Transfer -->
                <div class="col-md-4">
                    <div class="card payment-option">
                        <div class="card-body text-center">
                            <i class="fas fa-university fa-3x text-success mb-3"></i>
                            <h5>Bank Transfer</h5>
                            <p>Upload transfer proof</p>
                            <button class="btn btn-success" data-toggle="modal" data-target="#bankTransferModal">
                                Upload Proof
                            </button>
                        </div>
                    </div>
                </div>

                <!-- Cash Payment -->
                <div class="col-md-4">
                    <div class="card payment-option">
                        <div class="card-body text-center">
                            <i class="fas fa-money-bill-wave fa-3x text-warning mb-3"></i>
                            <h5>Cash</h5>
                            <p>Visit office to pay</p>
                            <div class="alert alert-info mt-2">
                                Please visit our office during business hours
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</div>

<!-- Bank Transfer Modal -->
<div class="modal fade" id="bankTransferModal" tabindex="-1">
    <div class="modal-dialog modal-lg">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title">Submit Bank Transfer</h5>
                <button type="button" class="close" data-dismiss="modal">&times;</button>
            </div>
            <form id="bankTransferForm" enctype="multipart/form-data">
                <div class="modal-body">
                    <input type="hidden" name="invoiceId" value="@Model.InvoiceId" />
                    <input type="hidden" name="tenantId" value="@Model.TenantId" />
                    <input type="hidden" name="propertyId" value="@ViewBag.PropertyId" />

                    <div class="form-group">
                        <label>Amount (KD)</label>
                        <input type="number" name="amount" step="0.01" class="form-control"
                               value="@Model.TotalAmount" required />
                    </div>

                    <div class="form-group">
                        <label>Bank Name</label>
                        <select name="bankName" class="form-control" required>
                            <option value="">-- Select Bank --</option>
                            <option>National Bank of Kuwait</option>
                            <option>Gulf Bank</option>
                            <option>Commercial Bank of Kuwait</option>
                            <option>Burgan Bank</option>
                            <option>Kuwait Finance House</option>
                            <option>Other</option>
                        </select>
                    </div>

                    <div class="form-group">
                        <label>Transfer Date</label>
                        <input type="date" name="transferDate" class="form-control" required />
                    </div>

                    <div class="form-group">
                        <label>Reference Number (Optional)</label>
                        <input type="text" name="referenceNumber" class="form-control" />
                    </div>

                    <div class="form-group">
                        <label>Upload Transfer Proof <span class="text-danger">*</span></label>
                        <input type="file" name="proofFile" class="form-control-file"
                               accept=".jpg,.jpeg,.png,.pdf" required />
                        <small class="form-text text-muted">
                            Accepted formats: JPG, PNG, PDF (Max 5MB)
                        </small>
                    </div>

                    <div class="form-group">
                        <label>Notes (Optional)</label>
                        <textarea name="notes" class="form-control" rows="3"></textarea>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-dismiss="modal">Cancel</button>
                    <button type="submit" class="btn btn-success">
                        <i class="fas fa-upload"></i> Submit Transfer
                    </button>
                </div>
            </form>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        function payWithKNET(invoiceId) {
            // Redirect to KNET gateway
            window.location.href = '/Payment/KNET?invoiceId=' + invoiceId;
        }

        $('#bankTransferForm').submit(function(e) {
            e.preventDefault();

            var formData = new FormData(this);

            $.ajax({
                url: '/api/payments/bank/upload',
                type: 'POST',
                data: formData,
                processData: false,
                contentType: false,
                success: function(response) {
                    if (response.success) {
                        alert('Bank transfer submitted successfully! Awaiting admin approval.');
                        location.reload();
                    } else {
                        alert('Error: ' + response.message);
                    }
                },
                error: function() {
                    alert('Failed to submit bank transfer. Please try again.');
                }
            });
        });

        // Set today's date as default
        $('input[name="transferDate"]').val(new Date().toISOString().split('T')[0]);
    </script>
}

<style>
    .payment-option {
        height: 100%;
        transition: transform 0.2s;
    }

    .payment-option:hover {
        transform: translateY(-5px);
        box-shadow: 0 4px 8px rgba(0,0,0,0.1);
    }
</style>
```

---

## Testing the Implementation

### 1. Test Cash Payment Flow

#### Admin Records Cash Payment:
```bash
curl -X POST http://localhost:5000/api/payments/cash/add \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": 1,
    "propertyId": 1,
    "invoiceId": 5,
    "amount": 350.00,
    "paymentDate": "2025-11-13T10:00:00",
    "notes": "November rent - cash payment"
  }'
```

#### Verify in Database:
```sql
SELECT * FROM Payments WHERE PaymentType = 2; -- Cash = 2
SELECT * FROM CashPayments;
```

### 2. Test Bank Transfer Flow

#### Tenant Submits Transfer:
```bash
curl -X POST http://localhost:5000/api/payments/bank/upload \
  -H "Authorization: Bearer YOUR_TENANT_TOKEN" \
  -F "tenantId=1" \
  -F "propertyId=1" \
  -F "invoiceId=5" \
  -F "amount=350.00" \
  -F "bankName=National Bank of Kuwait" \
  -F "transferDate=2025-11-13" \
  -F "referenceNumber=TXN987654" \
  -F "proofFile=@/path/to/transfer_slip.jpg"
```

#### Admin Gets Pending Transfers:
```bash
curl http://localhost:5000/api/payments/bank/list \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

#### Admin Approves Transfer:
```bash
curl -X POST http://localhost:5000/api/payments/bank/approve/1 \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

### 3. Verify Payment Updates Invoice

```sql
SELECT
    i.InvoiceId,
    i.TotalAmount,
    i.PaidAmount,
    i.Status,
    p.PaymentType,
    p.Status as PaymentStatus
FROM Invoices i
LEFT JOIN Payments p ON p.InvoiceId = i.InvoiceId
WHERE i.InvoiceId = 5;
```

---

## Security Considerations

### 1. File Upload Security

```csharp
// Validate file type
var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
if (!allowedExtensions.Contains(Path.GetExtension(file.FileName).ToLower()))
{
    throw new InvalidOperationException("Invalid file type");
}

// Validate file size
if (file.Length > 5 * 1024 * 1024) // 5MB
{
    throw new InvalidOperationException("File too large");
}

// Scan for viruses (optional)
// Use ClamAV or Windows Defender API
```

### 2. Authorization Rules

- **Cash Payments**: Only Admin and PropertyManager roles
- **Bank Transfer Upload**: Only Tenant role
- **Bank Transfer Approval**: Only Admin and PropertyManager roles
- **View Transfer Proof**: Admin, PropertyManager, and owning Tenant

### 3. Audit Logging

All payment operations log:
- User who performed action
- Timestamp
- IP address
- Action details

```csharp
_logger.LogInformation(
    "Cash payment {ReceiptNumber} recorded by {User} for tenant {TenantId}",
    receiptNumber, userName, tenantId);
```

### 4. Data Validation

- All monetary values use `decimal` type
- Foreign key constraints prevent orphaned records
- Status transitions are validated
- File paths are sanitized

---

## Troubleshooting

### Common Issues

#### 1. Build Errors Related to Payment Properties
**Problem**: `Payment.PaymentMethod` or `Payment.PaymentStatus` not found

**Solution**: Update existing code references to use the new enum properties:
- `PaymentMethod` → `PaymentType` (enum)
- `PaymentStatus` (string) → `Status` (enum)

#### 2. File Upload Fails
**Problem**: 413 Payload Too Large or 500 Internal Server Error

**Solution**: Configure file upload limits in `Program.cs`:
```csharp
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});
```

Also update `web.config`:
```xml
<system.webServer>
  <security>
    <requestFiltering>
      <requestLimits maxAllowedContentLength="10485760" /> <!-- 10MB -->
    </requestFiltering>
  </security>
</system.webServer>
```

#### 3. Foreign Key Constraint Errors
**Problem**: Cannot insert payment without valid PropertyId/TenantId

**Solution**: Ensure referenced entities exist before creating payment:
```sql
-- Verify tenant exists
SELECT * FROM Tenants WHERE TenantId = 1;

-- Verify property exists
SELECT * FROM Properties WHERE PropertyId = 1;
```

---

## Next Steps

### Phase 1: Core Implementation (Completed)
- ✅ Database schema
- ✅ Domain entities
- ✅ Service layer
- ✅ API controllers
- ✅ SQL migration script

### Phase 2: UI Implementation (Pending)
- ⏳ Admin cash payment UI
- ⏳ Admin bank transfer approval UI
- ⏳ Tenant payment portal UI
- ⏳ PDF receipt generation

### Phase 3: Integration (Pending)
- ⏳ KNET payment gateway integration
- ⏳ Email notifications
- ⏳ SMS notifications
- ⏳ Reporting dashboards

### Phase 4: Testing & Deployment
- ⏳ Unit tests
- ⏳ Integration tests
- ⏳ User acceptance testing
- ⏳ Production deployment

---

## Support & Documentation

### Additional Resources
- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/)
- [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/)
- [SOLID Principles](https://www.digitalocean.com/community/conceptual_articles/s-o-l-i-d-the-first-five-principles-of-object-oriented-design)

### Contact
For technical support, contact the development team.

---

**Document Version**: 1.0
**Last Updated**: 2025-11-13
**Author**: Development Team
