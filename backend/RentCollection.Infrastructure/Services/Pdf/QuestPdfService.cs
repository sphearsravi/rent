using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RentCollection.Application.Branding;
using RentCollection.Application.Interfaces;

namespace RentCollection.Infrastructure.Services.Pdf;

/// <summary>
/// QuestPDF-based invoice/receipt/statement/dashboard renderer. Licensed
/// Community in Program.cs (free under $1M USD revenue).
///
/// Invoice and receipt headers + footers are branded from
/// <see cref="BrandingSettings"/> (logo, colours, contact info, bank, terms).
/// Layouts respond to <c>InvoicePdfModel.Status</c> with status-aware
/// overlays (PAID stamp, OVERDUE bar, PENDING tag).
/// </summary>
public class QuestPdfService : IPdfService
{
    private readonly BrandingSettings _brand;

    public QuestPdfService(IOptions<BrandingSettings> brand)
    {
        _brand = brand.Value;
    }

    // Parameterless ctor for the legacy DI registration path. Falls back to
    // a default brand so PDF generation never breaks if branding is unset.
    public QuestPdfService() : this(Options.Create(new BrandingSettings())) { }

    public byte[] RenderInvoice(InvoicePdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor(_brand.SecondaryHex));

            // OVERDUE red bar across the very top — only on this status so
            // the visual signal is unmistakable from across a desk.
            if (string.Equals(m.Status, "Overdue", StringComparison.OrdinalIgnoreCase))
            {
                page.Background().Column(c =>
                {
                    c.Item().Height(6).Background("#dc2626");
                    c.Item().Element(_ => { });
                });
            }

            // Title reflects InvoiceType so a Deposit invoice reads
            // "SECURITY DEPOSIT INVOICE" instead of just "INVOICE".
            var headerTitle = m.InvoiceType switch
            {
                "Deposit"        => "SECURITY DEPOSIT INVOICE",
                "Insurance"      => "INSURANCE INVOICE",
                "LateFee"        => "LATE FEE INVOICE",
                "MaintenanceFee" => "MAINTENANCE INVOICE",
                _                => "INVOICE",
            };
            page.Header().Element(c => BrandedHeader(c, headerTitle, m.InvoiceNumber,
                m.InvoiceDate, m.DueDate, m.Status, m.BillingPeriod));

