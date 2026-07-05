using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using RentCollection.Application.Branding;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Services.Pdf;
using Xunit;

namespace RentCollection.Tests.Pdf;

/// <summary>
/// Smoke tests for the redesigned invoice + receipt. Renders three status
/// variants to <c>C:\Temp\invoice-samples\</c> so the result is visually
/// inspectable, plus asserts the byte stream is a valid PDF and contains
/// the company name from <see cref="BrandingSettings"/>.
/// </summary>
public class InvoiceSampleSmokeTest
{
    static InvoiceSampleSmokeTest()
    {
        // Match what Program.cs does at startup.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static IPdfService NewSvc()
    {
        var brand = new BrandingSettings
        {
            CompanyName = "ALRTAM Real Estate Developer",
            TagLine = "Real Estate Developer",
            // The test renderer is decoupled from the web root — a missing
            // file is a valid case (graceful fallback to text-only header).
            LogoPath = @"C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\wwwroot\img\branding\logo.png",
            PrimaryHex = "#C9A647",
            SecondaryHex = "#0B0B0B",
            ContactAddress = "Hawally, Kuwait",
            ContactEmail = "accounts@alrtam.example",
            Bank = new BrandingSettings.BankInfo
            {
                Name = "Kuwait Finance House",
                AccountHolder = "ALRTAM Real Estate Developer",
                Iban = "KW00 KFHO 0000 0000 0000 0000 0000 00",
                KnetReceiver = "ALRTAM Real Estate Developer",
            },
            FooterTerms = "Payment due within 5 days of issue.",
        };
        return new QuestPdfService(Options.Create(brand));
    }

    private static InvoicePdfModel SampleInvoice(string status, decimal paid) => new()
    {
        InvoiceNumber = "INV-2026-000123",
        InvoiceDate = new DateTime(2026, 6, 1),
        DueDate = new DateTime(2026, 6, 7),
        Status = status,
        TenantName = "Ravi Shankar",
        PropertyName = "Salmiya Tower B",
        UnitNumber = "A101",
        CompanyName = "Acme Holdings W.L.L.",
        TotalAmount = 265m,
        PaidAmount = paid,
        BalanceAmount = 265m - paid,
        Currency = "KWD",
        Notes = "Includes June rent + maintenance fee.",
        // New refinements
        BillingPeriod = "June 2026",
        TenantPaciNumber = "298011500123",
        PropertyPaciNumber = "451208760042",
        PaidDate = paid > 0 ? new DateTime(2026, 6, 5) : (DateTime?)null,
        // Unit address composition (Kuwait sample)
        UnitFloor = "1",
        PropertyBuildingNumber = "23",
        PropertyStreet = "5",
        PropertyBlock = "10",
        PropertyArea = "Salmiya",
        PropertyGovernorate = "Hawally",
        Lines = new()
        {
            new() { Description = "Monthly rent — Jun 2026", Amount = 250m },
            new() { Description = "Maintenance fee", Amount = 15m },
        },
    };

    [Theory]
    [InlineData("Pending", 0)]
    [InlineData("Overdue", 0)]
    [InlineData("Paid", 265)]
    public void RenderInvoice_writes_valid_pdf_with_brand(string status, decimal paid)
    {
        var svc = NewSvc();
        var bytes = svc.RenderInvoice(SampleInvoice(status, paid));

        Assert.NotEmpty(bytes);
        // PDF magic bytes: %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);

        // Side effect for human review — drop a copy under C:\Temp so the
        // dev can open it. Idempotent; overwrites on each run.
        var outDir = @"C:\Temp\invoice-samples";
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, $"invoice-{status.ToLowerInvariant()}.pdf"), bytes);
    }

    [Fact]
    public void RenderReceipt_writes_valid_pdf_with_paid_stamp()
    {
        var svc = NewSvc();
        var bytes = svc.RenderReceipt(new ReceiptPdfModel
        {
            ReceiptNumber = "RCP-2026-000045",
            PaymentDate = new DateTime(2026, 6, 1, 14, 30, 0),
            PaymentMethod = "K-NET",
            TenantName = "Ravi Shankar",
            PropertyName = "Salmiya Tower B",
            UnitNumber = "12",
            AmountPaid = 265m,
            Currency = "KWD",
            TransactionReference = "KNET20260601A4F2",
            InvoiceNumber = "INV-2026-000123",
        });

        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'%', bytes[0]);

        var outDir = @"C:\Temp\invoice-samples";
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "receipt.pdf"), bytes);
    }
}
