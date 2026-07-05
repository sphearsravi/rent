# Rent Collection System - Setup Instructions

## Prerequisites
- .NET 8.0 SDK installed
- SQL Server 2019 or later (Express, Developer, or Standard)
- SQL Server Management Studio (SSMS) - Optional but recommended

## Step-by-Step Setup

### Step 1: Run the Database Script

**Option A: Using SQL Server Management Studio (SSMS)**
1. Open SQL Server Management Studio
2. Connect to your SQL Server instance (usually `localhost` or `.`)
3. Click **File → Open → File**
4. Navigate to: `C:\Users\r.shankar\RentCollectionSystem\Database\CreateDatabase_SQLServer.sql`
5. Click **Execute** button (or press F5)
6. Wait for completion message

**Option B: Using Command Line (sqlcmd)**
```bash
sqlcmd -S localhost -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\CreateDatabase_SQLServer.sql"
```

### Step 2: Verify Database Creation

**Option A: Run Quick Test Script**
```bash
sqlcmd -S localhost -E -i "C:\Users\r.shankar\RentCollectionSystem\Database\QuickTest.sql"
```

**Option B: In SSMS, run:**
```sql
USE RentCollectionDB;
GO

SELECT
    (SELECT COUNT(*) FROM Companies) AS Companies,
    (SELECT COUNT(*) FROM AspNetUsers) AS Users,
    (SELECT COUNT(*) FROM Properties) AS Properties,
    (SELECT COUNT(*) FROM Units) AS Units,
    (SELECT COUNT(*) FROM Tenants) AS Tenants,
    (SELECT COUNT(*) FROM Leases) AS Leases,
    (SELECT COUNT(*) FROM Payments) AS Payments;
```

You should see:
- 1 Company
- 1 User
- 2 Properties
- 6 Units
- 3 Tenants
- 3 Leases
- 4 Payments

### Step 3: Update Connection String (If Needed)

If your SQL Server is not on `localhost` or doesn't use Windows Authentication:

Edit `RentCollection.Web\appsettings.json`:

**For SQL Server Authentication:**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_SERVER;Database=RentCollectionDB;User Id=sa;Password=YourPassword;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

**For Windows Authentication (Default):**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=RentCollectionDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

**For Named Instance:**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=RentCollectionDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

### Step 4: Run the Application

Open Command Prompt or Terminal:

```bash
cd C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web
dotnet run
```

You should see output like:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

### Step 5: Access the Application

Open your web browser and navigate to:
- **HTTPS:** https://localhost:5001
- **HTTP:** http://localhost:5000

You should see the beautiful dashboard!

### Step 6: Login

Use these default credentials:
- **Email:** `admin@rentcollection.com`
- **Password:** `Admin@123`

**IMPORTANT:** Change this password immediately after first login!

## What You Should See

After login, you'll see the dashboard with:
- **2 Properties** (Sunset Apartments, Green Valley Homes)
- **6 Units** (4 at Sunset, 2 at Green Valley)
- **3 Active Tenants**
- **4 Recent Payments** totaling $8,000
- **2 Pending Invoices**
- **Occupancy Rate:** 66.7% (4 out of 6 units occupied)

## Troubleshooting

### Issue: "Cannot connect to database"
**Solution:** Check your SQL Server is running:
```bash
# Check SQL Server service
services.msc
# Look for "SQL Server (MSSQLSERVER)" or "SQL Server (SQLEXPRESS)"
```

### Issue: "Login failed for user"
**Solution:**
1. Make sure Windows Authentication is enabled in SQL Server
2. Or update connection string with SQL Server credentials

### Issue: "Database does not exist"
**Solution:** Re-run the CreateDatabase_SQLServer.sql script

### Issue: "Application shows empty dashboard"
**Solution:**
1. Verify database has sample data
2. Check connection string
3. Look at console output for errors

### Issue: "Build errors"
**Solution:**
```bash
dotnet clean
dotnet restore
dotnet build
```

## Running in Visual Studio

1. Open `RentCollectionSystem.sln` in Visual Studio 2022
2. Right-click on `RentCollection.Web` project
3. Select **Set as Startup Project**
4. Press **F5** or click **Start** button
5. Browser will open automatically

## Database Schema

The database includes:
- **Companies** - Multi-tenant support
- **AspNetUsers** - Identity users with custom fields
- **AspNetRoles** - 6 roles (SuperAdmin, CompanyOwner, PropertyManager, Accountant, MaintenanceStaff, Tenant)
- **Properties** - Property information
- **Units** - Individual rental units
- **Tenants** - Tenant profiles
- **Leases** - Lease agreements
- **Payments** - Payment transactions
- **Invoices** - Billing
- **MaintenanceRequests** - Work orders

## Sample Data Included

**Sample Company:**
- Name: Sample Property Management
- Email: info@samplepm.com

**Sample Properties:**
1. Sunset Apartments - 24 units, Los Angeles
2. Green Valley Homes - 12 units, San Diego

**Sample Tenants:**
1. John Doe - Unit 101, Sunset Apartments
2. Jane Smith - Unit 103, Sunset Apartments
3. Michael Johnson - Unit A1, Green Valley Homes

## Next Steps

1. ✅ Login with admin credentials
2. ✅ Explore the dashboard
3. ✅ View Properties page
4. ✅ View Tenants page
5. ✅ Check Payments and Invoices
6. 🔧 Change admin password
7. 🔧 Add your own properties and tenants
8. 🔧 Customize as needed

## Support

For issues or questions:
1. Check the README.md file
2. Review the system analysis document
3. Check application console output for errors
4. Verify SQL Server connection

---

**Version:** 1.0
**Last Updated:** November 2, 2025
**Database:** SQL Server 2019+
