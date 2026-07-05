using Microsoft.EntityFrameworkCore;
using RentCollection.Core.Entities;
using RentCollection.Core.Enums;
using RentCollection.Infrastructure.Data;

namespace RentCollection.Tests.Helpers;

/// <summary>
/// Helper class to set up in-memory database with test data
/// </summary>
public static class TestDatabaseHelper
{
    public static ApplicationDbContext CreateInMemoryContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Variant for tests that exercise services using
    /// <c>DbContext.Database.BeginTransactionAsync()</c> against InMemory.
    /// The InMemory provider can't actually transact, but it raises a
    /// throw-by-default warning. We downgrade that to no-op for tests where
    /// the transaction is part of the production code path — the assertion
    /// boundary is the data shape after commit, not transaction isolation
    /// (which only matters in SQL Server tests).
    /// </summary>
    public static ApplicationDbContext CreateInMemoryContextWithTransactionsIgnored(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static async Task SeedTestDataAsync(ApplicationDbContext context)
    {
        // Create company
        var company = new Company
        {
            CompanyId = 1,
            CompanyName = "Test Property Management",
            Email = "admin@testpm.com",
            Phone = "12345678",
            Address = "Test Address",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };
        context.Companies.Add(company);

        // Create Building 1 - Al Salmiya Tower (with KNET)
        var building1 = new Property
        {
            PropertyId = 1,
            CompanyId = 1,
            PropertyName = "Al Salmiya Tower",
            PropertyType = "Residential",
            Address = "Block 5, Street 10, Salmiya",
            Governorate = "Hawally",
            Area = "Salmiya",
            Block = "5",
            PACINumber = "PACI001",
            Status = "Active",
            AcceptKNET = true,
            KNETAccountID = "KNET_ACC_001",
            KNETMID = "MID_SALMIYA_001",
            KnetTranportalId = "test_tranportal_salmiya",
            KnetTranportalPwd = "test_pwd_salmiya",
            KnetTerminalResourceKey = "test_key_salmiya_0001",
            KNETBankName = "National Bank of Kuwait",
            KNETBankAccountNumber = "KW001234567890",
            CreatedDate = DateTime.UtcNow
        };
        context.Properties.Add(building1);

        // Create Building 2 - Hawally Plaza (with KNET)
        var building2 = new Property
        {
            PropertyId = 2,
            CompanyId = 1,
            PropertyName = "Hawally Plaza",
            PropertyType = "Commercial",
            Address = "Block 3, Street 5, Hawally",
            Governorate = "Hawally",
            Area = "Hawally",
            Block = "3",
            PACINumber = "PACI002",
            Status = "Active",
            AcceptKNET = true,
            KNETAccountID = "KNET_ACC_002",
            KNETMID = "MID_HAWALLY_002",
            KnetTranportalId = "test_tranportal_hawally",
            KnetTranportalPwd = "test_pwd_hawally",
            KnetTerminalResourceKey = "test_key_hawally_0002",
            KNETBankName = "Gulf Bank",
            KNETBankAccountNumber = "KW009876543210",
            CreatedDate = DateTime.UtcNow
        };
        context.Properties.Add(building2);

        // Create Building 3 - Farwaniya Center (no KNET)
        var building3 = new Property
        {
            PropertyId = 3,
            CompanyId = 1,
            PropertyName = "Farwaniya Center",
            PropertyType = "Mixed",
            Address = "Block 1, Street 2, Farwaniya",
            Governorate = "Farwaniya",
            Area = "Farwaniya",
            Block = "1",
            PACINumber = "PACI003",
            Status = "Active",
            AcceptKNET = false,
            CreatedDate = DateTime.UtcNow
        };
        context.Properties.Add(building3);

        // Create Units for Building 1
        var unit1B1 = new Unit
        {
            UnitId = 1,
            PropertyId = 1,
            UnitNumber = "101",
            UnitType = "Apartment",
            Bedrooms = 2,
            Bathrooms = 2,
            SquareFeet = 120,
            MonthlyRent = 450,
            Status = "Occupied",
            CreatedDate = DateTime.UtcNow
        };
        context.Units.Add(unit1B1);

        var unit2B1 = new Unit
        {
            UnitId = 2,
            PropertyId = 1,
            UnitNumber = "102",
            UnitType = "Apartment",
            Bedrooms = 3,
            Bathrooms = 2,
            SquareFeet = 150,
            MonthlyRent = 550,
            Status = "Occupied",
            CreatedDate = DateTime.UtcNow
        };
        context.Units.Add(unit2B1);

        // Create Units for Building 2
        var unit1B2 = new Unit
        {
            UnitId = 3,
            PropertyId = 2,
            UnitNumber = "G01",
            UnitType = "Shop",
            SquareFeet = 80,
            MonthlyRent = 800,
            Status = "Occupied",
            CreatedDate = DateTime.UtcNow
        };
        context.Units.Add(unit1B2);

        // Create Units for Building 3
        var unit1B3 = new Unit
        {
            UnitId = 4,
            PropertyId = 3,
            UnitNumber = "201",
            UnitType = "Office",
            SquareFeet = 100,
            MonthlyRent = 350,
            Status = "Occupied",
            CreatedDate = DateTime.UtcNow
        };
        context.Units.Add(unit1B3);

        // Create Tenant 1 - Ahmed (has 3 leases in different buildings)
        var tenant1 = new Tenant
        {
            TenantId = 1,
            CompanyId = 1,
            FirstName = "Ahmed",
            LastName = "Al-Rashid",
            Email = "ahmed@email.com",
            Phone = "99001122",
            GovernmentId = "284010100001", // Civil ID
            Password = BCrypt.Net.BCrypt.HashPassword("Test@123"),
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Tenants.Add(tenant1);

        // Create Tenant 2 - Fatima (has 1 lease)
        var tenant2 = new Tenant
        {
            TenantId = 2,
            CompanyId = 1,
            FirstName = "Fatima",
            LastName = "Al-Kuwaiti",
            Email = "fatima@email.com",
            Phone = "99003344",
            GovernmentId = "284020200002", // Civil ID
            Password = BCrypt.Net.BCrypt.HashPassword("Test@456"),
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Tenants.Add(tenant2);

        // Create Tenant 3 - Mohammed (inactive)
        var tenant3 = new Tenant
        {
            TenantId = 3,
            CompanyId = 1,
            FirstName = "Mohammed",
            LastName = "Al-Sabah",
            Email = "mohammed@email.com",
            Phone = "99005566",
            GovernmentId = "284030300003", // Civil ID
            Password = BCrypt.Net.BCrypt.HashPassword("Test@789"),
            Status = "Inactive",
            CreatedDate = DateTime.UtcNow
        };
        context.Tenants.Add(tenant3);

        // Create Lease 1 - Ahmed in Building 1 (Salmiya)
        var lease1 = new Lease
        {
            LeaseId = 1,
            UnitId = 1,
            TenantId = 1,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-6),
            LeaseEndDate = DateTime.UtcNow.AddMonths(6),
            MonthlyRent = 450,
            SecurityDeposit = 450,
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Leases.Add(lease1);

        // Create Lease 2 - Ahmed in Building 2 (Hawally)
        var lease2 = new Lease
        {
            LeaseId = 2,
            UnitId = 3,
            TenantId = 1,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-3),
            LeaseEndDate = DateTime.UtcNow.AddMonths(9),
            MonthlyRent = 800,
            SecurityDeposit = 800,
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Leases.Add(lease2);

        // Create Lease 3 - Fatima in Building 1 (Salmiya)
        var lease3 = new Lease
        {
            LeaseId = 3,
            UnitId = 2,
            TenantId = 2,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-2),
            LeaseEndDate = DateTime.UtcNow.AddMonths(10),
            MonthlyRent = 550,
            SecurityDeposit = 550,
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Leases.Add(lease3);

        // Create Lease 4 - Ahmed in Building 3 (Farwaniya - no KNET)
        var lease4 = new Lease
        {
            LeaseId = 4,
            UnitId = 4,
            TenantId = 1,
            LeaseStartDate = DateTime.UtcNow.AddMonths(-1),
            LeaseEndDate = DateTime.UtcNow.AddMonths(11),
            MonthlyRent = 350,
            SecurityDeposit = 350,
            Status = "Active",
            CreatedDate = DateTime.UtcNow
        };
        context.Leases.Add(lease4);

        // Create Invoices for Lease 1 (Ahmed - Salmiya)
        var invoice1 = new Invoice
        {
            InvoiceId = 1,
            LeaseId = 1,
            TenantId = 1,
            InvoiceDate = DateTime.UtcNow.AddMonths(-1),
            DueDate = DateTime.UtcNow.AddDays(-15),
            TotalAmount = 450,
            PaidAmount = 0,
            Status = "Overdue",
            InvoiceType = "Rent",
            CreatedDate = DateTime.UtcNow
        };
        context.Invoices.Add(invoice1);

        var invoice2 = new Invoice
        {
            InvoiceId = 2,
            LeaseId = 1,
            TenantId = 1,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(15),
            TotalAmount = 450,
            PaidAmount = 0,
            Status = "Pending",
            InvoiceType = "Rent",
            CreatedDate = DateTime.UtcNow
        };
        context.Invoices.Add(invoice2);

        // Create Invoices for Lease 2 (Ahmed - Hawally)
        var invoice3 = new Invoice
        {
            InvoiceId = 3,
            LeaseId = 2,
            TenantId = 1,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(10),
            TotalAmount = 800,
            PaidAmount = 0,
            Status = "Pending",
            InvoiceType = "Rent",
            CreatedDate = DateTime.UtcNow
        };
        context.Invoices.Add(invoice3);

        // Create a completed payment (no mismatch)
        var payment1 = new Payment
        {
            PaymentId = 1,
            LeaseId = 1,
            TenantId = 1,
            InvoiceId = null,
            PaymentDate = DateTime.UtcNow.AddMonths(-2),
            AmountPaid = 450,
            PaymentType = PaymentType.KNET,
            Status = PaymentStatus.Completed,
            CivilID = "284010100001",
            PropertyId = 1,
            KNETAccountIDUsed = "KNET_ACC_001",
            KNETMIDUsed = "MID_SALMIYA_001",
            CheckoutPaymentId = "pay_test_001",
            TransactionReference = "TRX001",
            ReceiptNumber = "RCPT-00000001",
            HasMismatch = false,
            CreatedDate = DateTime.UtcNow.AddMonths(-2)
        };
        context.Payments.Add(payment1);

        // Create a payment with Civil ID mismatch
        var payment2 = new Payment
        {
            PaymentId = 2,
            LeaseId = 1,
            TenantId = 1,
            InvoiceId = null,
            PaymentDate = DateTime.UtcNow.AddMonths(-1),
            AmountPaid = 450,
            PaymentType = PaymentType.KNET,
            Status = PaymentStatus.Completed,
            CivilID = "999999999999", // Different Civil ID - mismatch!
            PropertyId = 1,
            KNETAccountIDUsed = "KNET_ACC_001",
            KNETMIDUsed = "MID_SALMIYA_001",
            CheckoutPaymentId = "pay_test_002",
            TransactionReference = "TRX002",
            ReceiptNumber = "RCPT-00000002",
            HasMismatch = true,
            MismatchDetails = "Civil ID mismatch: Expected 284010100001, Got 999999999999",
            CreatedDate = DateTime.UtcNow.AddMonths(-1)
        };
        context.Payments.Add(payment2);

        // Create a payment with KNETMID mismatch
        var payment3 = new Payment
        {
            PaymentId = 3,
            LeaseId = 2,
            TenantId = 1,
            InvoiceId = null,
            PaymentDate = DateTime.UtcNow.AddDays(-5),
            AmountPaid = 800,
            PaymentType = PaymentType.KNET,
            Status = PaymentStatus.Completed,
            CivilID = "284010100001",
            PropertyId = 2,
            KNETAccountIDUsed = "KNET_ACC_001", // Wrong KNET account!
            KNETMIDUsed = "MID_SALMIYA_001",    // Wrong MID!
            CheckoutPaymentId = "pay_test_003",
            TransactionReference = "TRX003",
            ReceiptNumber = "RCPT-00000003",
            HasMismatch = true,
            MismatchDetails = "KNETMID mismatch: Expected MID_HAWALLY_002, Got MID_SALMIYA_001",
            CreatedDate = DateTime.UtcNow.AddDays(-5)
        };
        context.Payments.Add(payment3);

        // Create Payment Audit records
        var audit1 = new PaymentAudit
        {
            PaymentAuditId = 1,
            PaymentId = 2,
            AuditType = "CivilIDMismatch",
            AuditTimestamp = DateTime.UtcNow.AddMonths(-1),
            Amount = 450,
            IsMismatch = true,
            MismatchType = "CivilID",
            ExpectedCivilID = "284010100001",
            ActualCivilID = "999999999999",
            MismatchDetails = "Civil ID used for payment does not match tenant's registered Civil ID",
            IsResolved = false
        };
        context.PaymentAudits.Add(audit1);

        var audit2 = new PaymentAudit
        {
            PaymentAuditId = 2,
            PaymentId = 3,
            AuditType = "KNETMIDMismatch",
            AuditTimestamp = DateTime.UtcNow.AddDays(-5),
            Amount = 800,
            IsMismatch = true,
            MismatchType = "KNETMID",
            ExpectedKNETMID = "MID_HAWALLY_002",
            ActualKNETMID = "MID_SALMIYA_001",
            ExpectedBuildingId = 2,
            ActualBuildingId = 1,
            MismatchDetails = "Payment routed to wrong KNET merchant account",
            IsResolved = false
        };
        context.PaymentAudits.Add(audit2);

        await context.SaveChangesAsync();
    }

    public static async Task SeedAdditionalPaymentsAsync(ApplicationDbContext context)
    {
        // Add more payments for reconciliation testing
        for (int i = 4; i <= 10; i++)
        {
            var payment = new Payment
            {
                PaymentId = i,
                LeaseId = (i % 3) + 1,
                TenantId = (i % 2) + 1,
                PaymentDate = DateTime.UtcNow.AddDays(-i * 2),
                AmountPaid = 450 + (i * 50),
                PaymentType = PaymentType.KNET,
                Status = i % 4 == 0 ? PaymentStatus.Pending : PaymentStatus.Completed,
                CivilID = (i % 2) + 1 == 1 ? "284010100001" : "284020200002",
                PropertyId = (i % 2) + 1,
                KNETAccountIDUsed = $"KNET_ACC_00{(i % 2) + 1}",
                KNETMIDUsed = i % 2 == 0 ? "MID_SALMIYA_001" : "MID_HAWALLY_002",
                CheckoutPaymentId = $"pay_test_00{i}",
                TransactionReference = $"TRX00{i}",
                ReceiptNumber = $"RCPT-{i:D8}",
                HasMismatch = false,
                CreatedDate = DateTime.UtcNow.AddDays(-i * 2)
            };
            context.Payments.Add(payment);
        }

        await context.SaveChangesAsync();
    }
}
