# Testing Guide - Cash & Bank Transfer Payment Module

## Application Details
- **URL**: http://localhost:5032
- **Status**: Running ✅
- **Environment**: Development

---

## Test Accounts

### Tenant Login Credentials

| Tenant Name | Civil ID (PACI Number) | Password | Pending Invoices |
|-------------|------------------------|----------|------------------|
| Ahmed Al-Rashid | 265123456789 | Welcome@123 | 1 |
| Sarah Al-Mutairi | 265234567890 | Welcome@123 | 2 |
| Mohammed Al-Sabah | 265345678901 | Welcome@123 | 2 |

---

## Testing Checklist

### Step 1: Login to Tenant Portal
1. Navigate to: http://localhost:5032/TenantPortal/Login
2. Enter Civil ID: `265234567890` (Sarah Al-Mutairi - has 2 invoices)
3. Enter Password: `Welcome@123`
4. Click "Login"
5. ✅ Should redirect to Dashboard

### Step 2: Navigate to Make Payment Page
1. From Dashboard, click "Make Payment" in the navigation menu
2. **EXPECTED NEW UI**:
   - Visual invoice cards (NOT a dropdown)
   - Each card shows:
     - Invoice number
     - Status badge (Pending/Overdue)
     - Property name and unit
     - Due date
     - Amount in KWD
     - "Select & Pay" button
3. ✅ Should see 2 invoice cards for Sarah

### Step 3: Test Bank Transfer Upload
1. Click "Select & Pay" on any invoice card
2. **Payment Method Modal should open** with 3 options:
   - KNET (online payment)
   - Bank Transfer (upload receipt)
   - Cash Payment (office visit)
3. Click on "Bank Transfer" card or "Upload Receipt" button
4. **Bank Transfer Form should open** with fields:
   - Amount (auto-filled, readonly)
   - Transfer Date (defaults to today)
   - Bank Name (dropdown)
   - Reference Number (optional)
   - File Upload (JPG/PNG/PDF, max 5MB)
   - Notes (optional)
5. Fill in the form:
   - Select a bank (e.g., "Gulf Bank")
   - Enter reference: "TEST123456"
   - Upload a test image file
   - Add notes: "Test bank transfer"
6. Click "Submit Transfer"
7. ✅ Should show success message
8. ✅ Page should reload showing remaining invoices

### Step 4: Verify API Endpoints (using Browser DevTools or Postman)
1. Open Browser DevTools (F12)
2. Go to Network tab
3. Submit a bank transfer
4. Look for POST request to `/api/payments/bank/upload`
5. ✅ Should return 200 OK with success response

---

## Troubleshooting

### Problem: Still seeing the old payment page (dropdown instead of cards)

**Possible Causes:**
1. Browser cache not cleared
2. View not recompiled
3. Wrong page being viewed

**Solutions:**

#### Solution 1: Hard Refresh Browser
- Press `Ctrl + Shift + R` (Chrome/Edge)
- Or `Ctrl + F5`
- Or open in Incognito/Private mode

#### Solution 2: Clear Browser Cache
1. Press `Ctrl + Shift + Delete`
2. Select "Cached images and files"
3. Click "Clear data"
4. Refresh the page

#### Solution 3: Verify the correct file is in place
Run this command to check the file timestamp:
```cmd
dir "C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\Views\TenantPortal\MakePayment.cshtml"
```

#### Solution 4: Force rebuild
```cmd
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet clean
dotnet build
```

#### Solution 5: Check if old file exists
```cmd
dir "C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\Views\TenantPortal\MakePayment*.cshtml"
```

---

## What You Should See (NEW UI)

### Old UI (if you're still seeing this):
```
[Dropdown: Select a lease]
[Payment Method: Radio buttons]
[Amount field]
[Submit button]
```

### NEW UI (what you should see):
```
┌─────────────────────────────────────────────────────┐
│  📄 Make Payment                                    │
│  Choose your payment method below                   │
└─────────────────────────────────────────────────────┘

Select Invoice to Pay
┌──────────────────┐  ┌──────────────────┐
│ Invoice #4       │  │ Invoice #5       │
│ [Pending]        │  │ [Pending]        │
│                  │  │                  │
│ 🏢 Property Name │  │ 🏢 Property Name │
│    Unit A-101    │  │    Unit B-202    │
│                  │  │                  │
│ 📅 Due: Nov 1    │  │ 📅 Due: Nov 1    │
│                  │  │                  │
│   380.000 KWD    │  │   420.000 KWD    │
│                  │  │                  │
│ [Select & Pay]   │  │ [Select & Pay]   │
└──────────────────┘  └──────────────────┘
```

When you click "Select & Pay":
```
Payment Method Modal:
┌─────────────────────────────────────────┐
│  ⚡ KNET             🏦 Bank Transfer    │
│  Pay online          Upload receipt     │
│  [Pay Now]           [Upload Receipt]   │
│                                         │
│  💰 Cash Payment                        │
│  Visit our office                       │
└─────────────────────────────────────────┘
```

---

## API Endpoints Available

### Tenant Endpoints (Authenticated)
- `POST /api/payments/bank/upload` - Submit bank transfer

### Admin Endpoints (Authenticated + Admin role)
- `POST /api/payments/cash/add` - Record cash payment
- `GET /api/payments/bank/list` - View pending transfers
- `POST /api/payments/bank/approve/{id}` - Approve transfer
- `POST /api/payments/bank/reject/{id}` - Reject transfer
- `GET /api/payments/bank/{id}/proof` - Download proof file

### API Documentation
- Swagger UI: http://localhost:5032/swagger

---

## Files Modified

1. **View**: `RentCollection.Web\Views\TenantPortal\MakePayment.cshtml`
   - Completely redesigned with visual invoice cards
   - Payment method selection modal
   - Bank transfer upload form
   - AJAX submission

2. **Controller**: `RentCollection.Web\Controllers\TenantPortalController.cs`
   - Line 202-230: Updated `MakePayment()` action
   - Now provides Invoices instead of Leases
   - Includes Property data for payment methods

3. **Backup**: `MakePayment_OLD.cshtml` (original file backed up)

---

## Expected Behavior

### Successful Bank Transfer Submission:
1. Form validates all required fields
2. File size checked (max 5MB)
3. AJAX POST to `/api/payments/bank/upload`
4. Success toast notification appears
5. Modals close automatically
6. Page reloads after 2 seconds
7. Invoice removed from list (status changed to PendingVerification)

### Database Changes After Submission:
- New record in `Payments` table with:
  - PaymentType = 3 (BankTransfer)
  - Status = 5 (PendingVerification)
- New record in `BankTransfers` table with:
  - File path to uploaded proof
  - Bank details
  - Pending verification status

---

## Next Steps for Complete Testing

1. ✅ Test tenant can see invoice cards
2. ✅ Test payment method selection modal
3. ✅ Test bank transfer upload
4. ⏳ Create admin page to approve/reject transfers
5. ⏳ Test admin approval workflow
6. ⏳ Verify invoice status updates after approval
7. ⏳ Test cash payment entry by admin

---

## Support

If you encounter any issues:
1. Check browser console (F12) for JavaScript errors
2. Check application logs in the terminal
3. Verify database connectivity
4. Ensure uploads folder exists and is writable
