# Complete Testing Guide - Rent Collection System
## Cash & Bank Transfer Payment Module

---

## 🎯 APPLICATION STATUS

- **Status**: ✅ RUNNING
- **URL**: http://localhost:5032
- **Environment**: Development
- **Database**: RentCollectionDB on RAVIWST\SQLEXPRESS

---

## 📋 WHAT WAS IMPLEMENTED

### Backend (Completed ✅)
1. **Entities**: Payment, CashPayment, BankTransfer
2. **Enums**: PaymentType, PaymentStatus, BankTransferStatus
3. **Services**: BankTransferService, CashPaymentService, FileStorageService
4. **API Controllers**: BankTransferApiController (Web project)
5. **Database**: Tables created and migration applied

### Frontend (Completed ✅)
1. **New Payment UI**: Visual invoice cards with payment method selection
2. **Bank Transfer Form**: File upload with all required fields
3. **Simplified Version**: MakePaymentSimple.cshtml for easier testing

---

## 🔐 TEST CREDENTIALS

| Tenant Name | Civil ID | Password | Pending Invoices |
|-------------|----------|----------|------------------|
| Ahmed Al-Rashid | 265123456789 | Welcome@123 | 1 |
| Sarah Al-Mutairi | 265234567890 | Welcome@123 | 2 |
| Mohammed Al-Sabah | 265345678901 | Welcome@123 | 2 |

---

## 🧪 TESTING STEPS

### Step 1: Login
1. Go to: **http://localhost:5032/TenantPortal/Login**
2. Enter Civil ID: `265234567890`
3. Enter Password: `Welcome@123`
4. Click "Login"

### Step 2: Access Payment Pages

You have **THREE** payment pages to test:

#### Option A: Original New UI (May have issues)
- URL: **http://localhost:5032/TenantPortal/MakePayment**
- Features: Full visual design with modals

#### Option B: Simplified Working Version (RECOMMENDED)
- URL: **http://localhost:5032/TenantPortal/MakePaymentSimple**
- Features: Simpler UI, better error messages, easier to debug

#### Option C: Raw Test Page
- URL: **http://localhost:5032/TenantPortal/TestBankTransfer**
- Features: Minimal form to test API directly

### Step 3: Submit Bank Transfer

1. Click "Pay Now" on any invoice
2. Fill in the form:
   - Transfer Date: (auto-filled with today)
   - Bank Name: Select "Gulf Bank"
   - Reference Number: "TEST123"
   - Upload File: Choose any small image (JPG/PNG) or PDF
   - Notes: "Test bank transfer"
3. Click "Submit Transfer"

### Step 4: Check Results

**If Successful:**
- Green success message appears
- Page reloads
- Invoice removed from list

**If Failed:**
- Red error message shows the problem
- Check browser console (F12) for details

---

## 🔍 TROUBLESHOOTING

### Problem: "Not showing anything"

**Possible Causes:**
1. Page is blank/white
2. No invoices are displayed
3. Form doesn't appear
4. Submission fails silently

**Solutions:**

#### Solution 1: Check Browser Console
```
1. Press F12
2. Go to "Console" tab
3. Look for red error messages
4. Take a screenshot and note the error
```

#### Solution 2: Check Network Tab
```
1. Press F12
2. Go to "Network" tab
3. Click on "MakePaymentSimple" request
4. Check the response - is it 200 OK or an error?
```

#### Solution 3: Use the Simplified Version
```
Go directly to: http://localhost:5032/TenantPortal/MakePaymentSimple

This version has built-in error display:
- Shows if ViewBag.Invoices is null
- Shows if no invoices exist
- Displays invoice count
- Shows detailed errors on submission
```

#### Solution 4: Test API Directly
```
1. Go to: http://localhost:5032/TenantPortal/TestBankTransfer
2. This is a bare-bones form that directly calls the API
3. It will show the exact JSON response or error
```

---

## 🐛 COMMON ERRORS & FIXES

### Error: "Unauthorized"
**Cause**: Not logged in or session expired
**Fix**: Login again at http://localhost:5032/TenantPortal/Login

### Error: "Failed to submit bank transfer"
**Cause**: API endpoint not working
**Fix**: Check application logs for the specific error

### Error: Page is blank
**Cause**: View compilation error or null reference
**Fix**: Use the simplified version at /TenantPortal/MakePaymentSimple

### Error: "No invoices data available"
**Cause**: Controller not passing ViewBag.Invoices
**Fix**: Check application logs, might be a database connection issue

