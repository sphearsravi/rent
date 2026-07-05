# How to Run the Rent Collection System with Cash & Bank Transfer Module

## ⚠️ Prerequisites

1. ✅ **Database Migration Applied** - Tables `CashPayments` and `BankTransfers` are created
2. ✅ **Dependencies Installed** - All NuGet packages restored
3. ✅ **Program.cs Updated** - Payment services registered

## 🚀 Running the Application

### Option 1: Run Web Application (Recommended)

1. **Stop any currently running instance**:
   - Press `Ctrl+C` in any terminal running the app
   - Or from Task Manager, end `RentCollection.Web.exe` process

2. **Start the application**:
   ```bash
   cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
   dotnet run
   ```

3. **Access the application**:
   - **Web UI**: https://localhost:5001
   - **Swagger/API**: https://localhost:5001/swagger

### Option 2: Run API Project Only (For Testing APIs)

If you want to test just the new Cash & Bank Transfer APIs without the UI:

```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.API
dotnet run
```

Access Swagger at: https://localhost:5002/swagger

## 📍 File Locations Summary

### Implementation Files Created:
```
C:\Users\r.shankar\RentCollectionSystem\
├── RentCollection.Core\
│   ├── Entities\
│   │   ├── Payment.cs (UPDATED - with backward compatibility)
│   │   ├── CashPayment.cs (NEW)
│   │   ├── BankTransfer.cs (NEW)
│   │   └── Property.cs (UPDATED)
│   └── Enums\
│       ├── PaymentType.cs (NEW)
│       ├── PaymentStatus.cs (NEW)
│       └── BankTransferStatus.cs (NEW)
│
├── RentCollection.Application\
│   ├── Interfaces\ (All NEW)
│   ├── Services\ (All NEW)
│   └── DTOs\ (All NEW)
│
├── RentCollection.Infrastructure\
│   ├── Data\ApplicationDbContext.cs (UPDATED)
│   ├── Repositories\Repository.cs (NEW)
│   └── Services\FileStorageService.cs (NEW)
│
├── RentCollection.API\Controllers\
│   ├── CashPaymentController.cs (NEW)
│   └── BankTransferController.cs (NEW)
│
├── RentCollection.Web\
│   ├── Program.cs (UPDATED - DI configured)
│   └── wwwroot\uploads\ (Created for file storage)
│
└── Database\
    └── AddCashAndBankTransferPayments.sql (EXECUTED)
```

### Documentation Files:
```
├── CASH_BANK_TRANSFER_IMPLEMENTATION_GUIDE.md (Full guide)
├── QUICK_START_CASH_BANK_PAYMENTS.md (Quick reference)
└── FIX_PAYMENT_PROPERTIES.md (Troubleshooting)
```

## 🧪 Testing the New Features

### 1. Test Cash Payment API

```bash
# Login as admin first (get token)
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin",
    "password": "your_password"
  }'

# Record cash payment
curl -X POST https://localhost:5001/api/payments/cash/add \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": 1,
    "propertyId": 1,
    "amount": 500.00,
    "paymentDate": "2025-11-13T10:00:00"
  }'
```

### 2. Test Bank Transfer API

```bash
# Login as tenant
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "tenant_username",
    "password": "tenant_password"
  }'

# Submit bank transfer (use Postman or form-data tool)
# POST https://localhost:5001/api/payments/bank/upload
# FormData:
#   tenantId: 1
#   propertyId: 1
#   amount: 500.00
#   transferDate: 2025-11-13
#   bankName: National Bank of Kuwait
#   proofFile: [FILE]
```

### 3. Access Swagger UI

Open browser: https://localhost:5001/swagger

You'll see the new endpoints:
- `/api/payments/cash/*`
- `/api/payments/bank/*`

## 🔧 Troubleshooting

### Build Errors (Payment Property References)

The existing code has been updated with backward compatibility properties. The app should now build successfully.

If you still see errors:
1. **Check if app is running**: Stop it with `Ctrl+C`
2. **Clean and rebuild**:
   ```bash
   cd C:\Users\r.shankar\RentCollectionSystem
   dotnet clean
   dotnet build
   ```

### Database Connection Issues

Verify connection string in `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=RAVIWST\\SQLEXPRESS;Database=RentCollectionDB;..."
}
```

### File Upload Issues

Ensure uploads directory exists:
```bash
mkdir -p RentCollection.Web/wwwroot/uploads/BankTransfers
```

## 📊 Verify Database Tables

Run this SQL to confirm tables were created:

```sql
USE RentCollectionDB;

-- Check tables
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('CashPayments', 'BankTransfers', 'Payments');

-- Check new columns in Payments table
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Payments'
  AND COLUMN_NAME IN ('PaymentType', 'Status', 'PropertyId', 'CollectedBy', 'ApprovedBy');

-- Check new columns in Properties table
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Properties'
  AND COLUMN_NAME IN ('AcceptKNET', 'AcceptCash', 'AcceptBankTransfer');
```

## 📘 Next Steps

Once the application is running:

1. **Login as Admin** - Test cash payment entry
2. **Login as Tenant** - Test bank transfer upload
3. **Review pending transfers** as Admin
4. **Approve/Reject transfers**
5. **Check invoice status updates**

## 📝 API Endpoints Available

### Cash Payment (Admin Only):
- `POST /api/payments/cash/add` - Record cash payment
- `GET /api/payments/cash/{id}` - Get payment by ID
- `GET /api/payments/cash/property/{propertyId}` - Get by property
- `GET /api/payments/cash/date/{date}` - Get by date

### Bank Transfer:
- `POST /api/payments/bank/upload` - Submit transfer (Tenant)
- `GET /api/payments/bank/list` - Get pending (Admin)
- `POST /api/payments/bank/approve/{id}` - Approve (Admin)
- `POST /api/payments/bank/reject/{id}` - Reject (Admin)
- `GET /api/payments/bank/{id}/proof` - Download proof

## 🎉 Success Indicators

✅ Application starts without errors
✅ Swagger UI shows new payment endpoints
✅ Cash payment API responds successfully
✅ Bank transfer upload works
✅ File uploads are saved to `wwwroot/uploads/BankTransfers`
✅ Admin can approve/reject transfers
✅ Invoice status updates correctly

---

**Need Help?**
- Check [CASH_BANK_TRANSFER_IMPLEMENTATION_GUIDE.md](CASH_BANK_TRANSFER_IMPLEMENTATION_GUIDE.md) for detailed implementation
- Check [QUICK_START_CASH_BANK_PAYMENTS.md](QUICK_START_CASH_BANK_PAYMENTS.md) for quick reference
- Check application logs in console
