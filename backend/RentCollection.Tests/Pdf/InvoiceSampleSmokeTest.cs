using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using RentCollection.Application.Branding;
using RentCollection.Application.Interfaces;
using RentCollection.Infrastructure.Services.Pdf;
using Xunit;

namespace RentCollection.Tests.Pdf;

/// <summary>
/// Smoke tests for the invoice + receipt renderer. Each variant is written to
/// <c>C:\Temp\invoice-samples\</c> as a PDF AND a PNG preview (one per page,
/// rasterised from the same QuestPDF document) so the design can be reviewed
/// visually without a PDF viewer. Asserts a valid PDF byte stream.
/// </summary>
public class InvoiceSampleSmokeTest
{
    private const string OutDir = @"C:\Temp\invoice-samples";

    static InvoiceSampleSmokeTest()
    {
        // Match what Program.cs does at startup.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static QuestPdfService NewSvc()
    {
        var brand = new BrandingSettings
        {
            CompanyName = "ALRTAM Company",
            TagLine = "Real Estate Developer",
            // The test renderer is decoupled from the web root — a missing
            // file is a valid case (graceful fallback to text-only header).
            LogoPath = @"C:\Users\r.shankar\RentCollectionSystem\RentCollection.Web\wwwroot\img\branding\logo.png",
            PrimaryHex = "#C9A647",
            SecondaryHex = "#0B0B0B",
            ContactAddress = "Hawally, Kuwait",
            ContactPhone = "+965 2222 3333",
            ContactEmail = "accounts@alrtam.co",
            Website = "www.alrtam.co",
            Bank = new BrandingSettings.BankInfo
            {
                Name = "Kuwait Finance House",
                AccountHolder = "ALRTAM Company",
                Iban = "KW00 KFHO 0000 0000 0000 0000 0000 00",
                KnetReceiver = "ALRTAM Company",
            },
            FooterTerms = "Payment due within 5 days of issue. Please include the invoice number with every transfer.",
            FooterTermsAr = "يُسدد المبلغ خلال 5 أيام من تاريخ الإصدار. يرجى ذكر رقم الفاتورة مع كل تحويل.",
        };
        return new QuestPdfService(Options.Create(brand));
    }

    private static InvoicePdfModel SampleInvoice(string status, decimal paid, string culture = "en") => new()
    {
        InvoiceNumber = "INV-000123",
        InvoiceDate = new DateTime(2026, 6, 1),
        DueDate = new DateTime(2026, 6, 7),
        GeneratedAt = new DateTime(2026, 6, 12, 9, 30, 0),
        Status = status,
        TenantName = "Ravi Shankar",
        TenantPhone = "+965 9900 1122",
        TenantEmail = "ravi@example.com",
        PropertyName = "Salmiya Tower B",
        UnitNumber = "A101",
        CompanyName = "Acme Holdings W.L.L.",
        TotalAmount = 265m,
        PaidAmount = paid,
        BalanceAmount = 265m - paid,
        Currency = "KWD",
        Culture = culture,
        Notes = "Includes June rent + maintenance fee.",
        BillingPeriod = culture == "ar" ? "يونيو 2026" : "June 2026",
        TenantPaciNumber = "298011500123",
        PropertyPaciNumber = "451208760042",
        PaidDate = paid > 0 ? new DateTime(2026, 6, 5) : (DateTime?)null,
        LeaseStartDate = new DateTime(2026, 1, 1),
        LeaseEndDate = new DateTime(2026, 12, 31),
        UnitFloor = "1",
        PropertyBuildingNumber = "23",
        PropertyStreet = "5",
        PropertyBlock = "10",
        PropertyArea = culture == "ar" ? "السالمية" : "Salmiya",
        PropertyGovernorate = culture == "ar" ? "حولي" : "Hawally",
        Lines = culture == "ar"
            ? new() { new() { Description = "الإيجار الشهري — يونيو 2026", Amount = 250m }, new() { Description = "رسوم صيانة", Amount = 15m } }
            : new() { new() { Description = "Monthly rent — June 2026", Amount = 250m }, new() { Description = "Maintenance fee", Amount = 15m } },
        Payments = paid > 0
            ? new List<InvoicePdfPayment>
            {
                new(new DateTime(2026, 6, 3), "K-NET", "KNET20260603A4F2", Math.Min(paid, 100m)),
                new(new DateTime(2026, 6, 5), culture == "ar" ? "نقداً" : "Cash", "CASH-2026-000045", paid - Math.Min(paid, 100m)),
            }.Where(p => p.Amount > 0).ToList()
            : new List<InvoicePdfPayment>(),
    };

    private static void AssertPdf(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        // PDF magic bytes: %PDF-
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    private static void WriteSamples(string name, byte[] pdf, IReadOnlyList<byte[]> pngPages)
    {
        // Side effect for human review — idempotent; overwrites on each run.
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes(Path.Combine(OutDir, $"{name}.pdf"), pdf);
        for (var i = 0; i < pngPages.Count; i++)
            File.WriteAllBytes(Path.Combine(OutDir, $"{name}-p{i + 1}.png"), pngPages[i]);
    }

    [Theory]
    [InlineData("Pending", 0, "en")]
    [InlineData("Overdue", 0, "en")]
    [InlineData("Partially Paid", 100, "en")]
    [InlineData("Paid", 265, "en")]
    [InlineData("Pending Verification", 0, "en")]
    [InlineData("Pending", 0, "ar")]
    [InlineData("Paid", 265, "ar")]
    public void RenderInvoice_writes_valid_pdf_with_brand(string status, decimal paid, string culture)
    {
        var svc = NewSvc();
        var model = SampleInvoice(status, paid, culture);

        var bytes = svc.RenderInvoice(model);
        AssertPdf(bytes);

        var pages = svc.RenderInvoicePreviewPng(model);
        Assert.Single(pages); // a one-invoice document must fit on one A4 page

        WriteSamples($"invoice-{culture}-{status.ToLowerInvariant().Replace(' ', '-')}", bytes, pages);
    }

    [Fact]
    public void RenderInvoice_many_lines_paginates_with_repeated_header()
    {
        var svc = NewSvc();
        var model = SampleInvoice("Pending", 0);
        model.Lines = Enumerable.Range(1, 45)
            .Select(i => new InvoicePdfLine { Description = $"Line item {i}", Amount = 5m })
            .ToList();
        model.TotalAmount = 225m;
        model.BalanceAmount = 225m;

        var pages = svc.RenderInvoicePreviewPng(model);
        Assert.True(pages.Count >= 2, "45 lines must spill onto a second page");
        WriteSamples("invoice-en-multipage", svc.RenderInvoice(model), pages);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void RenderReceipt_writes_valid_pdf_with_paid_stamp(string culture)
    {
        var svc = NewSvc();
        var model = new ReceiptPdfModel
        {
            ReceiptNumber = "RCPT-00000045",
            PaymentDate = new DateTime(2026, 6, 1, 14, 30, 0),
            GeneratedAt = new DateTime(2026, 6, 1, 14, 31, 0),
            PaymentMethod = "KNET",
            TenantName = "Ravi Shankar",
            PropertyName = "Salmiya Tower B",
            UnitNumber = "12",
            AmountPaid = 265m,
            Currency = "KWD",
            Culture = culture,
            TransactionReference = "KNET20260601A4F2",
            InvoiceNumber = "INV-000123",
            InvoiceBalanceAfter = 0m,
        };

        var bytes = svc.RenderReceipt(model);
        AssertPdf(bytes);

        var pages = svc.RenderReceiptPreviewPng(model);
        Assert.Single(pages);
        WriteSamples($"receipt-{culture}", bytes, pages);
    }
}