            page.Content().Element(content =>
            {
                content.Layers(layers =>
                {
                    // Diagonal PAID watermark when the invoice is settled.
                    if (string.Equals(m.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                    {
                        layers.Layer().AlignCenter().AlignMiddle().Element(c =>
                            c.Rotate(-22).Text("PAID").FontSize(140)
                             .Bold().FontColor("#10b98122"));  // ~13% alpha green
                    }

                    layers.PrimaryLayer().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(14);

                        // ── Bill To / Property / Unit — three-column block ──
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BILL TO").FontColor(_brand.PrimaryHex)
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                                c.Item().PaddingTop(3).Text(m.TenantName).SemiBold().FontSize(11);
                                if (!string.IsNullOrEmpty(m.CompanyName))
                                    c.Item().Text(m.CompanyName).FontColor("#64748b").FontSize(9);
                                if (!string.IsNullOrEmpty(m.TenantPaciNumber))
                                    c.Item().Text(t =>
                                    {
                                        t.Span("PACI: ").FontColor("#94a3b8").FontSize(8);
                                        t.Span(m.TenantPaciNumber).FontFamily("Courier New").FontSize(9);
                                    });
                            });
                            row.ConstantItem(16);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("PROPERTY").FontColor(_brand.PrimaryHex)
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                                c.Item().PaddingTop(3).Text(m.PropertyName ?? "—").SemiBold().FontSize(11);
                                if (!string.IsNullOrEmpty(m.PropertyPaciNumber))
                                    c.Item().Text(t =>
                                    {
                                        t.Span("PACI: ").FontColor("#94a3b8").FontSize(8);
                                        t.Span(m.PropertyPaciNumber).FontFamily("Courier New").FontSize(9);
                                    });
                            });
                            row.ConstantItem(16);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("UNIT").FontColor(_brand.PrimaryHex)
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                                // Flat number — bolded, top line under the heading.
                                var flatLabel = string.IsNullOrEmpty(m.UnitNumber)
                                    ? "—"
                                    : $"Flat {m.UnitNumber}";
                                c.Item().PaddingTop(3).Text(flatLabel).SemiBold().FontSize(11);

                                // Optional floor — small grey, only when supplied.
                                if (!string.IsNullOrEmpty(m.UnitFloor))
                                    c.Item().Text($"Floor {m.UnitFloor}").FontSize(9).FontColor("#64748b");

                                // Compose address lines from the Property fields,
                                // skipping any that are empty. Two lines max so
                                // the UNIT column doesn't tower over BILL TO.
                                static string? JoinPresent(string sep, params string?[] parts)
                                {
                                    var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                                    return present.Count == 0 ? null : string.Join(sep, present);
                                }

                                var line1 = JoinPresent(", ",
                                    !string.IsNullOrEmpty(m.PropertyBuildingNumber) ? $"Building {m.PropertyBuildingNumber}" : null,
                                    !string.IsNullOrEmpty(m.PropertyStreet)         ? $"Street {m.PropertyStreet}"           : null);
                                if (!string.IsNullOrEmpty(line1))
                                    c.Item().Text(line1).FontSize(9).FontColor("#64748b");

                                var line2 = JoinPresent(", ",
                                    !string.IsNullOrEmpty(m.PropertyBlock) ? $"Block {m.PropertyBlock}" : null,
                                    m.PropertyArea);
                                if (!string.IsNullOrEmpty(line2))
                                    c.Item().Text(line2).FontSize(9).FontColor("#64748b");

                                if (!string.IsNullOrEmpty(m.PropertyGovernorate))
                                    c.Item().Text(m.PropertyGovernorate).FontSize(9).FontColor("#64748b");
                            });
                        });

                        // ── Line items table ──
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(5);  // Description
                                c.RelativeColumn(2);  // Amount
                            });
                            table.Header(h =>
                            {
                                h.Cell().Background(_brand.SecondaryHex).Padding(8)
                                    .Text("DESCRIPTION").FontColor("#ffffff")
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                                h.Cell().Background(_brand.SecondaryHex).Padding(8).AlignRight()
                                    .Text($"AMOUNT ({m.Currency})").FontColor("#ffffff")
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                            });

                            var rows = m.Lines.Count == 0
                                ? new List<InvoicePdfLine>
                                {
                                    new() { Description = "Monthly rent", Amount = m.TotalAmount }
                                }
                                : m.Lines;
                            int idx = 0;
                            foreach (var line in rows)
                            {
                                var bg = idx++ % 2 == 0 ? "#ffffff" : "#fbfbfd";
                                table.Cell().Background(bg).PaddingVertical(8).PaddingHorizontal(8)
                                    .Text(line.Description);
                                table.Cell().Background(bg).PaddingVertical(8).PaddingHorizontal(8)
                                    .AlignRight().Text($"{line.Amount:0.000}");
                            }
                        });

                        // ── Totals block — right-aligned, brand pill on Balance ──
                        col.Item().Row(row =>
                        {
                            row.RelativeItem();  // spacer
                            row.ConstantItem(240).Column(c =>
                            {
                                c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Subtotal").FontColor("#64748b");
                                    r.RelativeItem().AlignRight().Text($"{m.TotalAmount:0.000} {m.Currency}");
                                });
                                c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Paid").FontColor("#64748b");
                                    r.RelativeItem().AlignRight().Text($"({m.PaidAmount:0.000})").FontColor("#10b981");
                                });
                                if (m.PaidAmount > 0 && m.PaidDate.HasValue)
                                {
                                    c.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Paid on").FontColor("#64748b").FontSize(9);
                                        r.RelativeItem().AlignRight()
                                            .Text(m.PaidDate.Value.ToString("dd MMM yyyy"))
                                            .FontColor("#10b981").FontSize(9);
                                    });
                                }
                                c.Item().PaddingTop(6).BorderTop(1).BorderColor("#e2e8f0");
                                c.Item().PaddingTop(8).Background(_brand.PrimaryHex)
                                    .Padding(10).Row(r =>
                                    {
                                        r.RelativeItem().Text("BALANCE DUE")
                                            .FontColor("#ffffff").Bold().FontSize(11)
                                            .LetterSpacing(0.06f);
                                        r.RelativeItem().AlignRight()
                                            .Text($"{m.BalanceAmount:0.000} {m.Currency}")
                                            .FontColor("#ffffff").Bold().FontSize(13);
                                    });
                            });
                        });

                        // ── Payment instructions ──
                        if (!IsBankEmpty())
                        {
                            col.Item().PaddingTop(6).BorderTop(1).BorderColor("#e2e8f0")
                                .PaddingTop(10).Column(c =>
                                {
                                    c.Item().Text("PAYMENT INSTRUCTIONS")
                                        .FontColor(_brand.PrimaryHex).Bold().FontSize(9)
                                        .LetterSpacing(0.06f);
                                    if (!string.IsNullOrEmpty(_brand.Bank.Name))
                                        c.Item().PaddingTop(3).Text(t =>
                                        {
                                            t.Span("Bank: ").FontColor("#64748b");
                                            t.Span(_brand.Bank.Name);
                                            if (!string.IsNullOrEmpty(_brand.Bank.AccountHolder))
                                            {
                                                t.Span("  ·  Account: ").FontColor("#64748b");
                                                t.Span(_brand.Bank.AccountHolder!);
                                            }
                                        });
                                    if (!string.IsNullOrEmpty(_brand.Bank.Iban))
                                        c.Item().Text(t =>
                                        {
                                            t.Span("IBAN: ").FontColor("#64748b");
                                            t.Span(_brand.Bank.Iban).FontFamily("Courier New").SemiBold();
                                        });
                                    if (!string.IsNullOrEmpty(_brand.Bank.SwiftBic))
                                        c.Item().Text(t =>
                                        {
                                            t.Span("SWIFT: ").FontColor("#64748b");
                                            t.Span(_brand.Bank.SwiftBic).FontFamily("Courier New");
                                        });
                                    if (!string.IsNullOrEmpty(_brand.Bank.KnetReceiver))
                                        c.Item().Text(t =>
                                        {
                                            t.Span("K-NET payable to: ").FontColor("#64748b");
                                            t.Span(_brand.Bank.KnetReceiver);
                                        });
                                    c.Item().PaddingTop(3).Text(t =>
                                    {
                                        t.Span("Reference: ").FontColor("#64748b");
                                        t.Span(m.InvoiceNumber).SemiBold();
                                        t.Span("  (include on every transfer)").FontColor("#94a3b8").FontSize(8);
                                    });
                                });
                        }

                        if (!string.IsNullOrWhiteSpace(m.Notes))
                        {
                            col.Item().PaddingTop(8).Text(m.Notes).Italic().FontColor("#64748b").FontSize(9);
                        }
                    });
                });
            });

            page.Footer().Element(BrandedFooter);
        });
    }).GeneratePdf();

    public byte[] RenderReceipt(ReceiptPdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(28);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor(_brand.SecondaryHex));

            page.Header().Element(c => BrandedHeader(c, "RECEIPT", m.ReceiptNumber,
                m.PaymentDate, dueDate: null, status: "PAID"));

            page.Content().Element(content =>
            {
                content.Layers(layers =>
                {
                    // Receipts are always paid by definition — diagonal stamp on every one.
                    layers.Layer().AlignCenter().AlignMiddle().Element(c =>
                        c.Rotate(-22).Text("PAID").FontSize(120)
                         .Bold().FontColor("#10b98122"));

                    layers.PrimaryLayer().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(12);
                        col.Item().Text(t =>
                        {
                            t.Span("Received from ").FontColor("#64748b");
                            t.Span(m.TenantName).SemiBold();
                        });
                        col.Item().Text(t =>
                        {
                            t.Span($"For {m.PropertyName} · Unit {m.UnitNumber}").FontColor("#64748b");
                        });

                        col.Item().PaddingVertical(6).Background(_brand.PrimaryHex)
                            .Padding(14).Column(c =>
                            {
                                c.Item().Text("AMOUNT RECEIVED").FontColor("#ffffff")
                                    .FontSize(9).Bold().LetterSpacing(0.06f);
                                c.Item().PaddingTop(2)
                                    .Text($"{m.AmountPaid:0.000} {m.Currency}")
                                    .FontColor("#ffffff").Bold().FontSize(20);
                            });

                        col.Item().Column(c =>
                        {
                            c.Item().Text(t =>
                            {
                                t.Span("Payment method: ").FontColor("#64748b");
                                t.Span(m.PaymentMethod).SemiBold();
                            });
                            c.Item().Text(t =>
                            {
                                t.Span("Date: ").FontColor("#64748b");
                                t.Span(m.PaymentDate.ToString("dd MMM yyyy HH:mm")).SemiBold();
                            });
                            if (!string.IsNullOrEmpty(m.TransactionReference))
                                c.Item().Text(t =>
                                {
                                    t.Span("Reference: ").FontColor("#64748b");
                                    t.Span(m.TransactionReference).FontFamily("Courier New").SemiBold();
                                });
                            if (!string.IsNullOrEmpty(m.InvoiceNumber))
                                c.Item().Text(t =>
                                {
                                    t.Span("Invoice: ").FontColor("#64748b");
                                    t.Span(m.InvoiceNumber).SemiBold();
                                });
                        });

                        if (!string.IsNullOrWhiteSpace(m.Notes))
                            col.Item().PaddingTop(6).Text(m.Notes).Italic().FontColor("#64748b").FontSize(9);
                    });
                });
            });

            page.Footer().Element(BrandedFooter);
        });
    }).GeneratePdf();

    /// <summary>
    /// Branded header used by Invoice + Receipt. Logo (when present on disk)
    /// left + company info middle + document meta right, separated by a thin
    /// brand-coloured rule.
    /// </summary>
    private void BrandedHeader(IContainer container, string title, string number,
        DateTime dateA, DateTime? dueDate, string? status, string? billingPeriod = null)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Row(row =>
            {
                // Logo column — fixed width so the layout doesn't shift if
                // the file is missing. Renderer falls back to a blank cell
                // and the company-name column expands naturally.
                row.ConstantItem(90).Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(_brand.LogoPath) && File.Exists(_brand.LogoPath))
                    {
                        c.Item().Height(70).Image(_brand.LogoPath).FitArea();
                    }
                });
                row.ConstantItem(12);  // spacer
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(_brand.CompanyName).SemiBold().FontSize(15)
                        .FontColor(_brand.SecondaryHex);
                    if (!string.IsNullOrEmpty(_brand.TagLine))
                        c.Item().Text(_brand.TagLine!).FontSize(9).FontColor(_brand.PrimaryHex)
                            .LetterSpacing(0.06f);
                    if (!string.IsNullOrEmpty(_brand.ContactAddress))
                        c.Item().PaddingTop(3).Text(_brand.ContactAddress!).FontSize(8).FontColor("#64748b");
                    var contactBits = new List<string>();
                    if (!string.IsNullOrEmpty(_brand.ContactPhone)) contactBits.Add(_brand.ContactPhone!);
                    if (!string.IsNullOrEmpty(_brand.ContactEmail)) contactBits.Add(_brand.ContactEmail!);
                    if (!string.IsNullOrEmpty(_brand.Website))      contactBits.Add(_brand.Website!);
                    if (contactBits.Count > 0)
                        c.Item().Text(string.Join("  ·  ", contactBits)).FontSize(8).FontColor("#64748b");
                });
                row.ConstantItem(180).Column(c =>
                {
                    c.Item().AlignRight().Text(title).Bold().FontSize(22).FontColor(_brand.PrimaryHex);
                    c.Item().AlignRight().Text(t =>
                    {
                        t.Span("# ").FontColor("#94a3b8");
                        t.Span(number).SemiBold();
                    });
                    c.Item().PaddingTop(4).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(9));
                        t.Span("Issued: ").FontColor("#64748b");
                        t.Span(dateA.ToString("dd MMM yyyy"));
                    });
                    if (dueDate.HasValue)
                    {
                        // Highlight the due date for non-Paid statuses so the
                        // recipient can't miss it.
                        var isOverdue = string.Equals(status, "Overdue", StringComparison.OrdinalIgnoreCase);
                        var isPending = string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase);
                        c.Item().AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(9));
                            t.Span("Due: ").FontColor("#64748b");
                            var due = t.Span(dueDate.Value.ToString("dd MMM yyyy")).SemiBold();
                            if (isOverdue) due.FontColor("#dc2626");
                            else if (isPending) due.FontColor("#d97706");
                        });
                    }
                    if (!string.IsNullOrEmpty(billingPeriod))
                    {
                        c.Item().AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(9));
                            t.Span("Period: ").FontColor("#64748b");
                            t.Span(billingPeriod).SemiBold();
                        });
                    }
                    if (!string.IsNullOrEmpty(status))
                    {
                        var (bg, fg) = status.ToLowerInvariant() switch
                        {
                            "paid"    => ("#10b981", "#ffffff"),
                            "overdue" => ("#dc2626", "#ffffff"),
                            "pending" => ("#f59e0b", "#ffffff"),
                            _         => ("#e2e8f0", "#475569"),
                        };
                        c.Item().PaddingTop(4).AlignRight().Background(bg)
                            .PaddingVertical(2).PaddingHorizontal(8)
                            .Text(status.ToUpperInvariant()).FontColor(fg).Bold().FontSize(8)
                            .LetterSpacing(0.08f);
                    }
                });
            });
            // Thin brand-coloured rule
            col.Item().PaddingTop(8).Height(2).Background(_brand.PrimaryHex);
        });
    }

    /// <summary>
    /// Branded footer with terms + page numbers. Compact so it doesn't
    /// elbow the content area on multi-page documents.
    /// </summary>
    private void BrandedFooter(IContainer container)
    {
        container.PaddingTop(8).BorderTop(1).BorderColor("#e2e8f0").Column(col =>
        {
            if (!string.IsNullOrWhiteSpace(_brand.FooterTerms))
            {
                col.Item().PaddingTop(4).Text(_brand.FooterTerms!)
                    .FontSize(7).FontColor("#94a3b8");
            }
            col.Item().PaddingTop(4).Row(r =>
            {
                r.RelativeItem().Text(_brand.CompanyName).FontSize(7).FontColor("#94a3b8");
                r.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Page ").FontSize(7).FontColor("#94a3b8");
                    t.CurrentPageNumber().FontSize(7).FontColor("#94a3b8");
                    t.Span(" of ").FontSize(7).FontColor("#94a3b8");
                    t.TotalPages().FontSize(7).FontColor("#94a3b8");
                });
            });
        });
    }

    /// <summary>True when no bank/K-NET fields are populated, so we can skip
    /// the whole Payment Instructions section gracefully.</summary>
    private bool IsBankEmpty() =>
        string.IsNullOrEmpty(_brand.Bank.Name)
        && string.IsNullOrEmpty(_brand.Bank.Iban)
        && string.IsNullOrEmpty(_brand.Bank.SwiftBic)
        && string.IsNullOrEmpty(_brand.Bank.KnetReceiver);

    /// <summary>
    /// Back-compat shim for the executive dashboard and tenant statement
    /// renderers which still call the old <c>Header(title, number, name)</c>
    /// shape. Delegates to <see cref="BrandedHeader"/> so all four documents
    /// share the brand (logo, colours, contact info).
    /// </summary>
    private void Header(IContainer container, string title, string number, string? _legacyCompanyName)
        => BrandedHeader(container, title, number,
            dateA: DateTime.Today, dueDate: null, status: null);

    // ─── Sprint 3 — executive dashboard PDF ─────────────────────────────
    public byte[] RenderExecutiveDashboard(ExecutiveDashboardPdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor("#1e293b"));

            page.Header().Element(c => Header(c, "EXECUTIVE DASHBOARD",
                $"{m.FromDate:dd MMM yyyy} – {m.ToDate:dd MMM yyyy}", m.CompanyName));

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(14);

                // 4 KPI tiles in a row
                col.Item().Row(r =>
                {
                    r.Spacing(8);
                    r.RelativeItem().Element(c => Kpi(c, "Collected", $"{m.Collected:N3} {m.Currency}",
                        m.CollectedDelta == 0 ? null : $"{(m.CollectedDelta > 0 ? "▲" : "▼")} {Math.Abs(m.CollectedDelta):0.0}%"));
                    r.RelativeItem().Element(c => Kpi(c, "Outstanding", $"{m.Outstanding:N3} {m.Currency}", null));
                    r.RelativeItem().Element(c => Kpi(c, "Overdue", m.OverdueCount.ToString(), null));
                    r.RelativeItem().Element(c => Kpi(c, "Occupancy",
                        $"{m.OccupancyPercent:0.0}%", $"{m.OccupiedUnits} / {m.TotalUnits} units"));
                });

                // 12-month revenue trend (table — PDF doesn't draw charts)
                col.Item().Text("Revenue — last 12 months").SemiBold().FontSize(12).FontColor("#475569");
                col.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in m.Trend) cols.RelativeColumn();
                    });
                    foreach (var (label, _) in m.Trend)
                        t.Cell().AlignCenter().Text(label).FontSize(8).FontColor("#94a3b8");
                    foreach (var (_, value) in m.Trend)
                        t.Cell().AlignCenter().Text($"{value:N0}").SemiBold();
                });

                // Status breakdown + top properties side by side
                col.Item().Row(r =>
                {
                    r.Spacing(12);
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Invoices by status").SemiBold().FontSize(12).FontColor("#475569");
                        c.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); });
                            t.Header(h =>
                            {
                                h.Cell().Text("Status").SemiBold().FontColor("#64748b").FontSize(9);
                                h.Cell().AlignRight().Text("Count").SemiBold().FontColor("#64748b").FontSize(9);
                            });
                            foreach (var (status, count) in m.StatusBreakdown)
                            {
                                t.Cell().Text(status);
                                t.Cell().AlignRight().Text(count.ToString());
                            }
                        });
                    });
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Top 5 properties").SemiBold().FontSize(12).FontColor("#475569");
                        c.Item().Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.RelativeColumn(2); });
                            t.Header(h =>
                            {
                                h.Cell().Text("Property").SemiBold().FontColor("#64748b").FontSize(9);
                                h.Cell().AlignRight().Text("Collected").SemiBold().FontColor("#64748b").FontSize(9);
                            });
                            foreach (var (name, value) in m.TopProperties)
                            {
                                t.Cell().Text(name);
                                t.Cell().AlignRight().Text($"{value:N3} {m.Currency}");
                            }
                        });
                    });
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Generated ").FontColor("#94a3b8").FontSize(8);
                t.Span(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm")).FontColor("#94a3b8").FontSize(8);
                t.Span("  ·  page ").FontColor("#94a3b8").FontSize(8);
                t.CurrentPageNumber().FontColor("#94a3b8").FontSize(8);
                t.Span(" / ").FontColor("#94a3b8").FontSize(8);
                t.TotalPages().FontColor("#94a3b8").FontSize(8);
            });
        });
    }).GeneratePdf();

    private static void Kpi(IContainer container, string label, string value, string? subtitle)
    {
        container.Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(10).Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor("#94a3b8").SemiBold();
            col.Item().Text(value).FontSize(14).SemiBold().FontColor("#0f172a");
            if (!string.IsNullOrEmpty(subtitle))
                col.Item().Text(subtitle).FontSize(8).FontColor("#64748b");
        });
    }

    // ─── Sprint 3 (Tenants) — payment statement PDF ─────────────────────
    public byte[] RenderTenantStatement(TenantStatementPdfModel m) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(s => s.FontSize(10).FontColor("#1e293b"));

            page.Header().Element(c => Header(c, "TENANT STATEMENT",
                $"#{m.TenantId:D6}", m.CompanyName));

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(14);

                // Tenant + outstanding side-by-side
                col.Item().Row(r =>
                {
                    r.Spacing(12);
                    r.RelativeItem(2).Border(1).BorderColor("#e2e8f0").Padding(10).Column(c =>
                    {
                        c.Item().Text(m.TenantName).FontSize(14).SemiBold();
                        if (!string.IsNullOrEmpty(m.Email))
                            c.Item().Text($"Email: {m.Email}").FontSize(9).FontColor("#475569");
                        if (!string.IsNullOrEmpty(m.Phone))
                            c.Item().Text($"Phone: {m.Phone}").FontSize(9).FontColor("#475569");
                        if (!string.IsNullOrEmpty(m.PACINumber))
                            c.Item().Text($"Client PACI: {m.PACINumber}").FontSize(9).FontColor("#475569");
                        c.Item().Text($"Statement as of {m.GeneratedAt:dd MMM yyyy HH:mm} UTC")
                            .FontSize(8).FontColor("#94a3b8").Italic();
                    });
                    r.RelativeItem().Element(c => Kpi(c, "Outstanding",
                        $"{m.Outstanding:N3} {m.Currency}", m.Outstanding > 0 ? "balance due" : "all settled"));
                });

                // Active leases
                col.Item().Text("Active leases").SemiBold().FontSize(12).FontColor("#475569");
                if (m.Leases.Count == 0)
                {
                    col.Item().PaddingVertical(8).Text("No active leases on file.")
                        .FontColor("#94a3b8").Italic();
                }
                else
                {
                    col.Item().Border(1).BorderColor("#e2e8f0").Padding(0).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(3); cd.RelativeColumn(); cd.RelativeColumn(2);
                            cd.RelativeColumn(2); cd.RelativeColumn(2);
                        });
                        t.Header(h =>
                        {
                            void H(string label) => h.Cell().Background("#f8fafc")
                                .Padding(6).Text(label).SemiBold().FontSize(9).FontColor("#64748b");
                            H("Property"); H("Unit"); H("Start"); H("End"); H("Monthly Rent");
                        });
                        foreach (var l in m.Leases)
                        {
                            t.Cell().Padding(6).Text(l.Property);
                            t.Cell().Padding(6).Text(l.Unit);
                            t.Cell().Padding(6).Text($"{l.LeaseStartDate:dd MMM yyyy}");
                            t.Cell().Padding(6).Text($"{l.LeaseEndDate:dd MMM yyyy}");
                            t.Cell().Padding(6).AlignRight().Text($"{l.MonthlyRent:N3} {m.Currency}");
                        }
                    });
                }

                // Recent payments
                col.Item().Text($"Recent payments (last {m.RecentPayments.Count})")
                    .SemiBold().FontSize(12).FontColor("#475569");
                if (m.RecentPayments.Count == 0)
                {
                    col.Item().PaddingVertical(8).Text("No completed payments recorded.")
                        .FontColor("#94a3b8").Italic();
                }
                else
                {
                    col.Item().Border(1).BorderColor("#e2e8f0").Padding(0).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(2); cd.RelativeColumn(3);
                        });
                        t.Header(h =>
                        {
                            void H(string label) => h.Cell().Background("#f8fafc")
                                .Padding(6).Text(label).SemiBold().FontSize(9).FontColor("#64748b");
                            H("Date"); H("Amount"); H("Method"); H("Reference");
                        });
                        foreach (var p in m.RecentPayments)
                        {
                            t.Cell().Padding(6).Text($"{p.PaymentDate:dd MMM yyyy}");
                            t.Cell().Padding(6).AlignRight().Text($"{p.AmountPaid:N3} {m.Currency}");
                            t.Cell().Padding(6).Text(p.Method);
                            t.Cell().Padding(6).Text(p.Reference ?? "—").FontColor("#64748b").FontSize(9);
                        }
                    });
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Generated ").FontColor("#94a3b8").FontSize(8);
                t.Span(m.GeneratedAt.ToString("dd MMM yyyy HH:mm")).FontColor("#94a3b8").FontSize(8);
                t.Span("  ·  page ").FontColor("#94a3b8").FontSize(8);
                t.CurrentPageNumber().FontColor("#94a3b8").FontSize(8);
                t.Span(" / ").FontColor("#94a3b8").FontSize(8);
                t.TotalPages().FontColor("#94a3b8").FontSize(8);
            });
        });
    }).GeneratePdf();
}