### Error: File upload fails
**Cause**: File too large or uploads folder doesn't exist
**Fix**:
```
1. File must be < 5MB
2. Check if folder exists: C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\wwwroot\uploads
3. Create it if missing
```

---

## 📊 VERIFY DATABASE

After successful submission, check the database:

```sql
-- Check if bank transfer was created
SELECT TOP 5 * FROM BankTransfers ORDER BY BankTransferId DESC;

-- Check if payment was created
SELECT TOP 5 * FROM Payments WHERE PaymentType = 3 ORDER BY PaymentId DESC;

-- Check payment status
SELECT
    bt.BankTransferId,
    bt.Amount,
    bt.BankName,
    bt.Status,
    bt.SubmittedDate,
    p.PaymentStatus
FROM BankTransfers bt
INNER JOIN Payments p ON bt.PaymentId = p.PaymentId
ORDER BY bt.BankTransferId DESC;
```

---

## 🔗 ALL AVAILABLE URLS

### Tenant Portal
- Login: http://localhost:5032/TenantPortal/Login
- Dashboard: http://localhost:5032/TenantPortal/Dashboard
- Make Payment (Original): http://localhost:5032/TenantPortal/MakePayment
- **Make Payment (Simple)**: http://localhost:5032/TenantPortal/MakePaymentSimple ⭐
- **Test Bank Transfer**: http://localhost:5032/TenantPortal/TestBankTransfer ⭐
- Payment History: http://localhost:5032/TenantPortal/PaymentHistory

### API Endpoints
- POST /api/payments/bank/upload - Submit bank transfer
- GET /api/payments/bank/{id} - Get bank transfer details
- GET /api/payments/bank/tenant/current - Get my transfers
- GET /api/diagnostics/test - Test if API is working
- GET /api/diagnostics/session - Check current session
- GET /api/diagnostics/invoices - Check invoices for current tenant

### Diagnostics
- Test API: http://localhost:5032/api/diagnostics/test
- Check Session: http://localhost:5032/api/diagnostics/session
- Check Invoices: http://localhost:5032/api/diagnostics/invoices
- Swagger: http://localhost:5032/swagger (if enabled)

---

## 📝 WHAT TO PROVIDE FOR DEBUGGING

If still not working, please provide:

1. **Which URL did you try?**
   - http://localhost:5032/TenantPortal/MakePayment
   - http://localhost:5032/TenantPortal/MakePaymentSimple
   - http://localhost:5032/TenantPortal/TestBankTransfer

2. **What do you see?**
   - Blank white page
   - Error message (what does it say?)
   - Invoice cards but form doesn't work
   - Nothing at all

3. **Browser Console Errors** (F12 → Console tab)
   - Screenshot of any red errors

4. **Network Tab** (F12 → Network tab)
   - What is the status code of the page request? (200, 404, 500?)
   - Is there a request to /api/payments/bank/upload when you submit?
   - What is the response?

5. **Application Logs**
   - Any errors in the terminal where the app is running

---

## ✅ EXPECTED WORKING FLOW

### On MakePaymentSimple page:
```
1. Page loads showing: "Logged in as: Sarah Al-Mutairi (ID: 5)"
2. Shows: "Your Pending Invoices (2)"
3. Displays 2 invoice cards with:
   - Invoice #4, Amount: 380.000 KWD
   - Invoice #5, Amount: 420.000 KWD
4. Each card has a "Pay Now" button
```

### When clicking "Pay Now":
```
1. Modal popup appears with form
2. Amount is pre-filled
3. Transfer date defaults to today
4. Can select bank from dropdown
5. Can upload file (shows file name after selection)
6. "Submit Transfer" button is enabled
```

### When clicking "Submit Transfer":
```
1. Button changes to "Submitting..."
2. AJAX request sent to /api/payments/bank/upload
3. Either:
   SUCCESS: Green message "✓ Bank transfer submitted successfully!"
   ERROR: Red message with specific error details
```

---

## 🎯 RECOMMENDED NEXT STEP

**Please try this exact sequence:**

1. Open a **NEW Incognito/Private window** (Ctrl+Shift+N)
2. Go to: http://localhost:5032/TenantPortal/Login
3. Login with: 265234567890 / Welcome@123
4. Go to: **http://localhost:5032/TenantPortal/MakePaymentSimple**
5. Take a screenshot of what you see
6. If you see invoices, click "Pay Now"
7. Fill the form and click "Submit Transfer"
8. Open F12 Console tab and screenshot any errors

**Send me:**
- Screenshot of the page
- Screenshot of any console errors
- Tell me at which step it fails

This will help me identify the exact issue!
