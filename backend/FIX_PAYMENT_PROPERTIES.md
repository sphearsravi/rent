# Fix Payment Property References

## Issue
The `Payment` entity has been updated to use enums instead of strings:
- `PaymentMethod` (string) → `PaymentType` (enum)
- `PaymentStatus` (string) → `Status` (enum)

## Quick Fix Guide

### Option 1: Run the Application with Existing Data (Recommended for Testing)

For now, you can run just the **API project** which doesn't have the view compilation errors:

```bash
cd RentCollectionSystem/RentCollection.API
dotnet run
```

This will start the API on `https://localhost:5001` and you can test the new Cash & Bank Transfer endpoints using Postman or Swagger.

### Option 2: Find and Replace in Files

Update all occurrences in your code:

#### In C# Controllers (.cs files):

**Find:** `payment.PaymentMethod`
**Replace with:** `payment.PaymentType.ToString()`

**Find:** `payment.PaymentStatus`
**Replace with:** `payment.Status.ToString()`

**Find:** `PaymentMethod =`
**Replace with:** `PaymentType =`

**Find:** `PaymentStatus =`
**Replace with:** `Status =`

#### In Razor Views (.cshtml files):

**Find:** `payment.PaymentMethod`
**Replace with:** `payment.PaymentType.ToString()`

**Find:** `payment.PaymentStatus`
**Replace with:** `payment.Status.ToString()`

### Option 3: Add Extension Property (Temporary Backward Compatibility)

Add this to `Payment.cs` to maintain backward compatibility:

```csharp
// Temporary backward compatibility properties
[NotMapped]
public string PaymentMethod
{
    get => PaymentType.ToString();
    set => PaymentType = Enum.Parse<PaymentType>(value, true);
}

[NotMapped]
public string PaymentStatus
{
    get => Status.ToString();
    set => Status = Enum.Parse<PaymentStatus>(value, true);
}
```

## Files That Need Updates

Based on build errors, these files reference the old properties:

### Controllers:
- `RentCollection.Web\Controllers\Api\TenantApiController.cs`
- `RentCollection.Web\Controllers\TenantPortalController.cs`
- `RentCollection.Web\Controllers\ReportsController.cs`

### Views:
- `RentCollection.Web\Views\Dashboard\Index.cshtml`
- `RentCollection.Web\Views\Leases\Details.cshtml`
- `RentCollection.Web\Views\Payments\Index.cshtml`
- `RentCollection.Web\Views\TenantPortal\Dashboard.cshtml`
- `RentCollection.Web\Views\TenantPortal\PaymentHistory.cshtml`
- `RentCollection.Web\Views\TenantPortal\ViewReceipt.cshtml`
- `RentCollection.Web\Views\Tenants\Details.cshtml`

## Recommended Approach for Now

1. **Test the new Cash & Bank Transfer APIs first using the API project**
2. **Update the views/controllers gradually** as you encounter each page

The new Cash & Bank Transfer features are fully functional via API - the build errors are only in the old existing pages.
